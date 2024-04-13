// -----------------------------------------------------------------------
// <copyright file="PublishPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to send data to the server or client.
/// </summary>
/// <param name="Qos">The delivery guarantee for this packet.</param>
/// <param name="Duplicate">Is this packet a duplicate?</param>
/// <param name="RetainRequested">Indicates whether or not this value has been retained by the MQTT broker.</param>
public sealed class PublishPacket(QualityOfService Qos, bool Duplicate, bool RetainRequested) : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.Publish;

    public override bool Duplicate { get; } = Duplicate;

    public override QualityOfService QualityOfService { get; } = Qos;

    public override bool RetainRequested { get; } = RetainRequested;
    
    public ushort TopicAlias { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Optional for <see cref="QualityOfService.AtMostOnce"/>
    /// </summary>
    public string? TopicName { get; set; }
    
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
            $"Publish: [Topic={TopicName}] [PayloadLength={Payload.Length}] [QoSLevel={QualityOfService}] [Dup={Duplicate}] [Retain={RetainRequested}] [PacketIdentifier={PacketId}]";
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