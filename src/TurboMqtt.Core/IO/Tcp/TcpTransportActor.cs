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
using TurboMqtt.Core.Client;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using Debug = System.Diagnostics.Debug;

namespace TurboMqtt.Core.IO;

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
        public ConnectionState(ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> writer,
            ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> reader,
            Task<ConnectionTerminatedReason> whenTerminated, int maxFrameSize, Task<bool> waitForPendingWrites)
        {
            Writer = writer;
            Reader = reader;
            WhenTerminated = whenTerminated;
            MaxFrameSize = maxFrameSize;
            WaitForPendingWrites = waitForPendingWrites;
        }

        public ConnectionStatus Status { get; set; } = ConnectionStatus.NotStarted;

        public CancellationTokenSource ShutDownCts { get; set; } = new();

        public int MaxFrameSize { get; }

        public Task<ConnectionTerminatedReason> WhenTerminated { get; }

        public Task<bool> WaitForPendingWrites { get; }

        public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }

        /// <summary>
        /// Used to read data from the underlying transport.
        /// </summary>
        public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
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
    public sealed record ConnectionUnexpectedlyClosed(ConnectionTerminatedReason Reason, string ReasonMessage);

    #endregion

    public MqttClientTcpOptions TcpOptions { get; }
    public int MaxFrameSize { get; }

    public ConnectionState State { get; private set; }

    private Socket? _tcpClient;

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly TaskCompletionSource<ConnectionTerminatedReason> _whenTerminated = new();
    private readonly TaskCompletionSource<bool> _waitForPendingWrites = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly Pipe _pipe;

    public TcpTransportActor(MqttClientTcpOptions tcpOptions)
    {
        TcpOptions = tcpOptions;
        MaxFrameSize = tcpOptions.MaxFrameSize;

        State = new ConnectionState(_writesToTransport.Writer, _readsFromTransport.Reader, _whenTerminated.Task,
            MaxFrameSize, _waitForPendingWrites.Task);

        _pipe = new Pipe(new PipeOptions(pauseWriterThreshold: MaxFrameSize, resumeWriterThreshold: MaxFrameSize / 2,
            useSynchronizationContext: false));
    }

    /*
     * FSM:
     * OnReceive (nothing has happened) --> CreateTcpTransport --> TransportCreated BECOME Connecting
     * Connecting --> DoConnect --> Connecting (already connecting) --> ConnectResult (Connected) BECOME Running
     * Running --> DoWriteToPipeAsync --> Running (read data from socket) --> DoWriteToSocketAsync --> Running (write data to socket)
     */

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
                ReceiveBufferSize = TcpOptions.MaxFrameSize * 2,
                SendBufferSize = TcpOptions.MaxFrameSize * 2
            };
        else
            _tcpClient = new Socket(TcpOptions.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = TcpOptions.MaxFrameSize * 2,
                SendBufferSize = TcpOptions.MaxFrameSize * 2,
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
            case DoConnect connect when State.Status != ConnectionStatus.Connected ||
                                        State.Status != ConnectionStatus.Connecting:
            {
                _log.Info("Attempting to connect to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);

                var sender = Sender;

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

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                ResolveAndConnect(connect.Cancel);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                // set status to connecting
                State.Status = ConnectionStatus.Connecting;
                break;
            }
            case DoConnect:
            {
                var warningMsg = State.Status == ConnectionStatus.Connecting
                    ? "Already attempting to connect to [{0}:{1}]"
                    : "Already connected to [{0}:{1}]";

                var formatted = string.Format(warningMsg, TcpOptions.Host, TcpOptions.Port);

                _log.Warning(formatted);
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
        DoWriteToPipeAsync(State.ShutDownCts.Token);
        ReadFromPipeAsync(State.ShutDownCts.Token);
        DoWriteToSocketAsync(State.ShutDownCts.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    private async Task DoWriteToSocketAsync(CancellationToken ct)
    {
        while (!_writesToTransport.Reader.Completion.IsCompleted)
        {
            try
            {
                while (await _writesToTransport.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                while (_writesToTransport.Reader.TryRead(out var item))
                {
                    var (buffer, readableBytes) = item;
                    try
                    {
                        var workingBuffer = buffer.Memory;
                        while (readableBytes > 0)
                        {
                            var sent = await _tcpClient!.SendAsync(workingBuffer.Slice(0, readableBytes), ct)
                                .ConfigureAwait(false);
                            if (sent == 0)
                            {
                                _log.Warning("Failed to write to socket - no bytes written.");
                                _closureSelf.Tell(ReadFinished.Instance);
                                goto WritesFinished;
                            }

                            readableBytes -= sent;
                            workingBuffer = workingBuffer.Slice(sent);
                        }
                    }
                    finally
                    {
                        // free the pooled buffer
                        buffer.Dispose();
                    }
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
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(ConnectionTerminatedReason.Error, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                goto WritesFinished;
            }
        }

        WritesFinished:
        _waitForPendingWrites.TrySetResult(true);
    }

    private async Task DoWriteToPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var memory = _pipe.Writer.GetMemory(TcpOptions.MaxFrameSize / 4);
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

                _pipe.Writer.Advance(bytesRead);
            }
            catch (OperationCanceledException)
            {
                // no need to log here
                _closureSelf.Tell(ReadFinished.Instance);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to read from socket.");
                // we are done reading
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(ConnectionTerminatedReason.Error, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                return;
            }

            // make data available to PipeReader
            var result = await _pipe.Writer.FlushAsync(ct);
            if (result.IsCompleted)
            {
                _closureSelf.Tell(ReadFinished.Instance);
                return;
            }
        }

        await _pipe.Writer.CompleteAsync();
    }

    private async Task ReadFromPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _pipe.Reader.ReadAsync(ct);
                var buffer = result.Buffer;

                // consume this entire sequence by copying it into a new buffer
                // have to copy because there's no guarantee we can safely release a shared buffer
                // once we hand the message over to the end-user.
                var newMemory = new Memory<byte>(new byte[buffer.Length]);
                var unshared = new UnsharedMemoryOwner<byte>(newMemory);
                buffer.CopyTo(newMemory.Span);
                _readsFromTransport.Writer.TryWrite((unshared, newMemory.Length));

                // tell the pipe we're done with this data
                _pipe.Reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    _closureSelf.Tell(ReadFinished.Instance);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                _closureSelf.Tell(ReadFinished.Instance);
            }
        }
    }

    private void Running(object message)
    {
        switch (message)
        {
            case DoClose:
            case ReadFinished: // graceful close cases
            {
                _ = CleanUpGracefully(); // idempotent
                break;
            }
            case ConnectionUnexpectedlyClosed closed:
            {
                // we got aborted
                _log.Warning("Connection to [{0}:{1}] was unexpectedly closed: {2}", TcpOptions.Host, TcpOptions.Port,
                    closed.ReasonMessage);
                Context.Stop(Self);
                break;
            }
        }
    }

    private async Task CleanUpGracefully()
    {
        // add a simulated DisconnectPacket to help ensure the stream gets terminated
        _readsFromTransport.Writer.TryWrite(DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1));
        
        // no more writes to transport
        _writesToTransport.Writer.TryComplete();

        // wait for any pending writes to finish
        await _waitForPendingWrites.Task;
        
        // wait for any reads to finish (should be terminated by Akka.Streams once the `DisconnectPacket` is processed.)
        await _readsFromTransport.Reader.Completion;

        _closureSelf.Tell(PoisonPill.Instance);
    }

    private void DisposeSocket(ConnectionStatus newStatus)
    {
        if (_tcpClient is null)
            return; // already disposed

        try
        {
            State.Status = newStatus;


            // stop reading from the socket
            State.ShutDownCts.Cancel();

            _pipe?.Reader.Complete();
            _pipe?.Writer.Complete();
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
    private void FullShutdown(ConnectionTerminatedReason reason = ConnectionTerminatedReason.Normal)
    {
        // mark the channels as complete (should have already been done by the time we get here, but doesn't hurt)
        _writesToTransport.Writer.TryComplete();
        _readsFromTransport.Writer.TryComplete();

        var newStatus = reason switch
        {
            ConnectionTerminatedReason.Error => ConnectionStatus.Failed,
            ConnectionTerminatedReason.Normal => ConnectionStatus.Disconnected,
            ConnectionTerminatedReason.CouldNotConnect => ConnectionStatus.Failed,
            _ => ConnectionStatus.Aborted
        };

        DisposeSocket(newStatus);

        // let upstairs know we're done
        _whenTerminated.TrySetResult(reason);
        _waitForPendingWrites.TrySetResult(true);
    }

    protected override void PostStop()
    {
        FullShutdown();
    }
}