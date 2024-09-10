// -----------------------------------------------------------------------
// <copyright file="TcpTransportActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Implementation.Fusing;
using TurboMqtt.Client;
using TurboMqtt.IO.Internal;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using Debug = System.Diagnostics.Debug;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Actor responsible for managing the TCP transport layer for MQTT.
/// </summary>
internal sealed class TcpTransportActor : UntypedActor
{
    #region Internal Types

    /// <summary>
    /// Mutable state shared between the TcpTransport and the TcpTransportActor.
    /// </summary>
    /// <remarks>
    /// Can values can only be set by the TcpTransportActor itself.
    /// </remarks>
    public sealed class ConnectionState
    {
        public ConnectionState(IDuplexPipe dataPipes,
            Task<DisconnectReasonCode> whenTerminated, int maxFrameSize, Task waitForPendingWrites)
        {
            Pipe = dataPipes;
            WhenTerminated = whenTerminated;
            MaxFrameSize = maxFrameSize;
            WaitForPendingWrites = waitForPendingWrites;
        }

        private volatile ConnectionStatus _status = ConnectionStatus.NotStarted;

        public ConnectionStatus Status
        {
            get => _status;
            set => _status = value;
        }

        public CancellationTokenSource ShutDownCts { get; set; } = new();

        public int MaxFrameSize { get; }

        public Task<DisconnectReasonCode> WhenTerminated { get; }

        public Task WaitForPendingWrites { get; }

        public IDuplexPipe Pipe { get; }

        public PipeWriter Writer => Pipe.Output;

        /// <summary>
        /// Used to read data from the underlying transport.
        /// </summary>
        public PipeReader Reader => Pipe.Input;
    }

    /// <summary>
    /// Has us create the client and share state, but the socket is not open yet.
    /// </summary>
    public sealed class CreateTcpTransport
    {
        private CreateTcpTransport()
        {
        }

        public static CreateTcpTransport Instance { get; } = new();
    }

    public sealed record DoConnect(CancellationToken Cancel);

    public sealed record ConnectResult(ConnectionStatus Status, string ReasonMessage);

    public sealed record DoClose(CancellationToken Cancel) : IDeadLetterSuppression;

    /// <summary>
    /// We are done reading from the socket.
    /// </summary>
    public sealed class ReadFinished : IDeadLetterSuppression
    {
        private ReadFinished()
        {
        }

        public static ReadFinished Instance { get; } = new();
    }

    /// <summary>
    /// In the event that our connection gets aborted by the broker, we will send ourselves this message.
    /// </summary>
    /// <param name="Reason"></param>
    /// <param name="ReasonMessage"></param>
    public sealed record ConnectionUnexpectedlyClosed(DisconnectReasonCode Reason, string ReasonMessage);

    #endregion

    public MqttClientTcpOptions TcpOptions { get; }
    public int MaxFrameSize { get; }

    public ConnectionState State { get; private set; }

    private Socket? _tcpClient;

    private readonly IDuplexPipe _transport;

    private readonly IDuplexPipe _application;

    private readonly TaskCompletionSource<DisconnectReasonCode> _whenTerminated = new();
    private readonly TaskCompletionSource _writingCompleted = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public TcpTransportActor(MqttClientTcpOptions tcpOptions)
    {
        TcpOptions = tcpOptions;
        MaxFrameSize = tcpOptions.MaxFrameSize;

        var pipeOptions = new PipeOptions(pauseWriterThreshold: ScaleBufferSize(MaxFrameSize),
            resumeWriterThreshold: ScaleBufferSize(MaxFrameSize) / 2,
            useSynchronizationContext: false);
        var pipes = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);

        _transport = pipes.Transport;
        _application = pipes.Application;

        State = new ConnectionState(pipes.Application, _whenTerminated.Task,
            MaxFrameSize, _writingCompleted.Task); // we signal completion when _writesToTransport is done
    }

    /*
     * FSM:
     * OnReceive (nothing has happened) --> CreateTcpTransport --> TransportCreated BECOME Connecting
     * Connecting --> DoConnect --> Connecting (already connecting) --> ConnectResult (Connected) BECOME Running
     * Running --> DoWriteToPipeAsync --> Running (read data from socket) --> DoWriteToSocketAsync --> Running (write data to socket)
     */

    /// <summary>
    /// Performs the max buffer size scaling for the socket.
    /// </summary>
    /// <param name="maxFrameSize">The maximum size of a single frame.</param>
    internal static int ScaleBufferSize(int maxFrameSize)
    {
        // if the max frame size is under 128kb, scale it up to 512kb
        if (maxFrameSize <= 128 * 1024)
            return 512 * 1024;

        // between 128kb and 1mb, scale it up to 2mb
        if (maxFrameSize <= 1024 * 1024)
            return 2 * 1024 * 1024;

        // if the max frame size is above 1mb, 2x it
        return maxFrameSize * 2;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateTcpTransport when State.Status == ConnectionStatus.NotStarted:
            {
                // first things first - attempt to create the socket
                CreateTcpClient();

                // return the transport to the client
                var tcpTransport = new TcpTransport(_log, State, Self);
                Sender.Tell(tcpTransport);
                _log.Debug("Created new TCP transport for client connecting to [{0}:{1}]", TcpOptions.Host,
                    TcpOptions.Port);
                Become(TransportCreated);
                break;
            }
            case CreateTcpTransport when State.Status != ConnectionStatus.NotStarted:
            {
                _log.Warning("Attempted to create a TCP transport when one already exists.");
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private void CreateTcpClient()
    {
        if (TcpOptions.AddressFamily == AddressFamily.Unspecified)
            _tcpClient = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 2), // give us a little time to flush the socket
                ReceiveBufferSize = ScaleBufferSize(TcpOptions.MaxFrameSize),
                SendBufferSize = ScaleBufferSize(TcpOptions.MaxFrameSize)
            };
        else
            _tcpClient = new Socket(TcpOptions.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = ScaleBufferSize(TcpOptions.MaxFrameSize),
                SendBufferSize = ScaleBufferSize(TcpOptions.MaxFrameSize),
                NoDelay = true,
                LingerState = new LingerOption(true, 2) // give us a little time to flush the socket
            };
    }

    private async Task DoConnectAsync(IPAddress[] addresses, int port, IActorRef destination,
        CancellationToken ct = default)
    {
        ConnectResult connectResult;
        try
        {
            if (addresses.Length == 0)
                throw new ArgumentException("No IP addresses provided to connect to.", nameof(addresses));

            Debug.Assert(_tcpClient != null, nameof(_tcpClient) + " != null");
            await _tcpClient.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
            connectResult = new ConnectResult(ConnectionStatus.Connected, "Connected.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);
            connectResult = new ConnectResult(ConnectionStatus.Failed, ex.Message);
        }

        // let both parties know the result of the connection attempt
        destination.Tell(connectResult);
        _closureSelf.Tell(connectResult);
    }

    private readonly IActorRef _closureSelf = Context.Self;

    private void TransportCreated(object message)
    {
        switch (message)
        {
            case DoConnect connect when State.Status == ConnectionStatus.NotStarted:
            {
                RunTask(async () =>
                {
                    _log.Info("Attempting to connect to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);

                    var sender = Sender;

                    // set status to connecting
                    State.Status = ConnectionStatus.Connecting;

                    await ResolveAndConnect(connect.Cancel);
                    return;

                    // need to resolve DNS to an IP address
                    async Task ResolveAndConnect(CancellationToken ct)
                    {
                        var resolved = await Dns.GetHostAddressesAsync(TcpOptions.Host, ct).ConfigureAwait(false);

                        if (_log.IsDebugEnabled)
                            _log.Debug("Attempting to connect to [{0}:{1}] - resolved to [{2}]", TcpOptions.Host,
                                TcpOptions.Port,
                                string.Join(", ", resolved.Select(c => c.ToString())));

                        await DoConnectAsync(resolved, TcpOptions.Port, sender, ct).ConfigureAwait(false);
                    }
                });

                break;
            }
            case DoConnect:
            {
                var warningMsg = State.Status == ConnectionStatus.Connecting
                    ? "Already attempting to connect to [{0}:{1}]"
                    : "Already connected to [{0}:{1}]";

                var formatted = string.Format(warningMsg, TcpOptions.Host, TcpOptions.Port);
                Sender.Tell(new ConnectResult(ConnectionStatus.Connecting, formatted));
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Connected }:
            {
                _log.Info("Successfully connected to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);
                State.Status = ConnectionStatus.Connected;

                BecomeRunning();
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Failed }:
            {
                _log.Error("Failed to connect to [{0}:{1}]: {2}", TcpOptions.Host, TcpOptions.Port, message);
                State.Status = ConnectionStatus.Failed;
                Context.Stop(Self);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private void BecomeRunning()
    {
        Become(Running);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        DoWriteToOutputPipeAsync(State.ShutDownCts.Token);
        DoWriteToSocketAsync(State.ShutDownCts.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    private async Task DoWriteToSocketAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var readResult = await _transport.Input.ReadAsync(ct).ConfigureAwait(false);
 

                var buffer = readResult.Buffer;
                
                var totalSent = 0;
                try
                {
                    var seqPosition = buffer.Start;
                    
                    while (buffer.TryGet(ref seqPosition, out var workingBuffer) && _tcpClient is { Connected: true })
                    {
                        var sent = await _tcpClient!.SendAsync(workingBuffer, ct)
                            .ConfigureAwait(false);
                        
                        totalSent += sent;
                        var position = buffer.GetPosition(workingBuffer.Length);
                        _transport.Input.AdvanceTo(seqPosition, position);
                        if (sent == 0)
                        {
                            _log.Warning("Failed to write to socket - no bytes written.");
                            _closureSelf.Tell(ReadFinished.Instance);
                            goto WritesFinished;
                        }
                    }
                    
                    if (readResult.IsCompleted)
                    {
                        // reader is done reading
                        break;
                    }
                }
                finally
                {
                    // free the pooled buffer
                    _log.Debug("Sent {0} bytes to socket.", totalSent);
                }
            }
            catch (OperationCanceledException)
            {
                // we're being shut down
                _log.Debug("Shutting down write to socket.");
                _closureSelf.Tell(ReadFinished.Instance);
                goto WritesFinished;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to write to socket.");
                // we are done writing
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(DisconnectReasonCode.UnspecifiedError, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                goto WritesFinished;
            }
        }

        WritesFinished:
            await _application.Output.CompleteAsync(); // can't write anymore either
            await _transport.Output.CompleteAsync();
            _writingCompleted.TrySetResult(); // signal that we are done writing
    }

    private async Task DoWriteToOutputPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var memory = _transport.Output.GetMemory(TcpOptions.MaxFrameSize / 4);
            try
            {
                int bytesRead = await _tcpClient!.ReceiveAsync(memory, SocketFlags.None, ct);
                if (bytesRead == 0)
                {
                    // we are done reading - socket was gracefully closed
                    _closureSelf.Tell(ReadFinished.Instance);
                    // abort ongoing reads and writes, but don't shutdown the transport
                    await State.ShutDownCts.CancelAsync();
                    return;
                }

                _transport.Output.Advance(bytesRead);
            }
            catch (OperationCanceledException)
            {
                // no need to log here
                _closureSelf.Tell(ReadFinished.Instance);
            }
            catch (Exception ex)
            {
                // this is a debug-level issue
                _log.Debug(ex, "Failed to read from socket.");
                // we are done reading
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(DisconnectReasonCode.UnspecifiedError, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                return;
            }

            // make data available to PipeReader
            var result = await _transport.Output.FlushAsync(ct);
            if (result.IsCompleted)
            {
                _closureSelf.Tell(ReadFinished.Instance);
                return;
            }
        }

        // we are done reading from the transport
        await _transport.Output.CompleteAsync();
    }

    private void Running(object message)
    {
        switch (message)
        {
            case DoClose: // we are closing
            {
                _ = CleanUpGracefully(true);
                break;
            }
            case ReadFinished: // server closed us
            {
                _ = CleanUpGracefully(true); // idempotent
                break;
            }
            case ConnectionUnexpectedlyClosed closed:
            {
                // we got aborted
                _log.Warning("Connection to [{0}:{1}] was unexpectedly closed: {2}", TcpOptions.Host, TcpOptions.Port,
                    closed.ReasonMessage);
                _ = CleanUpGracefully(true); // idempotent
                break;
            }
        }
    }

    private async Task CleanUpGracefully(bool waitOnReads = false)
    {
        // add a simulated DisconnectPacket to help ensure the stream gets terminated
        // _readsFromTransport.Writer.TryWrite(
        //     DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1));

        State.Status = ConnectionStatus.Disconnected;

        // no more writes to transport
        await _transport.Input.CompleteAsync();

        // wait for any pending writes to finish
        await State.WaitForPendingWrites;

        if (waitOnReads)
        {
            // wait for any reads to finish (should be terminated by Akka.Streams once the `DisconnectPacket` is processed.)
            await _application.Input.CompleteAsync();
        }
        else // if we're not waiting on reads, just complete the reader
        {
            await _application.Output.CompleteAsync();
        }

        _closureSelf.Tell(PoisonPill.Instance);
    }

    private void DisposeSocket(ConnectionStatus newStatus)
    {
        _log.Info("Disposing of TCP client socket.");
        if (_tcpClient is null)
            return; // already disposed

        try
        {
            State.Status = newStatus;


            // stop reading from the socket
            State.ShutDownCts.Cancel();

            _transport.Input.Complete();
            _transport.Output.Complete();
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to cleanly dispose of TCP client and stream.");
        }
        finally
        {
            _tcpClient = null;
        }
    }

    /// <summary>
    /// This is for when we run out of retries or we were explicitly told to close the connection.
    /// </summary>
    private void FullShutdown(DisconnectReasonCode reason = DisconnectReasonCode.NormalDisconnection)
    {
        // mark the channels as complete (should have already been done by the time we get here, but doesn't hurt)
        _application.Output.Complete();
        _transport.Output.Complete();

        var newStatus = reason switch
        {
            DisconnectReasonCode.ServerShuttingDown => ConnectionStatus.Disconnected,
            DisconnectReasonCode.NormalDisconnection => ConnectionStatus.Disconnected,
            DisconnectReasonCode.UnspecifiedError => ConnectionStatus.Failed,
            _ => ConnectionStatus.Aborted
        };

        DisposeSocket(newStatus);

        // let upstairs know we're done
        _whenTerminated.TrySetResult(reason);
    }

    protected override void PostStop()
    {
        FullShutdown();
    }
}