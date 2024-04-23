// -----------------------------------------------------------------------
// <copyright file="IMqttClient.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Protocol.Pub;
using TurboMqtt.Core.Streams;
using TurboMqtt.Core.Utility;

namespace TurboMqtt.Core.Client;

/// <summary>
/// A TurboMQTT client that can be used to send and receive MQTT messages.
/// </summary>
public interface IMqttClient : IAsyncDisposable
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
    /// <param name="cancellationToken">The token used to cancel the connection.</param>
    /// <returns></returns>
    Task<IAckResponse> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcefully aborts the connection to the MQTT broker.
    /// </summary>
    Task AbortConnectionAsync();

    /// <summary>
    /// Disconnects the client from the MQTT broker.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the disconnection.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message to the MQTT broker.
    /// </summary>
    /// <param name="message">The message to be published.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns></returns>
    Task<IPublishControlMessage> PublishAsync(MqttMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message to the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to publish the message to.</param>
    /// <param name="message">The message to publish.</param>
    /// <param name="qos">The quality of service level to use when publishing the message.</param>
    /// <param name="retain">Whether or not to retain the message on the broker.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns></returns>
    Task<IPublishControlMessage> PublishAsync(string topic, ReadOnlyMemory<byte> message, QualityOfService qos,
        bool retain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a topic on the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="qos">The quality of service level to use when subscribing to the topic.</param>
    /// <param name="cancellationToken">The token used to cancel the subscription push operation to the server.</param>
    /// <remarks>
    /// Cancelling this operation DOES NOT GUARANTEE that the server will stop sending messages to the client on this topic.
    /// It only guarantees that we will stop awaiting on the server's response to our SUBSCRIBE packet.
    ///
    /// Use the <see cref="UnsubscribeAsync(string,System.Threading.CancellationToken)"/> method to stop receiving messages on a topic.
    /// </remarks>
    Task<IAckResponse> SubscribeAsync(string topic, QualityOfService qos,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to multiple topics on the MQTT broker.
    /// </summary>
    /// <param name="topics">The topics to subscribe to.</param>
    /// <param name="cancellationToken">The token used to cancel the subscription push operation to the server.</param>
    /// <remarks>
    /// Cancelling this operation DOES NOT GUARANTEE that the server will stop sending messages to the client on this topic.
    /// It only guarantees that we will stop awaiting on the server's response to our SUBSCRIBE packet.
    ///
    /// Use the <see cref="UnsubscribeAsync(string,System.Threading.CancellationToken)"/> method to stop receiving messages on a topic.
    /// </remarks>
    Task<IAckResponse> SubscribeAsync(TopicSubscription[] topics, CancellationToken cancellationToken = default);

    /// <summary>
    /// A channel reader that can be used to read messages received from the MQTT broker.
    /// </summary>
    ChannelReader<MqttMessage> ReceivedMessages { get; }

    /// <summary>
    /// Unsubscribes from a topic on the MQTT broker.
    /// </summary>
    /// <param name="topic">The topic to unsubscribe from.</param>
    /// <param name="cancellationToken">The token used to cancel the unsubscription.</param>
    Task<IAckResponse> UnsubscribeAsync(string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from multiple topics on the MQTT broker.
    /// </summary>
    /// <param name="topics">The range of topics we want to unsubscribe from</param>
    /// <param name="cancellationToken">The token used to cancel the unsubscription.</param>
    Task<IAckResponse> UnsubscribeAsync(string[] topics, CancellationToken cancellationToken = default);

    /// <summary>
    /// A task we can use to wait for the connection to terminate.
    /// </summary>
    /// <remarks>
    /// Does not cause the connection to terminate - just waits for it to finish.
    /// </remarks>
    public Task<DisconnectReasonCode> WhenTerminated { get; }
}

/// <summary>
/// INTERNAL API
/// </summary>
internal interface IInternalMqttClient : IMqttClient
{
    /// <summary>
    /// Needed to power the ability to swap out the transport during reconnect scenarios.
    /// </summary>
    void SwapTransport(IMqttTransport newTransport);
}

/// <summary>
/// Default MQTT client implementation
/// </summary>
public sealed class MqttClient : IInternalMqttClient
{
    private readonly MqttClientConnectOptions _options;
    private IMqttTransport _transport;
    private readonly IActorRef _clientOwner;
    private readonly MqttRequiredActors _requiredActors;
    private readonly ChannelWriter<MqttPacket> _packetWriter;
    private readonly ILoggingAdapter _log;
    private readonly UShortCounter _packetIdCounter = new();

    internal MqttClient(IMqttTransport transport, IActorRef clientOwner, MqttRequiredActors requiredActors,
        ChannelReader<MqttMessage> messageReader, ChannelWriter<MqttPacket> packetWriter, ILoggingAdapter log,
        MqttClientConnectOptions options, Task<DisconnectReasonCode> trueDeath)
    {
        _transport = transport;
        _clientOwner = clientOwner;
        _requiredActors = requiredActors;
        ReceivedMessages = messageReader;
        _packetWriter = packetWriter;
        _log = log;
        _options = options;
        WhenTerminated = trueDeath;
    }

    /// <summary>
    /// Used to swap out the transport for a new one during reconnect scenarios.
    /// </summary>
    /// <param name="newTransport">The replacement transport</param>
    void IInternalMqttClient.SwapTransport(IMqttTransport newTransport)
    {
        _transport = newTransport;
    }

    public MqttProtocolVersion ProtocolVersion => _options.ProtocolVersion;
    public bool IsConnected => _transport.Status == ConnectionStatus.Connected;

    public async Task AbortConnectionAsync()
    {
        // should trigger a graceful stop of the client and the transport
        await _clientOwner.GracefulStop(TimeSpan.FromSeconds(3));
        await WhenTerminated;
    }

    public async Task<IAckResponse> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_transport.Status == ConnectionStatus.Connected)
            return new AckProtocol.ConnectSuccess("Already connected to broker.");
        if(!(_transport.Status == ConnectionStatus.Connecting ||
           _transport.Status == ConnectionStatus.NotStarted))
                return new AckProtocol.ConnectFailure($"Already in state [{_transport.Status}]");

        // this will blow up if there's a problem with the connection
        await _transport.ConnectAsync(cancellationToken);

        var connectFlags = new ConnectFlags
        {
            CleanSession = _options.CleanSession,
            UsernameFlag = !string.IsNullOrEmpty(_options.UserName),
            PasswordFlag = !string.IsNullOrEmpty(_options.Password),
            WillFlag = _options.LastWill != null,
            WillQoS = _options.LastWill?.QosLevel ?? QualityOfService.AtMostOnce,
            WillRetain = _options.LastWill?.Retain ?? false
        };


        // now we need to send the CONNECT packet
        var connectPacket = new ConnectPacket(_options.ProtocolVersion)
        {
            ClientId = _options.ClientId,
            KeepAliveSeconds = _options.KeepAliveSeconds,
            Username = _options.UserName,
            Password = _options.Password,
            ConnectFlags = connectFlags,
            MaximumPacketSize = _options.MaximumPacketSize,
            ReceiveMaximum = _options.ReceiveMaximum,
        };

        if (_options.LastWill != null)
        {
            var lastWill = _options.LastWill;

            var will = new MqttLastWill(lastWill.Topic, lastWill.Message);

            // MQTT 5.0 properties we don't support yet
            // will.ContentType = lastWill.ContentType;
            // will.DelayInterval = lastWill.DelayInterval;
            // will.MessageExpiryInterval = lastWill.MessageExpiryInterval;
            // will.PayloadFormatIndicator = lastWill.PayloadFormatIndicator;
            // will.ResponseTopic = lastWill.ResponseTopic;
            // will.WillCorrelationData = lastWill.WillCorrelationData;
            // will.WillProperties = lastWill.WillProperties;
            connectPacket.Will = will;
        }

        // send the CONNECT packet for completion tracking
        var askTask = _requiredActors.ClientAck.Ask<IAckResponse>(connectPacket, cancellationToken);

        // flush the packet to the wire
        var wrote = _packetWriter.TryWrite(connectPacket);
        if (!wrote)
        {
            _log.Error("Failed to write CONNECT packet to wire.");
            return new AckProtocol.ConnectFailure("Failed to write CONNECT packet to wire.");
        }

        // wait for the response
        try
        {
            var resp = await askTask;
            if (!resp.IsSuccess)
            {
                _log.Error("Failed to connect to MQTT broker - Reason: {0}", resp.Reason);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                AbortConnectionAsync();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                return resp;
            }

            return resp;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to MQTT broker - Reason: {0}", ex.Message);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            AbortConnectionAsync();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            return new AckProtocol.ConnectFailure(ex.Message);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_transport.Status != ConnectionStatus.Connected)
            return;

        var disconnectPacket = new DisconnectPacket();
        await _packetWriter.WriteAsync(disconnectPacket, cancellationToken);

        // begin the shutdown of the transport - internally, the transport should guarantee
        // that all pending writes are flushed before the connection is closed
#pragma warning disable CA2016
        await _clientOwner.Ask<ClientStreamOwner.DisconnectComplete>(
            new ClientStreamOwner.DoDisconnect(cancellationToken));
#pragma warning restore CA2016

        // the broker SHOULD disconnect from us
        try
        {
            await WhenTerminated.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch
        {
            // force the connection to close after grace period has elapsed
            await AbortConnectionAsync();
        }
    }

    private static readonly Task<IPublishControlMessage> Qos0Task =
        Task.FromResult((IPublishControlMessage)PublishingProtocol.PublishSuccess.Instance);

    private sealed class CancelHandle(IActorRef actor, object message)
    {
        public IActorRef Actor { get; } = actor;
        public object Message { get; } = message;

        public void DoCancel()
        {
            Actor.Tell(Message);
        }
    }

    public async Task<IPublishControlMessage> PublishAsync(MqttMessage message,
        CancellationToken cancellationToken = default)
    {
        // if (_transport.Status != ConnectionStatus.Connected)
        //     return new PublishingProtocol.PublishFailure("Not connected to broker.");

        var publishPacket = message.ToPacket();

        Task<IPublishControlMessage> WaitForAck(IActorRef targetActor, PublishPacket packet)
        {
            var task = targetActor.Ask<IPublishControlMessage>(packet, cancellationToken);

            var cancel = new CancelHandle(targetActor, new PublishingProtocol.PublishCancelled(packet.PacketId));

            cancellationToken.Register((obj) =>
            {
                if (obj is CancelHandle c)
                {
                    c.DoCancel();
                }
            }, cancel);

            return task;
        }

        var ackTask = Qos0Task;
        switch (publishPacket.QualityOfService)
        {
            case QualityOfService.AtLeastOnce:
            {
                publishPacket.PacketId = _packetIdCounter.GetNextValue();
                ackTask = WaitForAck(_requiredActors.Qos1Actor, publishPacket);
                break;
            }
            case QualityOfService.ExactlyOnce:
            {
                publishPacket.PacketId = _packetIdCounter.GetNextValue();
                ackTask = WaitForAck(_requiredActors.Qos2Actor, publishPacket);
                break;
            }
        }

        // flush the packet to the wire
        var didWrite = _packetWriter.TryWrite(publishPacket);
        if (!didWrite)
        {
            _log.Error("Failed to write PUBLISH packet to wire.");
            return new PublishingProtocol.PublishFailure("Failed to write PUBLISH packet to wire.");
        }

        // wait for the response
        try
        {
            var resp = await ackTask;
            if (!resp.IsSuccess)
            {
                _log.Error("Failed to publish message to MQTT broker - Reason: {0}", resp.Reason);
                return resp;
            }

            return resp;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to publish message to MQTT broker - Reason: {0}", ex.Message);
            return new PublishingProtocol.PublishFailure(ex.Message);
        }
    }

    public Task<IPublishControlMessage> PublishAsync(string topic, ReadOnlyMemory<byte> message,
        QualityOfService qos = QualityOfService.AtMostOnce, bool retain = false,
        CancellationToken cancellationToken = default)
    {
        var mqttMessage = new MqttMessage(topic, message)
        {
            QoS = qos,
            Retain = retain
        };

        return PublishAsync(mqttMessage, cancellationToken);
    }

    public Task<IAckResponse> SubscribeAsync(string topic, QualityOfService qos,
        CancellationToken cancellationToken = default)
    {
        var subscriptionOptions = new SubscriptionOptions
        {
            QoS = qos
        };
        var subscription = new TopicSubscription(topic)
        {
            Options = subscriptionOptions
        };

        return SubscribeAsync([subscription], cancellationToken);
    }

    public async Task<IAckResponse> SubscribeAsync(TopicSubscription[] topics,
        CancellationToken cancellationToken = default)
    {
        if (_transport.Status != ConnectionStatus.Connected)
            return new AckProtocol.SubscribeFailure("Not connected to broker.");

        var subscribePacket = new SubscribePacket()
        {
            PacketId = _packetIdCounter.GetNextValue(),
            Topics = topics
        };

        // Violates MQTT spec - we should never have a packet ID of 0 on Subscribe or Unsubscribe
        System.Diagnostics.Debug.Assert(subscribePacket.PacketId != 0, "PacketId should not be 0");

        _clientOwner.Tell(subscribePacket); // for reconnect support
        var askTask = _requiredActors.ClientAck.Ask<IAckResponse>(subscribePacket, cancellationToken);

        // flush the packet to the wire
        await _packetWriter.WriteAsync(subscribePacket, cancellationToken);

        // wait for the response
        try
        {
            var resp = await askTask;
            if (!resp.IsSuccess)
            {
                _log.Error("Failed to subscribe to topics - Reason: {0}", resp.Reason);
                return resp;
            }

            return resp;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to subscribe to topics - Reason: {0}", ex.Message);
            return new AckProtocol.SubscribeFailure(ex.Message);
        }
    }

    public ChannelReader<MqttMessage> ReceivedMessages { get; }

    public Task<IAckResponse> UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        return UnsubscribeAsync([topic], cancellationToken);
    }

    public async Task<IAckResponse> UnsubscribeAsync(string[] topics, CancellationToken cancellationToken = default)
    {
        if (_transport.Status != ConnectionStatus.Connected)
            return new AckProtocol.UnsubscribeFailure("Not connected to broker.");

        var unsubscribePacket = new UnsubscribePacket()
        {
            PacketId = _packetIdCounter.GetNextValue(),
            Topics = topics
        };

        // Violates MQTT spec - we should never have a packet ID of 0 on Subscribe or Unsubscribe
        System.Diagnostics.Debug.Assert(unsubscribePacket.PacketId != 0, "PacketId should not be 0");
        _clientOwner.Tell(unsubscribePacket); // for reconnect support
        var askTask = _requiredActors.ClientAck.Ask<IAckResponse>(unsubscribePacket, cancellationToken);

        // flush the packet to the wire
        await _packetWriter.WriteAsync(unsubscribePacket, cancellationToken);

        // wait for the response
        try
        {
            var resp = await askTask;
            if (!resp.IsSuccess)
            {
                _log.Error("Failed to unsubscribe to topics - Reason: {0}", resp.Reason);
                return resp;
            }

            return resp;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to unsubscribe to topics - Reason: {0}", ex.Message);
            return new AckProtocol.UnsubscribeFailure(ex.Message);
        }
    }

    public Task<DisconnectReasonCode> WhenTerminated { get; }

    public async ValueTask DisposeAsync()
    {
        if (_transport.Status is not (ConnectionStatus.Aborted or ConnectionStatus.Disconnected))
            await AbortConnectionAsync();
    }
}