// -----------------------------------------------------------------------
// <copyright file="TcpTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;

namespace TurboMqtt.Core.IO;

/// <summary>
/// TCP implementation of <see cref="IMqttTransport"/>.
/// </summary>
internal sealed class TcpTransport : IMqttTransport
{
    internal TcpTransport(ILoggingAdapter log, TcpTransportActor.ConnectionState state, IActorRef connectionActor)
    {
        Log = log;
        State = state;
        _connectionActor = connectionActor;
        Reader = state.Reader;
        Writer = state.Writer;
        MaxFrameSize = state.MaxFrameSize;
    }

    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status => State.Status;

    private TcpTransportActor.ConnectionState State { get; }
    private readonly IActorRef _connectionActor;

    public Task<ConnectionTerminatedReason> WhenTerminated => State.WhenTerminated;
    
    public Task<bool> WaitForPendingWrites => State.WaitForPendingWrites;

    public Task CloseAsync(CancellationToken ct = default)
    {
        var watch = _connectionActor.WatchAsync(ct);
        _connectionActor.Tell(new TcpTransportActor.DoClose(ct));
        return watch;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return _connectionActor.Ask<TcpTransportActor.ConnectResult>(new TcpTransportActor.DoConnect(ct), ct);
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}