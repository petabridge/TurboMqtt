namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to acknowledge the receipt of a <see cref="PubRecPacket"/> from the broker.
/// This packet type is part of the QoS 2 message flow.
/// </summary>
public sealed class PubRelPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.PubRel;

    // MQTT 5.0 - Optional Reason Code and Properties
    /// <summary>
    /// The Reason Code for the PUBREL, available in MQTT 5.0.
    /// </summary>
    public PubRelReasonCode? ReasonCode { get; set; } // MQTT 5.0 only

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
        return $"PubRel: [PacketIdentifier={PacketId}], [ReasonCode={ReasonCode}], [ReasonString={ReasonString}]";
    }
}

/// <summary>
/// Enum for PUBREL reason codes (typically these would be simpler as successful flow is usually assumed)
/// </summary>
public enum PubRelReasonCode
{
    Success = 0x00,
    PacketIdentifierNotFound = 0x92
}