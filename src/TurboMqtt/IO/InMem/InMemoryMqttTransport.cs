// -----------------------------------------------------------------------
// <copyright file="InMemoryMqttTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Akka.Event;
using TurboMqtt.IO.Internal;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO.InMem;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class InMemoryMqttTransportManager : IMqttTransportManager
{
    private readonly int _maxFrameSize;
    private readonly ILoggingAdapter _log;
    private readonly MqttProtocolVersion _protocolVersion;

    public InMemoryMqttTransportManager(int maxFrameSize, ILoggingAdapter log, MqttProtocolVersion protocolVersion)
    {
        _maxFrameSize = maxFrameSize;
        _log = log;
        _protocolVersion = protocolVersion;
    }

    public Task<IMqttTransport> CreateTransportAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IMqttTransport>(new InMemoryMqttTransport(_maxFrameSize, _log, _protocolVersion));
    }
}

/// <summary>
/// Intended for use in testing scenarios where we want to simulate a network connection
/// </summary>
internal sealed class InMemoryMqttTransport : IMqttTransport
{
    private readonly TaskCompletionSource<DisconnectReasonCode> _terminationSource = new();

    private readonly IDuplexPipe _transport;

    private readonly IDuplexPipe _application;

    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly IFakeServerHandle _serverHandle;

    public InMemoryMqttTransport(int maxFrameSize, ILoggingAdapter log, MqttProtocolVersion protocolVersion)
    {
        MaxFrameSize = maxFrameSize;
        Log = log;
        ProtocolVersion = protocolVersion;
        var pipeOptions = new PipeOptions(pauseWriterThreshold: MaxFrameSize,
            resumeWriterThreshold: MaxFrameSize / 2,
            useSynchronizationContext: false);
        var pipes = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);
        _transport = pipes.Transport;
        _application = pipes.Application;
        Channel = _application;

        _serverHandle = protocolVersion switch
        {
            MqttProtocolVersion.V3_1_1 => new FakeMqtt311ServerHandle(PushFn, CloseFn, log),
            _ => throw new NotSupportedException("Only V3.1.1 is supported.")
        };
        return;

        async Task CloseFn()
        {
            await CloseAsync();
        }

        bool PushFn((IMemoryOwner<byte> buffer, int estimatedSize) msg)
        {
            try
            {
                _application.Output.Write(msg.buffer.Memory.Span);
                return true;
            }
            finally
            {
                msg.buffer.Dispose();
            }
        }
    }

    public MqttProtocolVersion ProtocolVersion { get; }

    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.NotStarted;

    public Task<DisconnectReasonCode> WhenTerminated => _terminationSource.Task;

    private readonly TaskCompletionSource<bool> _waitForPendingWrites = new();
    public Task WaitForPendingWrites => _waitForPendingWrites.Task;


    public async Task<bool> CloseAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Disconnected;
        await _application.Output.CompleteAsync();
        await _transport.Input.CompleteAsync();
        await _waitForPendingWrites.Task;
        await _shutdownTokenSource.CancelAsync();
        _terminationSource.TrySetResult(DisconnectReasonCode.NormalDisconnection);
        return true;
    }

    public async Task AbortAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Disconnected;
        await _application.Output.CompleteAsync();
        await _transport.Input.CompleteAsync();
        _terminationSource.TrySetResult(DisconnectReasonCode.UnspecifiedError);
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (Status == ConnectionStatus.NotStarted)
        {
            Status = ConnectionStatus.Connected;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            DoByteWritesAsync(_shutdownTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Simulates the "Server" side of this connection
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task DoByteWritesAsync(CancellationToken ct)
    {
        Log.Debug("Starting to read from transport.");
        while (ct.IsCancellationRequested == false)
        {
            try
            {
                var msg = await _transport.Input.ReadAsync(ct);

                var buffer = msg.Buffer;
                Log.Debug("Received {0} bytes from transport.", buffer.Length);

                if (!buffer.IsEmpty)
                {
                    var seqPosition = buffer.Start;
                    while (buffer.TryGet(ref seqPosition, out var memory))
                    {
                        _serverHandle.HandleBytes(memory);
                    }

                }

                if (msg.IsCompleted)
                {
                    break;
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Error reading from transport.");
                _transport.Input.CancelPendingRead();
                break;
            }
        }

        // should signal that all pending writes have finished
        _waitForPendingWrites.TrySetResult(true);
    }

    public int MaxFrameSize { get; }
    public IDuplexPipe Channel { get; }
}