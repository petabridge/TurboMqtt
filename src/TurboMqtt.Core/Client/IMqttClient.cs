// -----------------------------------------------------------------------
// <copyright file="IMqttClient.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Streams;
using TurboMqtt.Core.Config;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Protocol.Pub;

namespace TurboMqtt.Core.Client;

/// <summary>
/// A TurboMQTT client that can be used to send and receive MQTT messages.
/// </summary>
public interface IMqttClient
{
    /// <summary>
    /// The version of the MQTT protocol that this client is using.
    /// </summary>
    public MqttProtocolVersion ProtocolVersion { get; }
    
    /// <summary>
    /// The state of the connection to the MQTT broker.
    /// </summary>
    public bool IsConnected { get; }
    
    /// <summary>
    /// Connects the client to the MQTT broker.
    /// </summary>
    /// <param name="options">The options used to connect to the broker.</param>
    /// <param name="cancellationToken">The token used to cancel the connection.</param>
    /// <returns></returns>
    Task<IAckResponse> ConnectAsync(MqttClientConnectOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the client from the MQTT broker.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the disconnection.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message to the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to publish the message to.</param>
    /// <param name="message">The message to publish.</param>
    /// <param name="qos">The quality of service level to use when publishing the message.</param>
    /// <param name="retain">Whether or not to retain the message on the broker.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns></returns>
    Task<IPublishControlMessage> PublishAsync(string topic, ReadOnlyMemory<byte> message, QualityOfService qos, bool retain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a topic on the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="qos">The quality of service level to use when subscribing to the topic.</param>
    /// <param name="cancellationToken">The token used to cancel the subscription.</param>
    /// <returns></returns>
    Task<IAckResponse> SubscribeAsync(string topic, QualityOfService qos, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Receives a stream of messages from the MQTT broker.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to terminate the stream.</param>
    /// <returns></returns>
    IAsyncEnumerable<MqttMessage> ReceiveMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a topic on the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to unsubscribe from.</param>
    /// <param name="cancellationToken">The token used to cancel the unsubscription.</param>
    /// <returns></returns>
    Task<IAckResponse> UnsubscribeAsync(string topic, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// A task we can use to wait for the connection to terminate.
    /// </summary>
    /// <remarks>
    /// Does not cause the connection to terminate - just waits for it to finish.
    /// </remarks>
    public Task<ConnectionTerminatedReason> WaitForTermination();
}

/// <summary>
/// Default MQTT client implementation
/// </summary>
// public sealed class MqttClient : IMqttClient
// {
//     private readonly IMqttTransport _transport;
//     private readonly IActorRefFactory _actorRefFactory;
//     private readonly IMaterializer _materializer;
//
//     internal MqttClient(IMqttTransport transport, IActorRefFactory actorRefFactory)
//     {
//         _transport = transport;
//         _actorRefFactory = actorRefFactory;
//         _materializer = _actorRefFactory.Materializer();
//     }
//
//     public bool IsConnected => _transport.Status == ConnectionStatus.Connected;
//     public async Task<IAckResponse> ConnectAsync(MqttClientConnectOptions options, CancellationToken cancellationToken = default)
//     {
//         if (_transport.Status != ConnectionStatus.NotStarted)
//             return new AckProtocol.ConnectFailure($"Already in state [{_transport.Status}]");
//         
//         // this will blow up if there's a problem with the connection
//         await _transport.ConnectAsync(cancellationToken);
//     }
//
//     public async Task DisconnectAsync(CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<IPublishControlMessage> PublishAsync(string topic, ReadOnlyMemory<byte> message, QualityOfService qos, bool retain,
//         CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<IAckResponse> SubscribeAsync(string topic, QualityOfService qos, CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public IAsyncEnumerable<MqttMessage> ReceiveMessagesAsync(CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<IAckResponse> UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<ConnectionTerminatedReason> WaitForTermination()
//     {
//         throw new NotImplementedException();
//     }
// }