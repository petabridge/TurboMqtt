// -----------------------------------------------------------------------
// <copyright file="MqttEncoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

public static class MqttEncoder
{
    public static int EstimatePacketSize(MqttPacket packet,
        MqttProtocolVersion protocolVersion = MqttProtocolVersion.V5_0)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Connect:
                break;
            case MqttPacketType.ConnAck:
                break;
            case MqttPacketType.Publish:
                break;
            case MqttPacketType.PubAck:
                break;
            case MqttPacketType.PubRec:
                break;
            case MqttPacketType.PubRel:
                break;
            case MqttPacketType.PubComp:
                break;
            case MqttPacketType.Subscribe:
                break;
            case MqttPacketType.SubAck:
                break;
            case MqttPacketType.Unsubscribe:
                break;
            case MqttPacketType.UnsubAck:
                break;
            case MqttPacketType.PingReq:
            case MqttPacketType.PingResp:
            case MqttPacketType.Disconnect when protocolVersion < MqttProtocolVersion.V5_0:
                return 2; // fixed header only
            case MqttPacketType.Disconnect when protocolVersion == MqttProtocolVersion.V5_0:
                
            case MqttPacketType.Auth:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    /// <summary>
    ///  Helper method to calculate the length of the Variable Byte Integer for MQTT packet lengths
    /// </summary>
    /// <remarks>
    /// Packets in MQTT can have a length header between 1-4 bytes long.
    /// </remarks>
    private static int GetPacketHeaderSize(int packetBodyLength)
    {
        return packetBodyLength switch
        {
            < 128 => 1,
            < 16384 => 2,
            < 2097152 => 3,
            _ => 4
        };
    }
    
    private static int ComputeUserPropertiesSize(IReadOnlyDictionary<string, string> userProperties)
    {
        var userPropertiesSize = 0;
        foreach (var (key, value) in userProperties)
        {
            userPropertiesSize += 2 + Encoding.UTF8.GetByteCount(key) + 2 + Encoding.UTF8.GetByteCount(value);
        }

        return userPropertiesSize;
    }
    
    /// <summary>
    /// Gets just the packet size back - **does not include the size of the length header**
    /// </summary>
    /// <param name="packet">The <see cref="DisconnectPacket"/></param>
    /// <returns>The size of the packet not including the length headers.</returns>
    /// <remarks>
    /// Only used for MQTT 5.0 packets.
    /// </remarks>
    private static int EstimateDisconnectPacketSizeMqtt5(DisconnectPacket packet)
    {
        var size = 2; // Start with 2 bytes for the fixed header

        if (packet.ReasonCode.HasValue)
        {
            size += 1; // Reason code is 1 byte
        }

        // Start calculating the properties size
        var propertiesSize = 0;

        if (packet.UserProperties != null && packet.UserProperties.Any())
        {
            propertiesSize = ComputeUserPropertiesSize(packet.UserProperties);
        }

        if (!string.IsNullOrEmpty(packet.ServerReference))
        {
            propertiesSize += 2 + Encoding.UTF8.GetByteCount(packet.ServerReference);
        }

        if (packet.SessionExpiryInterval.HasValue)
        {
            propertiesSize += 5; // 1 byte for the identifier plus 4 bytes for the value
        }

        return size + propertiesSize;
    }
}