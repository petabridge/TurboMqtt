// -----------------------------------------------------------------------
// <copyright file="PublishPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.PacketTypes;

/// <summary>
/// Used to send data to the server or client.
/// </summary>
public sealed class PublishPacket : MqttPacketWithId
{
    /// <summary>
    /// Creates a new publish packet, of course.
    /// </summary>
    /// <param name="qos">The delivery guarantee for this packet.</param>
    /// <param name="duplicate">Is this packet a duplicate?</param>
    /// <param name="retainRequested">Indicates whether or not this value has been retained by the MQTT broker.</param>
    /// <param name="topicName">The name of the topic we're publishing to.</param>
    public PublishPacket(QualityOfService qos, bool duplicate, bool retainRequested, string topicName)
    {
        QualityOfService = qos;
        Duplicate = duplicate;
        RetainRequested = retainRequested;
        TopicName = topicName;
    }
    
    public override MqttPacketType PacketType => MqttPacketType.Publish;
    

    public override QualityOfService QualityOfService { get; }

    public override bool RetainRequested { get; }
    
    public ushort TopicAlias { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Optional for <see cref="QualityOfService.AtMostOnce"/>
    /// </summary>
    public string TopicName { get; }

    public uint MessageExpiryInterval { get; set; } // MQTT 5.0 only

    // Payload
    public ReadOnlyMemory<byte> Payload { get; set; } = ReadOnlyMemory<byte>.Empty;

    // MQTT 3.1.1 and 5.0 - Optional Properties
    public PayloadFormatIndicator PayloadFormatIndicator { get; set; } // MQTT 5.0 only

    /// <summary>
    /// The Content Type property, available in MQTT 5.0.
    /// This property is optional and indicates the MIME type of the application message.
    /// </summary>
    public string? ContentType { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Response Topic property, available in MQTT 5.0.
    /// It specifies the topic name for a response message.
    /// </summary>
    public string? ResponseTopic { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Correlation Data property, available in MQTT 5.0.
    /// This property is used by the sender of the request message to identify which request the response message is for when it receives a response.
    /// </summary>
    public ReadOnlyMemory<byte>? CorrelationData { get; set; } // MQTT 5.0 only

    /// <summary>
    /// User Property, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Subscription Identifiers, available in MQTT 5.0.
    /// This property allows associating the publication with multiple subscriptions.
    /// Each identifier corresponds to a different subscription that matches the published message.
    /// </summary>
    public IReadOnlyList<uint>? SubscriptionIdentifiers { get; set; } // MQTT 5.0 only

    public override string ToString()
    {
        return
            $"Pub: [Topic={TopicName}] [PayloadLength={Payload.Length}] [QoSLevel={QualityOfService}] [Dup={Duplicate}] [Retain={RetainRequested}] [PacketIdentifier={PacketId}]";
    }
}

public enum PayloadFormatIndicator : byte
{
    /// <summary>
    /// The payload is unspecified bytes, which should not be interpreted as UTF-8 encoded character data.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The payload is UTF-8 encoded character data.
    /// </summary>
    Utf8Encoded = 1
}

/// <summary>
/// Helper methods for generating QoS responses to <see cref="PublishPacket"/>s.
/// </summary>
internal static class PublishPacketExtensions
{
    public static PubAckPacket ToPubAck(this PublishPacket packet)
    {
        return new PubAckPacket()
        {
            Duplicate = false,
            PacketId = packet.PacketId,
            ReasonCode = MqttPubAckReasonCode.Success
        };
    }
    
    public static PubRecPacket ToPubRec(this PublishPacket packet)
    {
        return new PubRecPacket()
        {
            Duplicate = false,
            PacketId = packet.PacketId,
            ReasonCode = PubRecReasonCode.Success,
            ReasonString = "Success",
            UserProperties = packet.UserProperties
        };
    }
    
    public static PubRelPacket ToPubRel(this PublishPacket packet)
    {
        return new PubRelPacket()
        {
            Duplicate = false,
            PacketId = packet.PacketId,
            ReasonCode = PubRelReasonCode.Success,
            ReasonString = "Success",
            UserProperties = packet.UserProperties
        };
    }
    
    public static PubCompPacket ToPubComp(this PublishPacket packet)
    {
        return new PubCompPacket()
        {
            Duplicate = false,
            PacketId = packet.PacketId,
            ReasonCode = PubCompReasonCode.Success,
            ReasonString = "Success",
            UserProperties = packet.UserProperties
        };
    }
}