// -----------------------------------------------------------------------
// <copyright file="SubscribeAckPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

public enum MqttSubscribeReasonCode : byte
{
    // Common reason codes in MQTT 3.1.1 and earlier versions (implicitly used, typically not explicitly specified in these versions)
    GrantedQoS0 = 0x00, // Maximum QoS 0, MQTT 3.0, 3.1.1
    GrantedQoS1 = 0x01, // Maximum QoS 1, MQTT 3.0, 3.1.1
    GrantedQoS2 = 0x02, // Maximum QoS 2, MQTT 3.0, 3.1.1

    // MQTT 5.0 specific reason codes
    UnspecifiedError = 0x80, // MQTT 5.0
    ImplementationSpecificError = 0x83, // MQTT 5.0
    NotAuthorized = 0x87, // MQTT 5.0
    TopicFilterInvalid = 0x8F, // MQTT 5.0
    PacketIdentifierInUse = 0x91, // MQTT 5.0
    QuotaExceeded = 0x97, // MQTT 5.0
    SharedSubscriptionsNotSupported = 0x9E, // MQTT 5.0
    SubscriptionIdentifiersNotSupported = 0xA1, // MQTT 5.0
    WildcardSubscriptionsNotSupported = 0xA2, // MQTT 5.0
}

/// <summary>
/// Represents the acknowledgement packet for a subscription request.
/// </summary>
public sealed class SubAckPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.SubAck;

    /// <summary>
    /// The reason codes for each topic subscription.
    /// </summary>
    public IReadOnlyList<MqttSubscribeReasonCode> ReasonCodes { get; set; } = Array.Empty<MqttSubscribeReasonCode>();
    
    /// <summary>
    /// The reason string for the subscription.
    /// </summary>
    /// <remarks>
    /// This property is only used in MQTT v5.0.0 and later.
    /// </remarks>
    public string? ReasonString { get; set; }
    
    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only
    
    public override string ToString()
    {
        var reasonCodesText = string.Join(",", ReasonCodes.Select(f => f.ToString()));

        return $"SubAck: [PacketIdentifier={PacketId}] [ReasonCode={reasonCodesText}]";
    }
}