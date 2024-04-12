namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Unsubscribe Acknowledgment Reason Codes.
/// </summary>
/// <remarks>
/// This is an MQTT 5.0 feature.
/// </remarks>
public enum MqttUnsubscribeReasonCode
{
    // MQTT 5.0 specific reason codes
    Success = 0x00, // The subscription is deleted successfully, MQTT 5.0
    NoSubscriptionExisted = 0x11, // No subscription existed for the specified topic filter, MQTT 5.0
    UnspecifiedError = 0x80, // The unsubscribe could not be completed and the reason is not specified, MQTT 5.0
    ImplementationSpecificError = 0x83, // The unsubscribe could not be completed due to an implementation-specific error, MQTT 5.0
    NotAuthorized = 0x87, // The client was not authorized to unsubscribe, MQTT 5.0
    TopicFilterInvalid = 0x8F, // The specified topic filter is invalid, MQTT 5.0
    PacketIdentifierInUse = 0x91, // The Packet Identifier is already in use, MQTT 5.0
}

/// <summary>
/// Used to acknowledge an unsubscribe request.
/// </summary>
public sealed class UnsubscribeAckPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.UnsubAck;
    
    /// <summary>
    /// Set of unsubscribe reason codes.
    /// </summary>
    /// <remarks>
    /// Available in MQTT 5.0.
    /// </remarks>
    public IReadOnlyList<MqttUnsubscribeReasonCode> ReasonCodes { get; set; } = Array.Empty<MqttUnsubscribeReasonCode>();

    /// <summary>
    /// Reason given by the server for the unsubscribe.
    /// </summary>
    /// <remarks>
    /// Available in MQTT 5.0.
    /// </remarks>
    public string ReasonString { get; set; } = string.Empty;
    
    /// <summary>
    /// User Property, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only    
    
    public override string ToString()
    {
        return $"Unsubscribe Ack: [PacketIdentifier={PacketId}] [ReasonCodes={string.Join(", ", ReasonCodes)}]";
    }
}