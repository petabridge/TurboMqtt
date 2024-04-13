// -----------------------------------------------------------------------
// <copyright file="ConnAckPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used by the broker to acknowledge a connection request from a client.
/// </summary>
public sealed class ConnAckPacket : MqttPacket
{
    public override MqttPacketType PacketType => MqttPacketType.ConnAck;

    public bool SessionPresent { get; set; }
    public ConnAckReasonCode ReasonCode { get; set; } // Enum defined below

    // MQTT 5.0 - Optional Properties
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; }

    public override string ToString()
    {
        return $"ConnAck: [SessionPresent={SessionPresent}] [ReasonCode={ReasonCode}]";
    }
}

public enum ConnAckReasonCode : byte
{
    Success = 0x00,
    UnspecifiedError = 0x80,
    MalformedPacket = 0x81,
    ProtocolError = 0x82,
    ImplementationSpecificError = 0x83,
    UnsupportedProtocolVersion = 0x84,
    ClientIdentifierNotValid = 0x85,
    BadUsernameOrPassword = 0x86,
    NotAuthorized = 0x87,
    ServerUnavailable = 0x88,
    ServerBusy = 0x89,
    Banned = 0x8A,
    BadAuthenticationMethod = 0x8C,
    TopicNameInvalid = 0x90,
    PacketTooLarge = 0x95,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99,
    RetainNotSupported = 0x9A,
    QoSNotSupported = 0x9B,
    UseAnotherServer = 0x9C,
    ServerMoved = 0x9D,
    ConnectionRateExceeded = 0x9F
}