// -----------------------------------------------------------------------
// <copyright file="IMqttClientFactory.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
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

    public Task<IMqttClient> CreateTcpClient(MqttClientConnectOptions options, MqttClientTcpOptions tcpOptions)
    {
        AssertMqtt311(options);
        throw new NotImplementedException();
    }

    public async Task<IMqttClient> CreateInMemoryClient(MqttClientConnectOptions options)
    {
        AssertMqtt311(options);
        var clientActor =
            await _mqttClientManager.Ask<IActorRef>(new ClientManagerActor.StartClientActor(options.ClientId));
        return await clientActor.Ask<IMqttClient>(new ClientStreamOwner.CreateClient(
            new InMemoryMqttTransport((int)options.MaximumPacketSize * 2, _system.CreateLogger<InMemoryMqttTransport>(options.ClientId), MqttProtocolVersion.V3_1_1),
            options));
    }

    private void AssertMqtt311(MqttClientConnectOptions options)
    {
        if (options.ProtocolVersion != MqttProtocolVersion.V3_1_1)
        {
            throw new NotSupportedException("Only MQTT 3.1.1 is supported.");
        }
    }
}

/// <summary>
/// Aggregate root actor for managing all TurboMqtt clients.
/// </summary>
internal sealed class ClientManagerActor : UntypedActor
{
    public sealed class StartClientActor(string endpointDescriptor)
    {
        public string EndpointDescriptor { get; } = endpointDescriptor;
    }

    /// <summary>
    /// Used to help generate unique actor names for each client.
    /// </summary>
    private int _clientCounter = 0;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            // TODO: probably need to enforce duplicates of client ids
            case StartClientActor start:
                var actorName = Uri.EscapeDataString($"mqttclient-{start.EndpointDescriptor}-{_clientCounter++}");
                var client = Context.ActorOf(Props.Create(() => new ClientStreamOwner()), actorName);
                Sender.Tell(client);
                break;
        }
    }
}