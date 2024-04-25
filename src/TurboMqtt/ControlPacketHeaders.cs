// -----------------------------------------------------------------------
// <copyright file="ControlPacketHeaders.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace TurboMqtt;

/// <summary>
/// The type of MQTT packet.
/// </summary>
/// <remarks>
/// Aligns to the MQTT 3.1.1 specification: https://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html
///
/// Also supports MQTT 5.0.
/// </remarks>
public enum MqttPacketType
{
    Connect = 1,
    ConnAck =2,
    Publish = 3,
    PubAck = 4,
    PubRec = 5,
    PubRel = 6,
    PubComp = 7,
    Subscribe = 8,
    SubAck = 9,
    Unsubscribe = 10,
    UnsubAck = 11,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14,
    Auth = 15
}