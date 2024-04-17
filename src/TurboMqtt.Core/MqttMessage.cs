// -----------------------------------------------------------------------
// <copyright file="MqttMessage.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core;

/// <summary>
/// Represents a message received from the MQTT broker.
/// </summary>
public sealed record MqttMessage
{
    public MqttMessage(string topic, ReadOnlyMemory<byte> payload)
    {
        Topic = topic;
        
        // validate the topic
        var (isValid, errorMessage) = MqttTopicValidator.ValidateSubscribeTopic(topic);
        if (!isValid)
        {
            throw new ArgumentException(errorMessage, nameof(topic));
        }
        
        Payload = payload;
    }

    public string Topic { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public QualityOfService QoS { get; init; }
    public bool Retain { get; init; }
    
    public PayloadFormatIndicator PayloadFormatIndicator { get; init; } // MQTT 5.0 only

    /// <summary>
    /// The Content Type property, available in MQTT 5.0.
    /// This property is optional and indicates the MIME type of the application message.
    /// </summary>
    public string? ContentType { get; init; } // MQTT 5.0 only

    /// <summary>
    /// Response Topic property, available in MQTT 5.0.
    /// It specifies the topic name for a response message.
    /// </summary>
    public string? ResponseTopic { get; init; } // MQTT 5.0 only

    /// <summary>
    /// Correlation Data property, available in MQTT 5.0.
    /// This property is used by the sender of the request message to identify which request the response message is for when it receives a response.
    /// </summary>
    public ReadOnlyMemory<byte>? CorrelationData { get; init; } // MQTT 5.0 only

    /// <summary>
    /// User Property, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; init; } // MQTT 5.0 only
}

/// <summary>
/// INTERNAL API
/// </summary>
internal static class MqttMessageExtensions
{
    internal static MqttMessage FromPacket(PublishPacket packet)
    {
        return new MqttMessage(packet.TopicName, packet.Payload)
        {
            QoS = packet.QualityOfService,
            Retain = packet.RetainRequested,
            PayloadFormatIndicator = packet.PayloadFormatIndicator,
            ContentType = packet.ContentType,
            ResponseTopic = packet.ResponseTopic,
            CorrelationData = packet.CorrelationData,
            UserProperties = packet.UserProperties
        };
    }
    
    // create a ToPacket method here
    internal static PublishPacket ToPacket(this MqttMessage message)
    {
        var packet = new PublishPacket(message.QoS, false, message.Retain, message.Topic)
        {
            Payload = message.Payload,
            PayloadFormatIndicator = message.PayloadFormatIndicator,
            ContentType = message.ContentType,
            ResponseTopic = message.ResponseTopic,
            CorrelationData = message.CorrelationData,
            UserProperties = message.UserProperties
        };

        return packet;
    }
}