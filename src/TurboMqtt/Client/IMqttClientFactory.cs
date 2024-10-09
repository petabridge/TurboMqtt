// -----------------------------------------------------------------------
// <copyright file="IMqttClientFactory.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt.IO;
using TurboMqtt.Streams;
using TurboMqtt.IO.InMem;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Client;

public interface IMqttClientFactory
{
    /// <summary>
    /// Creates a TCP-based MQTT client.
    /// </summary>
    /// <param name="options">Options for our <see cref="ConnectPacket"/> to the broker.</param>
    /// <param name="tcpOptions">Options for controlling our TCP socket.</param>
    /// <returns></returns>
    Task<IMqttClient> CreateTcpClient(MqttClientConnectOptions options, MqttClientTcpOptions tcpOptions);
}

/// <summary>
/// Used for testing purposes
/// </summary>
internal interface IInternalMqttClientFactory
{
    Task<IMqttClient> CreateInMemoryClient(MqttClientConnectOptions options);
}

/// <summary>
/// Used to create instances of <see cref="IMqttClient"/> for use in end-user applications.
/// </summary>
/// <remarks>
/// Requires an <see cref="ActorSystem"/> to function properly. 
/// </remarks>
public sealed class MqttClientFactory : IMqttClientFactory, IInternalMqttClientFactory
{
    private readonly ActorSystem _system;
    private readonly IActorRef _mqttClientManager;

    public MqttClientFactory(ActorSystem system)
    {
        _system = system;
        _mqttClientManager = _system.ActorOf(Props.Create<ClientManagerActor>(), "turbomqtt-clients");
    }

    public async Task<IMqttClient> CreateTcpClient(MqttClientConnectOptions options, MqttClientTcpOptions tcpOptions)
    {
        AssertMqtt311(options);
        if (tcpOptions.TlsOptions is { UseTls: true, SslOptions: null })
            throw new NullReferenceException("TlsOptions.SslOptions can not be null if TlsOptions.UseTls is true");
        
        var transportManager = new TcpMqttTransportManager(tcpOptions, _mqttClientManager, options.ProtocolVersion);

        // create the client
        var clientActor =
            await _mqttClientManager.Ask<IActorRef>(new ClientManagerActor.StartClientActor(options.ClientId))
                .ConfigureAwait(false);

        var client = await clientActor.Ask<IMqttClient>(new ClientStreamOwner.CreateClient(transportManager, options))
            .ConfigureAwait(false);
        
        return client;
    }

    public async Task<IMqttClient> CreateInMemoryClient(MqttClientConnectOptions options)
    {
        AssertMqtt311(options);
        var transportManager = new InMemoryMqttTransportManager((int)options.MaximumPacketSize * 2,
            _system.CreateLogger<InMemoryMqttTransportManager>(options.ClientId), options.ProtocolVersion);
        
        var clientActor =
            await _mqttClientManager.Ask<IActorRef>(new ClientManagerActor.StartClientActor(options.ClientId));
        return await clientActor.Ask<IMqttClient>(new ClientStreamOwner.CreateClient(
           transportManager,
            options));
    }

    private static void AssertMqtt311(MqttClientConnectOptions options)
    {
        if (options.ProtocolVersion != MqttProtocolVersion.V3_1_1)
        {
            throw new NotSupportedException("Only MQTT 3.1.1 is supported.");
        }
    }
}