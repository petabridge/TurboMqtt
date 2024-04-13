// -----------------------------------------------------------------------
// <copyright file="MqttEncoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

internal static class MqttPacketSizeEstimator
{
    internal const int PacketIdLength = 2;
    internal const int StringSizeLength = 2;
    internal const int MaxVariableLength = 4;
    internal const string Mqtt5ProtocolName = "MQTT";
    internal const string Mqtt311ProtocolName = "MQIsdp";
    
    /// <summary>
    /// Estimates the size of the packet WITHOUT the length header.
    /// </summary>
    /// <param name="packet">The packet to estimate.</param>
    /// <param name="protocolVersion">The version of the MQTT protocol we're encoding the packet for.</param>
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized MQTT protocol version is supplied.</exception>
    public static int EstimatePacketSize(MqttPacket packet,
        MqttProtocolVersion protocolVersion = MqttProtocolVersion.V5_0)
    {
        switch (protocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
                return EstimateMqtt3PacketSize(packet);
            case MqttProtocolVersion.V5_0:
                return EstimateMqtt5PacketSize(packet);
            default:
                throw new ArgumentOutOfRangeException(nameof(protocolVersion), protocolVersion, null);
        }
    }
    
    /// <summary>
    /// Estimates the size of the packet WITHOUT the length header.
    /// </summary>
    /// <param name="packet">The packet to estimate.</param>
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized packet type is supplied.</exception>
    public static int EstimateMqtt3PacketSize(MqttPacket packet)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Connect:
                return EstimateConnectPacketSizeMqtt311((ConnectPacket)packet);
            case MqttPacketType.ConnAck:
                break;
            case MqttPacketType.Publish:
                break;
            case MqttPacketType.PubAck:
            case MqttPacketType.PubRec:
            case MqttPacketType.PubRel:
            case MqttPacketType.PubComp:
            case MqttPacketType.SubAck:
            case MqttPacketType.UnsubAck:
                return 3; // fixed header + packet id only
            case MqttPacketType.Subscribe:
                break;
            case MqttPacketType.Unsubscribe:
                break;
            case MqttPacketType.PingReq:
            case MqttPacketType.PingResp:
            case MqttPacketType.Disconnect:
                return 2; // fixed header only
            case MqttPacketType.Auth: // this should throw for AUTH packets in MQTT3, which are not supported (MQTT5 and up only)
            default: 
                throw new ArgumentOutOfRangeException(nameof(packet), packet.PacketType, null);
        }

        return -1;
    }

    /// <summary>
    /// Estimates the size of the packet WITHOUT the length header.
    /// </summary>
    /// <param name="packet">The packet to estimate.</param>
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized packet type is supplied.</exception>
    /// <remarks>
    /// MQTT5 includes many additional properties aimed at making debuggability easier, but they also increase the size of the packet.
    /// </remarks>
    public static int EstimateMqtt5PacketSize(MqttPacket packet)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Connect:
                return EstimateConnectPacketSizeMqtt5((ConnectPacket)packet);
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
                return 2; // fixed header only
            case MqttPacketType.Disconnect:
                return EstimateDisconnectPacketSizeMqtt5((DisconnectPacket)packet);
            case MqttPacketType.Auth:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return -1;
    }
    
    /// <summary>
    ///  Helper method to calculate the length of the Variable Byte Integer for MQTT packet lengths
    /// </summary>
    /// <remarks>
    /// Packets in MQTT can have a length header between 1-4 bytes long.
    ///
    /// We subtract 2 bytes from the packet body length to account for the fixed length header.
    /// </remarks>
    public static int GetPacketHeaderSize(int packetBodyLength)
    {
        // remove 2 bytes for the fixed header, which isn't included in the length
        return packetBodyLength - 2 switch
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
            // Include 1 byte for the property identifier for each user property
            userPropertiesSize += 1; // Property identifier byte for "User Property"
            userPropertiesSize += 2 + Encoding.UTF8.GetByteCount(key); // Length of key + key bytes
            userPropertiesSize += 2 + Encoding.UTF8.GetByteCount(value); // Length of value + value bytes
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

    private static int EstimateConnectPacketSizeMqtt311(ConnectPacket packet)
    {
        var size = 2; // Start with 2 bytes for the fixed header
        // Variable header:
        
        /*
            +-------------------+-----------------+----------------+------------+----------------+
            |  Protocol Name    | Protocol Version| Connect Flags  |  Keep Alive|    Properties  |
            |      X Bytes      |      1 Byte     |     1 Byte     |   2 Bytes  |      X Bytes   |
            +-------------------+-----------------+----------------+------------+----------------+
            */

        
        // Protocol Name (2 bytes length + actual length of string)

        size += 2 + Encoding.ASCII.GetByteCount(Mqtt311ProtocolName); // MQTT uses ASCII, not UTF8, for the protocol name: https://www.emqx.com/en/blog/mqtt-5-0-control-packets-01-connect-connack#connect-packet-structure
        
        // Protocol Version (1 byte)
        size += 1;

        // Connect Flags (1 byte)
        size += 1;

        // Keep Alive (2 bytes)
        size += 2;
        
        var payloadSize = 0;
        payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.ClientId);
        
        // compute size of LastWillAndTestament, excluding any optional properties only available in MQTT 3.11
        if (packet.Will != null)
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Will.Topic);
            payloadSize += 2 + packet.Will.Message.Length;
        }
        
        if (!string.IsNullOrEmpty(packet.Username))
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Username);
        }
        
        if (packet.Password.HasValue)
        {
            payloadSize += 2 + packet.Password.Value.Length;
        }
        
        return size + payloadSize;
    }

    private static int EstimateConnectPacketSizeMqtt5(ConnectPacket packet)
    {
        var size = 2; // Start with 2 bytes for the fixed header
        
        // Variable header:
        
        /*
            +-------------------+-----------------+----------------+------------+----------------+
            |  Protocol Name    | Protocol Version| Connect Flags  |  Keep Alive|    Properties  |
            |      X Bytes      |      1 Byte     |     1 Byte     |   2 Bytes  |      X Bytes   |
            +-------------------+-----------------+----------------+------------+----------------+
            */

        
        // Protocol Name (2 bytes length + actual length of string)

        size += 2 + Encoding.ASCII.GetByteCount(Mqtt5ProtocolName); // MQTT uses ASCII, not UTF8, for the protocol name: https://www.emqx.com/en/blog/mqtt-5-0-control-packets-01-connect-connack#connect-packet-structure
        
        // Protocol Version (1 byte)
        size += 1;

        // Connect Flags (1 byte)
        size += 1;

        // Keep Alive (2 bytes)
        size += 2;
        
        // Start calculating the properties size
        var propertiesSize = 0;
        
        /*
            | Identifier | Property Name                | Type                 |
            |------------|------------------------------|----------------------|
            | 0x11       | Session Expiry Interval      | Four Byte Integer    |
            | 0x21       | Receive Maximum              | Two Byte Integer     |
            | 0x27       | Maximum Packet Size          | Four Byte Integer    |
            | 0x22       | Topic Alias Maximum          | Two Byte Integer     |
            | 0x19       | Request Response Information | Byte                 |
            | 0x17       | Request Problem Information  | Byte                 |
            | 0x26       | User Property                | UTF-8 String Pair    |
            | 0x15       | Authentication Method        | UTF-8 Encoded String |
            | 0x16       | Authentication Data          | Binary Data          |
            
            Source: https://www.emqx.com/en/blog/mqtt-5-0-control-packets-01-connect-connack#connect-packet-structure
         */        

        // "OINK OINK! 🐷" the premature optimizer says! "Why not just turn this into a single constant value?"
        // Because the JIT will do that anyway - in the meantime, I want whoever is working on this code to understand
        // where the numbers come from. - Aaron
        propertiesSize += 1 + 4; // Session expiry interval
        
        // so why a "1 + n" for each of these? The 1 represents the 1 byte const delimiter in the header, the "n" represents the actual size of the value
        
        propertiesSize += 1 + 2; // Receive maximum
        propertiesSize += 1 + 4; // Maximum packet size
        propertiesSize += 1 + 2; // Topic alias maximum
        propertiesSize += 1 + 1; // Request response information
        propertiesSize += 1 + 1; // Request problem information
        
        if(packet.UserProperties != null && packet.UserProperties.Any())
        {
            propertiesSize = ComputeUserPropertiesSize(packet.UserProperties);
        }
        
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
        {
            propertiesSize += 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        }
        
        if (packet.AuthenticationData.HasValue)
        {
            propertiesSize += 2 + packet.AuthenticationData.Value.Length;
        }
        
        var payloadSize = 0;
        payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.ClientId);
        
        if (packet.Will != null)
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Will.Topic);
            payloadSize += 2 + packet.Will.Message.Length;
            
            if (!string.IsNullOrEmpty(packet.Will.ResponseTopic))
            {
                payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Will.ResponseTopic);
            }
            
            if (packet.Will.WillCorrelationData.HasValue)
            {
                payloadSize += 2 + packet.Will.WillCorrelationData.Value.Length;
            }
            
            if (!string.IsNullOrEmpty(packet.Will.ContentType))
            {
                payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Will.ContentType);
            }
            
            if (packet.Will.WillProperties != null && packet.Will.WillProperties.Any())
            {
                payloadSize += ComputeUserPropertiesSize(packet.Will.WillProperties);
            }
        }
        
        if (!string.IsNullOrEmpty(packet.Username))
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Username);
        }
        
        if (packet.Password.HasValue)
        {
            payloadSize += 2 + packet.Password.Value.Length;
        }
        
        return size + propertiesSize + payloadSize;
    }
}