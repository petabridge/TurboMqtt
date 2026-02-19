// -----------------------------------------------------------------------
// <copyright file="TcpTransportActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

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
        public ConnectionState(ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> writer,
            ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> reader,
            Task<DisconnectReasonCode> whenTerminated, int maxFrameSize, Task waitForPendingWrites)
        {
            Writer = writer;
            Reader = reader;
            WhenTerminated = whenTerminated;
            MaxFrameSize = maxFrameSize;
            WaitForPendingWrites = waitForPendingWrites;
        }

        private int _status = (int)ConnectionStatus.NotStarted;

        public ConnectionStatus Status
        {
            get => (ConnectionStatus)Volatile.Read(ref _status);
            set => Volatile.Write(ref _status, (int)value);
        }

        /// <summary>
        /// Atomically update the status if current value matches expected.
        /// </summary>
        /// <returns>True if the update was successful, false otherwise.</returns>
        public bool CompareAndSetStatus(ConnectionStatus expected, ConnectionStatus newValue)
        {
            return Interlocked.CompareExchange(ref _status, (int)newValue, (int)expected) == (int)expected;
        }

        public CancellationTokenSource ShutDownCts { get; set; } = new();

        public int MaxFrameSize { get; }

        public Task<DisconnectReasonCode> WhenTerminated { get; }

        public Task WaitForPendingWrites { get; }

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
    public sealed record ConnectionUnexpectedlyClosed(DisconnectReasonCode Reason, string ReasonMessage);

    #endregion

    public MqttClientTcpOptions TcpOptions { get; }
    public int MaxFrameSize { get; }

    public ConnectionState State { get; private set; }

    private readonly IStreamProvider _streamProvider;
    private Stream? _stream;

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly TaskCompletionSource<DisconnectReasonCode> _whenTerminated = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly Pipe _pipe;

    // Track task completion for graceful shutdown
    private readonly TaskCompletionSource _shutdownComplete = new();

    public TcpTransportActor(MqttClientTcpOptions tcpOptions, IStreamProvider streamProvider)
    {
        TcpOptions = tcpOptions;
        MaxFrameSize = tcpOptions.MaxFrameSize;
        _streamProvider = streamProvider;

        State = new ConnectionState(_writesToTransport.Writer, _readsFromTransport.Reader, _whenTerminated.Task,
            MaxFrameSize, _writesToTransport.Reader.Completion); // we signal completion when _writesToTransport is done

        _pipe = new Pipe(new PipeOptions(pauseWriterThreshold: ScaleBufferSize(MaxFrameSize), resumeWriterThreshold: ScaleBufferSize(MaxFrameSize) / 2,
            useSynchronizationContext: false));
    }

    /*
     * FSM:
     * OnReceive (nothing has happened) --> CreateTcpTransport --> TransportCreated BECOME Connecting
     * Connecting --> DoConnect --> Connecting (already connecting) --> ConnectResult (Connected) BECOME Running
     * Running --> DoWriteToPipeAsync --> Running (read data from stream) --> DoWriteToSocketAsync --> Running (write data to stream)
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

                    ConnectResult connectResult;
                    try
                    {
                        _stream = await _streamProvider.ConnectAsync(TcpOptions.Host, TcpOptions.Port, connect.Cancel)
                            .ConfigureAwait(false);
                        connectResult = new ConnectResult(ConnectionStatus.Connected, "Connected.");
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to connect to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);
                        connectResult = new ConnectResult(ConnectionStatus.Failed, ex.Message);
                    }

                    sender.Tell(connectResult);
                    _closureSelf.Tell(connectResult);
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
                        while (readableBytes > 0 && _stream is not null)
                        {
                            var slice = workingBuffer.Slice(0, readableBytes);
                            await _stream!.WriteAsync(slice, ct).ConfigureAwait(false);
                            readableBytes = 0; // Stream.WriteAsync writes all bytes
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
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(DisconnectReasonCode.UnspecifiedError, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                goto WritesFinished;
            }
        }

        WritesFinished:
            _writesToTransport.Writer.TryComplete(); // can't write anymore either
    }

    private async Task DoWriteToPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var memory = _pipe.Writer.GetMemory(TcpOptions.MaxFrameSize / 4);
            try
            {
                int bytesRead = await _stream!.ReadAsync(memory, ct).ConfigureAwait(false);
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
                return;
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

                // consume this entire sequence by copying it into a pooled buffer
                var length = (int)buffer.Length;
                var pooled = MemoryPool<byte>.Shared.Rent(length);
                buffer.CopyTo(pooled.Memory.Span);
                _readsFromTransport.Writer.TryWrite((pooled, length));

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
                return;
            }
        }
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
        _readsFromTransport.Writer.TryWrite(DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1));

        State.Status = ConnectionStatus.Disconnected;

        // no more writes to transport
        _writesToTransport.Writer.TryComplete();

        // wait for any pending writes to finish
        await State.WaitForPendingWrites;

        if (waitOnReads)
        {
            // wait for any reads to finish (should be terminated by Akka.Streams once the `DisconnectPacket` is processed.)
            await _readsFromTransport.Reader.Completion;
        }
        else // if we're not waiting on reads, just complete the reader
        {
            _readsFromTransport.Writer.TryComplete();
        }

        // Cancel the background tasks gracefully
        try
        {
            await State.ShutDownCts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already cancelled, ignore
        }

        // Brief delay to allow ongoing operations to complete
        await Task.Delay(100);

        _closureSelf.Tell(PoisonPill.Instance);
    }

    private void DisposeStreamProvider(ConnectionStatus newStatus)
    {
        _log.Info("Disposing of TCP transport stream.");

        try
        {
            State.Status = newStatus;

            // stop reading from the socket
            State.ShutDownCts.Cancel();

            _pipe.Reader.Complete();
            _pipe.Writer.Complete();

            _stream?.Close();
            _stream?.Dispose();
            _streamProvider.Close();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to cleanly dispose of TCP client and stream.");
        }
        finally
        {
            _stream = null;
        }
    }

    /// <summary>
    /// This is for when we run out of retries or we were explicitly told to close the connection.
    /// </summary>
    private void FullShutdown(DisconnectReasonCode reason = DisconnectReasonCode.NormalDisconnection)
    {
        // mark the channels as complete (should have already been done by the time we get here, but doesn't hurt)
        _writesToTransport.Writer.TryComplete();
        _readsFromTransport.Writer.TryComplete();

        var newStatus = reason switch
        {
            DisconnectReasonCode.ServerShuttingDown => ConnectionStatus.Disconnected,
            DisconnectReasonCode.NormalDisconnection => ConnectionStatus.Disconnected,
            DisconnectReasonCode.UnspecifiedError => ConnectionStatus.Failed,
            _ => ConnectionStatus.Aborted
        };

        DisposeStreamProvider(newStatus);

        // let upstairs know we're done
        _whenTerminated.TrySetResult(reason);
    }

    protected override void PostStop()
    {
        FullShutdown();
    }
}
