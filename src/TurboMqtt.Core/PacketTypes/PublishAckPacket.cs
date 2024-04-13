// -----------------------------------------------------------------------
// <copyright file="PublishAckPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// All possible reason codes for the PubAck packet.
/// </summary>
public enum MqttPubAckReasonCode : byte
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

// add a static helper method that can turn a MqttPubAckReason code into a hard-coded string representation
internal static class MqttPubAckHelpers
{
    public static string ReasonCodeToString(MqttPubAckReasonCode reasonCode)
    {
        return reasonCode switch
        {
            MqttPubAckReasonCode.Success => "Success",
            MqttPubAckReasonCode.NoMatchingSubscribers => "NoMatchingSubscribers",
            MqttPubAckReasonCode.UnspecifiedError => "UnspecifiedError",
            MqttPubAckReasonCode.ImplementationSpecificError => "ImplementationSpecificError",
            MqttPubAckReasonCode.NotAuthorized => "NotAuthorized",
            MqttPubAckReasonCode.TopicNameInvalid => "TopicNameInvalid",
            MqttPubAckReasonCode.PacketIdentifierInUse => "PacketIdentifierInUse",
            MqttPubAckReasonCode.QuotaExceeded => "QuotaExceeded",
            MqttPubAckReasonCode.PayloadFormatInvalid => "PayloadFormatInvalid",
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode, null)
        };
    }
}

/// <summary>
/// Used to acknowledge the receipt of a Publish packet.
/// </summary>
public sealed class PublishAckPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.PubAck;

    // MQTT 5.0 Optional Properties

    /// <summary>
    /// Reason Code for the acknowledgment, available in MQTT 5.0.
    /// This field is optional and provides more detailed acknowledgment information.
    /// </summary>
    public MqttPubAckReasonCode ReasonCode { get; set; }

    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// These are key-value pairs that can be sent to provide additional information in the acknowledgment.
    /// </summary>
    public string ReasonString => MqttPubAckHelpers.ReasonCodeToString(ReasonCode);

    public override string ToString()
    {
        return $"PubAck: [PacketIdentifier={PacketId}] [ReasonCode={ReasonCode}]";
    }
}