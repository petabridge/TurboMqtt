namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to acknowledge the receipt of a Publish packet with <see cref="QualityOfService.ExactlyOnce"/>.
/// This packet type is part of the QoS 2 message flow.
/// </summary>
public sealed class PubRecPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.PubRec;

    // MQTT 5.0 - Optional Reason Code and Properties
    /// <summary>
    /// The Reason Code for the PUBREC, available in MQTT 5.0.
    /// </summary>
    public PubRecReasonCode? ReasonCode { get; set; } // MQTT 5.0 only

    /// <summary>
    /// The Reason String for the PUBREC, available in MQTT 5.0.
    /// </summary>
    public string ReasonString { get; set; } = string.Empty;

    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only

    public override string ToString()
    {
        return $"PubRec: [PacketIdentifier={PacketId}], [ReasonCode={ReasonCode}], [ReasonString={ReasonString}]";
    }
}

/// <summary>
///  Enum for PUBREC and PUBCOMP reason codes (as they share the same codes)
/// </summary>
public enum PubRecReasonCode
{
    Success = 0x00,
    NoMatchingSubscribers = 0x10,
    UnspecifiedError = 0x80,
    ImplementationSpecificError = 0x83,
    NotAuthorized = 0x87,
    TopicNameInvalid = 0x90,
    PacketIdentifierInUse = 0x91,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99
}