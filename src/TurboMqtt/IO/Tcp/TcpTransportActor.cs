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
///
/// FSM:
///   NotStarted (OnReceive) → Created (TransportCreated) → Connecting
///   → Connected → Draining → Closing → Stopped
///                  Connected → Aborted → Stopped
/// </summary>
internal sealed class TcpTransportActor : UntypedActor
{
    #region Internal Types

    /// <summary>
    /// Shared state between the TcpTransport wrapper and the TcpTransportActor.
    /// Status is updated atomically by the actor; all other fields are immutable after construction.
    /// </summary>
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
            internal set => Volatile.Write(ref _status, (int)value);
        }

        public CancellationTokenSource ShutDownCts { get; } = new();

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

    /// <summary>
    /// Self-tell sent when all three background tasks (read-from-stream, read-from-pipe, write-to-stream) have completed.
    /// </summary>
    private sealed class BackgroundTasksCompleted : IDeadLetterSuppression
    {
        private BackgroundTasksCompleted()
        {
        }

        public static BackgroundTasksCompleted Instance { get; } = new();
    }

    /// <summary>
    /// Self-tell sent when outbound data has been flushed during the Draining state.
    /// </summary>
    private sealed class OutboundFlushed : IDeadLetterSuppression
    {
        private OutboundFlushed()
        {
        }

        public static OutboundFlushed Instance { get; } = new();
    }

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

    // Guard against multiple PoisonPill sends
    private bool _poisonPillSent;

    // Guard against CTS double-cancel
    private int _ctsCancelled;

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

    /// <summary>
    /// Logs a structured FSM transition at Info level.
    /// </summary>
    private void LogTransition(string fromState, string toState)
    {
        _log.Info("Transport [{0}:{1}] FSM: {2} -> {3}", TcpOptions.Host, TcpOptions.Port, fromState, toState);
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
                LogTransition("NotStarted", "Created");
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
                State.Status = ConnectionStatus.Connecting;
                LogTransition("Created", "Connecting");
                Become(Connecting);

                // Compose caller's token with configured connect timeout
                var connectTimeout = TcpOptions.ConnectTimeout;
                var timeoutCts = new CancellationTokenSource(connectTimeout);
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(connect.Cancel, timeoutCts.Token);

                RunTask(async () =>
                {
                    var sender = Sender;

                    ConnectResult connectResult;
                    try
                    {
                        _stream = await _streamProvider.ConnectAsync(TcpOptions.Host, TcpOptions.Port, linkedCts.Token)
                            .ConfigureAwait(false);
                        connectResult = new ConnectResult(ConnectionStatus.Connected, "Connected.");
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !connect.Cancel.IsCancellationRequested)
                    {
                        _log.Warning("Connect to [{0}:{1}] timed out after {2}", TcpOptions.Host, TcpOptions.Port, connectTimeout);
                        connectResult = new ConnectResult(ConnectionStatus.Failed,
                            $"Connect timed out after {connectTimeout.TotalSeconds:F0}s");
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to connect to [{0}:{1}]", TcpOptions.Host, TcpOptions.Port);
                        connectResult = new ConnectResult(ConnectionStatus.Failed, ex.Message);
                    }
                    finally
                    {
                        linkedCts.Dispose();
                        timeoutCts.Dispose();
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
            default:
                Unhandled(message);
                break;
        }
    }

    /// <summary>
    /// Connecting state — async connection attempt is in progress.
    /// Valid transitions: ConnectResult(Connected) → Connected, ConnectResult(Failed) → Stopped
    /// </summary>
    private void Connecting(object message)
    {
        switch (message)
        {
            case ConnectResult { Status: ConnectionStatus.Connected }:
            {
                LogTransition("Connecting", "Connected");
                State.Status = ConnectionStatus.Connected;
                BecomeConnected();
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Failed } result:
            {
                _log.Error("Failed to connect to [{0}:{1}]: {2}", TcpOptions.Host, TcpOptions.Port, result.ReasonMessage);
                LogTransition("Connecting", "Failed");
                State.Status = ConnectionStatus.Failed;
                Context.Stop(Self);
                break;
            }
            case DoConnect:
            {
                _log.Debug("Ignoring DoConnect in Connecting state — connection attempt already in progress.");
                Sender.Tell(new ConnectResult(ConnectionStatus.Connecting,
                    $"Already attempting to connect to [{TcpOptions.Host}:{TcpOptions.Port}]"));
                break;
            }
            case DoClose:
            {
                _log.Debug("Received DoClose while Connecting — will abort connection attempt.");
                LogTransition("Connecting", "Failed");
                State.Status = ConnectionStatus.Failed;
                Context.Stop(Self);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    /// <summary>
    /// Transitions to the Connected state and starts the three background tasks,
    /// tracking them with Task.WhenAll + ContinueWith self-tell.
    /// </summary>
    private void BecomeConnected()
    {
        Become(Connected);

        var readFromStreamTask = DoWriteToPipeAsync(State.ShutDownCts.Token);
        var readFromPipeTask = ReadFromPipeAsync(State.ShutDownCts.Token);
        var writeToStreamTask = DoWriteToSocketAsync(State.ShutDownCts.Token);

        Task.WhenAll(readFromStreamTask, readFromPipeTask, writeToStreamTask).ContinueWith(_ =>
        {
            _closureSelf.Tell(BackgroundTasksCompleted.Instance);
        }, TaskContinuationOptions.ExecuteSynchronously);
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
                return;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to write to socket.");
                // we are done writing
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(DisconnectReasonCode.UnspecifiedError, ex.Message));
                // abort ongoing reads and writes, but don't shutdown the transport
                TryCancelCts();
                return;
            }
        }

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
                    return;
                }

                _pipe.Writer.Advance(bytesRead);
            }
            catch (OperationCanceledException)
            {
                // no need to log here
                return;
            }
            catch (Exception ex)
            {
                // this is a debug-level issue
                _log.Debug(ex, "Failed to read from socket.");
                // we are done reading
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(DisconnectReasonCode.UnspecifiedError, ex.Message));
                return;
            }

            // make data available to PipeReader
            var result = await _pipe.Writer.FlushAsync(ct);
            if (result.IsCompleted)
            {
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

    /// <summary>
    /// Connected state — the transport is active and reading/writing data.
    /// Valid transitions: DoClose → Draining, ReadFinished → Closing, ConnectionUnexpectedlyClosed → Aborted
    /// </summary>
    private void Connected(object message)
    {
        switch (message)
        {
            case DoClose:
            {
                BecomeDraining();
                break;
            }
            case ReadFinished:
            {
                BecomeClosing();
                break;
            }
            case ConnectionUnexpectedlyClosed closed:
            {
                _log.Warning("Connection to [{0}:{1}] was unexpectedly closed: {2}", TcpOptions.Host, TcpOptions.Port,
                    closed.ReasonMessage);
                BecomeAborted();
                break;
            }
            case BackgroundTasksCompleted:
            {
                // Background tasks finished while still in Connected — go straight to shutdown
                _log.Debug("Background tasks completed while in Connected state.");
                LogTransition("Connected", "Stopped");
                SendPoisonPillOnce();
                break;
            }
            default:
                // Ignore unknown messages in Connected state
                break;
        }
    }

    /// <summary>
    /// Draining state — outbound channel writer is completed, waiting for pending outbound data to flush.
    /// </summary>
    private void BecomeDraining()
    {
        LogTransition("Connected", "Draining");
        State.Status = ConnectionStatus.Draining;
        Become(Draining);

        // no more writes to transport — completes the channel so the socket writer drains remaining data
        _writesToTransport.Writer.TryComplete();

        // wait for pending writes to flush, then inject DISCONNECT and transition
        State.WaitForPendingWrites.ContinueWith(_ =>
        {
            _closureSelf.Tell(OutboundFlushed.Instance);
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void Draining(object message)
    {
        switch (message)
        {
            case OutboundFlushed:
            {
                // Outbound has flushed — BecomeClosing() injects the DISCONNECT signal for Akka.Streams
                BecomeClosing();
                break;
            }
            case BackgroundTasksCompleted:
            {
                _log.Debug("Background tasks completed while in Draining state.");
                LogTransition("Draining", "Stopped");
                SendPoisonPillOnce();
                break;
            }
            // Ignore duplicate messages that may arrive while draining
            case DoClose:
            case ReadFinished:
            case ConnectionUnexpectedlyClosed:
                _log.Debug("Ignoring {0} in Draining state.", message.GetType().Name);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Closing state — CTS cancelled, waiting for BackgroundTasksCompleted.
    /// </summary>
    private void BecomeClosing()
    {
        LogTransition(State.Status == ConnectionStatus.Draining ? "Draining" : "Connected", "Closing");
        State.Status = ConnectionStatus.Closing;
        Become(Closing);

        // Inject a disconnect packet so Akka.Streams can detect the shutdown
        _readsFromTransport.Writer.TryWrite(DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1));

        // Cancel the background tasks
        TryCancelCts();

        // Complete channels
        _writesToTransport.Writer.TryComplete();
        _readsFromTransport.Writer.TryComplete();
    }

    private void Closing(object message)
    {
        switch (message)
        {
            case BackgroundTasksCompleted:
            {
                LogTransition("Closing", "Stopped");
                SendPoisonPillOnce();
                break;
            }
            // Ignore duplicate messages that may arrive while closing
            case DoClose:
            case ReadFinished:
            case ConnectionUnexpectedlyClosed:
            case OutboundFlushed:
                _log.Debug("Ignoring {0} in Closing state.", message.GetType().Name);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Aborted state — immediate cancel, no drain wait.
    /// </summary>
    private void BecomeAborted()
    {
        LogTransition("Connected", "Aborted");
        State.Status = ConnectionStatus.Aborted;
        Become(Aborted);

        // Inject a disconnect packet so Akka.Streams can detect the shutdown
        _readsFromTransport.Writer.TryWrite(DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1));

        // Immediate cancel
        TryCancelCts();

        // Complete channels
        _writesToTransport.Writer.TryComplete();
        _readsFromTransport.Writer.TryComplete();
    }

    private void Aborted(object message)
    {
        switch (message)
        {
            case BackgroundTasksCompleted:
            {
                LogTransition("Aborted", "Stopped");
                SendPoisonPillOnce();
                break;
            }
            // Ignore everything else in Aborted state
            case DoClose:
            case ReadFinished:
            case ConnectionUnexpectedlyClosed:
            case OutboundFlushed:
                _log.Debug("Ignoring {0} in Aborted state.", message.GetType().Name);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Thread-safe CTS cancellation — ensures we only cancel once.
    /// </summary>
    private void TryCancelCts()
    {
        if (Interlocked.CompareExchange(ref _ctsCancelled, 1, 0) == 0)
        {
            try
            {
                State.ShutDownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }
    }

    /// <summary>
    /// Sends PoisonPill exactly once.
    /// </summary>
    private void SendPoisonPillOnce()
    {
        if (!_poisonPillSent)
        {
            _poisonPillSent = true;
            _closureSelf.Tell(PoisonPill.Instance);
        }
    }

    private void DisposeStreamProvider(ConnectionStatus newStatus)
    {
        _log.Info("Disposing of TCP transport stream.");

        try
        {
            State.Status = newStatus;

            // stop reading from the socket (safe — TryCancelCts guards double-cancel)
            TryCancelCts();

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
