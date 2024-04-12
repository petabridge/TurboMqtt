// -----------------------------------------------------------------------
// <copyright file="UnsubscribePacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to unsubscribe from topics.
/// </summary>
public sealed class UnsubscribePacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.Unsubscribe;

    /// <summary>
    /// The set of topics we're unsubscribing from.
    /// </summary>
    public IReadOnlyList<string> Topics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// User Property, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only    

    public override string ToString()
    {
        return $"Unsubscribe: [PacketIdentifier={PacketId}] [Topics={string.Join(", ", Topics)}]";
    }
}