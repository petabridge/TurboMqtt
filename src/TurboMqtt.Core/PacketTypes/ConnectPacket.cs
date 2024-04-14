// -----------------------------------------------------------------------
// <copyright file="ConnectPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used to initiate a connection to the MQTT broker.
/// </summary>
public class ConnectPacket(MqttProtocolVersion protocolVersion) : MqttPacket
{
    public override MqttPacketType PacketType => MqttPacketType.Connect;

    public string ClientId { get; set; }
    public ushort KeepAlive { get; set; }
    public ConnectFlags Flags { get; set; }
    
    public MqttLastWill? Will { get; set; }
    
    public string? Username { get; set; }
    public ReadOnlyMemory<byte>? Password { get; set; }

    public string ProtocolName { get; set; } = string.Empty;
    
    public MqttProtocolVersion ProtocolVersion { get; } = protocolVersion;
    
    // MQTT 5.0 - Optional Properties
    public ushort ReceiveMaximum { get; set; } // MQTT 5.0 only
    public uint MaximumPacketSize { get; set; } // MQTT 5.0 only
    public ushort TopicAliasMaximum { get; set; } // MQTT 5.0 only
    public uint SessionExpiryInterval { get; set; } // MQTT 5.0 only
    public bool RequestProblemInformation { get; set; } // MQTT 5.0 only
    public bool RequestResponseInformation { get; set; } // MQTT 5.0 only
    public string? AuthenticationMethod { get; set; } // MQTT 5.0 only
    public ReadOnlyMemory<byte>? AuthenticationData { get; set; } // MQTT 5.0 only
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 custom properties


}

/// <summary>
/// Payload for the Last Will and Testament message.
/// </summary>
public sealed class MqttLastWill
{
    public MqttLastWill(string topic, ReadOnlyMemory<byte> message)
    {
        Topic = topic;
        Message = message;
        
        // throw if topic is invalid
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty.", nameof(topic));
    }

    public string Topic { get; }
    public ReadOnlyMemory<byte> Message { get; }
    
    // MQTT 5.0 - Optional Properties for Last Will and Testament
    public string? ResponseTopic { get; set; } // MQTT 5.0 only
    public ReadOnlyMemory<byte>? WillCorrelationData { get; set; } // MQTT 5.0 only
    public string? ContentType { get; set; } // MQTT 5.0 only
    public PayloadFormatIndicator PayloadFormatIndicator { get; set; } // MQTT 5.0 only
    public NonZeroUInt32 DelayInterval { get; set; } // MQTT 5.0 only
    public uint MessageExpiryInterval { get; set; } // MQTT 5.0 only
    public IReadOnlyDictionary<string, string>? WillProperties { get; set; } // MQTT 5.0 custom properties
    
    // QoS and Retain are determined by ConnectFlags
}

public struct ConnectFlags
{
    public bool UsernameFlag { get; set; }
    public bool PasswordFlag { get; set; }
    public bool WillRetain { get; set; }
    public QualityOfService WillQoS { get; set; }
    public bool WillFlag { get; set; }
    public bool CleanSession { get; set; }  // Renamed from CleanStart for 3.1.1 compatibility

    public byte Encode(MqttProtocolVersion version)
    {
        byte result = 0;
        if (UsernameFlag)
            result |= 0b1000_0000;
        if (PasswordFlag)
            result |= 0b0100_0000;
        if (version == MqttProtocolVersion.V5_0 && WillRetain)
            result |= 0b0010_0000;
        if (WillFlag)
            result |= 0b0000_0100;
        if (CleanSession)
            result |= 0b0000_0010;
        if (WillFlag) // Only encode Will QoS if Will is set
            result |= (byte)(((int)WillQoS & 0x03) << 3);

        return result;
    }
    
    public static ConnectFlags Decode(byte flags)
    {
        var result = new ConnectFlags
        {
            UsernameFlag = (flags & 0b1000_0000) != 0,
            PasswordFlag = (flags & 0b0100_0000) != 0,
            WillRetain = (flags & 0b0010_0000) != 0,
            WillFlag = (flags & 0b0000_0100) != 0,
            CleanSession = (flags & 0b0000_0010) != 0
        };

        if (result.WillFlag)
            result.WillQoS = (QualityOfService)((flags & 0b0001_1000) >> 3);
        
        // if ((flags & 0x38) != 0) // bits 3,4,5 [MQTT-3.1.2-11]
        // {
        //     throw new ArgumentOutOfRangeException(nameof(flags), "[MQTT-3.1.2-11]");
        // }

        return result;
    }
}
