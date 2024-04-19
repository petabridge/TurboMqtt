// -----------------------------------------------------------------------
// <copyright file="IMqttClientFactory.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Client;

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
        var tcpTransportActor =
            await _mqttClientManager.Ask<IActorRef>(new TcpConnectionManager.CreateTcpTransport(tcpOptions, options.ProtocolVersion))
                .ConfigureAwait(false);

        // get the TCP transport
        var tcpTransport = await tcpTransportActor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance)
            .ConfigureAwait(false);

        // create the client
        var clientActor =
            await _mqttClientManager.Ask<IActorRef>(new ClientManagerActor.StartClientActor(options.ClientId))
                .ConfigureAwait(false);

        var client = await clientActor.Ask<IMqttClient>(new ClientStreamOwner.CreateClient(tcpTransport, options))
            .ConfigureAwait(false);

        async Task SignDeathPact()
        {
            // arrange death pact between client and transport
            var deathWatches = Task.WhenAny(tcpTransportActor.WatchAsync(), clientActor.WatchAsync());

            await deathWatches;

            // kill the other actor if one dies (don't need to check)
            tcpTransportActor.Tell(PoisonPill.Instance);
            clientActor.Tell(PoisonPill.Instance);

            // dispose of the client
            await client.DisposeAsync();
        }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        SignDeathPact(); // fire and forget
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        return client;
    }

    public async Task<IMqttClient> CreateInMemoryClient(MqttClientConnectOptions options)
    {
        AssertMqtt311(options);
        var clientActor =
            await _mqttClientManager.Ask<IActorRef>(new ClientManagerActor.StartClientActor(options.ClientId));
        return await clientActor.Ask<IMqttClient>(new ClientStreamOwner.CreateClient(
            new InMemoryMqttTransport((int)options.MaximumPacketSize * 2,
                _system.CreateLogger<InMemoryMqttTransport>(options.ClientId), MqttProtocolVersion.V3_1_1),
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