// -----------------------------------------------------------------------
// <copyright file="TcpTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class TcpMqttTransportManager : IMqttTransportManager
{
    private readonly MqttClientTcpOptions _tcpOptions;
    private readonly MqttProtocolVersion _protocolVersion;
    private readonly IActorRef _mqttClientManager;
    private readonly IStreamProvider? _streamProvider;

    public TcpMqttTransportManager(MqttClientTcpOptions tcpOptions, IActorRef mqttClientManager, MqttProtocolVersion protocolVersion,
        IStreamProvider? streamProvider = null)
    {
        _tcpOptions = tcpOptions;
        _mqttClientManager = mqttClientManager;
        _protocolVersion = protocolVersion;
        _streamProvider = streamProvider;
    }

    public async Task<IMqttTransport> CreateTransportAsync(CancellationToken ct = default)
    {
        var tcpTransportActor =
            await _mqttClientManager.Ask<IActorRef>(new TcpConnectionManager.CreateTcpTransport(_tcpOptions, _protocolVersion, _streamProvider), cancellationToken: ct)
                .ConfigureAwait(false);

        // get the TCP transport
        var tcpTransport = await tcpTransportActor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, cancellationToken: ct)
            .ConfigureAwait(false);

        return tcpTransport;
    }
}

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

    public Task<DisconnectReasonCode> WhenTerminated => State.WhenTerminated;
    
    public Task WaitForPendingWrites => State.WaitForPendingWrites;

    public Task<bool> CloseAsync(CancellationToken ct = default)
    {
        var watch = _connectionActor.WatchAsync(ct);
         // mark the writer as complete
        _connectionActor.Tell(new TcpTransportActor.DoClose(ct));
        return watch;
    }

    public Task AbortAsync(CancellationToken ct = default)
    {
        // just force a shutdown
        var watch = _connectionActor.WatchAsync(ct);
        _connectionActor.Tell(PoisonPill.Instance);
        return watch;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        // Pass CancellationToken.None as the DoConnect payload so DNS resolution and
        // TCP socket connect are governed only by TcpOptions.ConnectTimeout (10 s default),
        // not by the caller's token (which may be a short reconnect-window CTS).
        // The Ask itself still uses ct so this call remains promptly cancellable from
        // the caller's perspective.
        var result = await _connectionActor.Ask<TcpTransportActor.ConnectResult>(
                new TcpTransportActor.DoConnect(CancellationToken.None), ct)
            .ConfigureAwait(false);

        return result.Status == ConnectionStatus.Connected;
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}