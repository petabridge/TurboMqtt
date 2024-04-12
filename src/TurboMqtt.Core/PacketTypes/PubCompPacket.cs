// -----------------------------------------------------------------------
// <copyright file="PubCompPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to acknowledge the receipt of a <see cref="PubRelPacket"/> from the client.
/// This is the final packet in the QoS 2 message flow.
/// </summary>
public sealed class PubCompPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.PubComp;

    // MQTT 5.0 - Optional Reason Code and Properties
    /// <summary>
    /// The Reason Code for the PUBCOMP, available in MQTT 5.0.
    /// </summary>
    public PubCompReasonCode? ReasonCode { get; set; } // MQTT 5.0 only

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
        return $"PubComp: [PacketIdentifier={PacketId}], [ReasonCode={ReasonCode}], [ReasonString={ReasonString}]";
    }
}

/// <summary>
/// Enum for PUBCOMP reason codes, using the same as PUBREC for simplicity and because MQTT 5.0 reuses these
/// </summary>
public enum PubCompReasonCode
{
    Success = 0x00,
    PacketIdentifierNotFound = 0x92
}