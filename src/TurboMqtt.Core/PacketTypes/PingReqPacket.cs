// -----------------------------------------------------------------------
// <copyright file="PingReqPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Packet sent to the client by the server in response to a <see cref="PingReqPacket"/>.
/// </summary>
/// <remarks>
/// Used to keep the connection alive.
/// </remarks>
public sealed class PingReqPacket : MqttPacket
{
    public static readonly PingReqPacket Instance = new PingReqPacket();

    private PingReqPacket()
    {
    }

    public override MqttPacketType PacketType => MqttPacketType.PingReq;

    public override string ToString()
    {
        return "PingReq";
    }
}