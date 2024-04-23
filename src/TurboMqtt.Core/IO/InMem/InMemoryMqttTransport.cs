// -----------------------------------------------------------------------
// <copyright file="InMemoryMqttTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Event;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.IO;

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
    private readonly TaskCompletionSource<ConnectionTerminatedReason> _terminationSource = new();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();
    
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly IFakeServerHandle _serverHandle;

    public InMemoryMqttTransport(int maxFrameSize, ILoggingAdapter log, MqttProtocolVersion protocolVersion)
    {
        MaxFrameSize = maxFrameSize;
        Log = log;
        ProtocolVersion = protocolVersion;
        Reader = _readsFromTransport;
        Writer = _writesToTransport;
        
        Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushFn = msg => _readsFromTransport.Writer.TryWrite(msg);
        Func<Task> closeFn = async () =>
        {
            await CloseAsync();
        };
        
        _serverHandle = protocolVersion switch
        {
            MqttProtocolVersion.V3_1_1 => new FakeMqtt311ServerHandle(pushFn, closeFn, log),
            _ => throw new NotSupportedException("Only V3.1.1 is supported.")
        };
    }

    public MqttProtocolVersion ProtocolVersion { get; }

    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.NotStarted;

    public Task<ConnectionTerminatedReason> WhenTerminated => _terminationSource.Task;
    
    private readonly TaskCompletionSource<bool> _waitForPendingWrites = new();
    public Task<bool> WaitForPendingWrites => _waitForPendingWrites.Task;
    


    public async Task CloseAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Disconnected;
        _writesToTransport.Writer.TryComplete();
        await _waitForPendingWrites.Task;
        await _shutdownTokenSource.CancelAsync();
        _readsFromTransport.Writer.TryComplete();
        _terminationSource.TrySetResult(ConnectionTerminatedReason.Normal);
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (Status == ConnectionStatus.NotStarted)
        {
            Status = ConnectionStatus.Connected;
            
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            DoByteWritesAsync(_shutdownTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates the "Server" side of this connection
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task DoByteWritesAsync(CancellationToken ct)
    {
        Log.Debug("Starting to read from transport.");
        while(!_writesToTransport.Reader.Completion.IsCompleted)
        {
            if (!_writesToTransport.Reader.TryRead(out var msg))
            {
                try
                {
                    await _writesToTransport.Reader.WaitToReadAsync(ct);
                }
                catch(OperationCanceledException)
                {
                    
                }
                continue;
            }
            
            try
            {
                ReadOnlyMemory<byte> buffer = msg.buffer.Memory.Slice(0, msg.readableBytes);
                Log.Debug("Received {0} bytes from transport.", buffer.Length);
                _serverHandle.HandleBytes(buffer);
            }
            finally
            {
                // have to free the shared buffer
                msg.buffer.Dispose();
            }
        }

        // should signal that all pending writes have finished
        _waitForPendingWrites.TrySetResult(true);
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
    
}