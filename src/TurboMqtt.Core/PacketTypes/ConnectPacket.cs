// -----------------------------------------------------------------------
// <copyright file="ConnectPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to initiate a connection to the MQTT broker.
/// </summary>
public class ConnectPacket(string clientId) : MqttPacket
{
    public override MqttPacketType PacketType => MqttPacketType.Connect;

    public string ClientId { get; } = clientId;
    public bool CleanSession { get; set; }
    public ushort KeepAlive { get; set; }

    // MQTT 5.0 - Optional Properties
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool? WillFlag { get; set; }
    public string? WillTopic { get; set; }
    public ReadOnlyMemory<byte>? WillMessage { get; set; }
    public QualityOfService? WillQos { get; set; }
    public bool? WillRetain { get; set; }

    public IReadOnlyDictionary<string, string>?
        Properties { get; set; } // Custom properties like Session Expiry Interval, Maximum Packet Size, etc.

    public override string ToString()
    {
        return $"Connect: [ClientId={ClientId}] [CleanSession={CleanSession}] [KeepAlive={KeepAlive}]";
    }
}