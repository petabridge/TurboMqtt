// -----------------------------------------------------------------------
// <copyright file="MqttDecoderException.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Protocol;

/// <summary>
/// Thrown when the decoder encounters an error while parsing the MQTT packet.
/// </summary>
public sealed class MqttDecoderException : Exception
{
    public MqttPacketType? PacketType { get; }
    public MqttProtocolVersion ProtocolVersion { get; }

    public MqttDecoderException(string message, MqttProtocolVersion protocolVersion, MqttPacketType? packetType = null)
        : base(message)
    {
        ProtocolVersion = protocolVersion;
        PacketType = packetType;
    }

    public MqttDecoderException(string message, Exception innerEx, MqttProtocolVersion protocolVersion,
        MqttPacketType? packetType = null) : base(message,
        innerException: innerEx)
    {
        ProtocolVersion = protocolVersion;
        PacketType = packetType;
    }

    public override string ToString()
    {
        // check if there's an InnerException and log that too
        return $"MqttDecoderException[ProtocolVersion={ProtocolVersion}, PacketType={PacketType}]" +
               Environment.NewLine + base.ToString();
    }
}