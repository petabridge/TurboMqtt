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
public sealed class ConnectPacket(MqttProtocolVersion protocolVersion) : MqttPacket
{
    public const string DefaultClientId = "turbomqtt";
    
    public override MqttPacketType PacketType => MqttPacketType.Connect;

    public string ClientId { get; set; } = DefaultClientId;
    public ushort KeepAliveSeconds { get; set; }
    public ConnectFlags Flags { get; set; }

    public MqttLastWill? Will
    {
        get;
        set;
    }

    private string? _username;
    public string? Username
    {
        get => _username;
        set
        {
            _username = value;
            
            // ensure that the username flag is set or unset
            var flags = Flags;
            flags.UsernameFlag = !string.IsNullOrEmpty(value);
            Flags = flags;
        }
    }

    private string? _password;

    public string? Password
    {
        get => _password;
        set
        {
            _password = value;
            // ensure that the password flag is set or unset
            var flags = Flags;
            flags.PasswordFlag = !string.IsNullOrEmpty(value);
            Flags = flags;
        }
    }

    public string ProtocolName { get; set; } = "MQTT";
    
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
    public ConnectFlags ConnectFlags { get; set; }

    public override string ToString()
    {
        return $"ConnectPacket(ClientId={ClientId}, KeepAliveSeconds={KeepAliveSeconds}, Flags={Flags}, Will={Will}, Username={Username}, Password={Password})";
    
    }
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
    public NonZeroUInt16 DelayInterval { get; set; } // MQTT 5.0 only
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

    public byte Encode()
    {
        int result = 0;
        if (UsernameFlag)
            result |= 0x80;
        if (PasswordFlag)
            result |= 0x40;
        if (WillRetain)
            result |= 0x20;
        if (WillFlag)
            result |= 0x04;
        if (CleanSession)
            result |= 0x02;
        if (WillFlag) // Only encode Will QoS if Will is set
            result |= (((int)WillQoS & 0x03) << 3);

        return (byte)result;
    }
    
    public static ConnectFlags Decode(byte flags)
    {
        var result = new ConnectFlags
        {
            UsernameFlag = (flags & 0x80) == 0x80,
            PasswordFlag = (flags & 0x40) == 0x40,
            WillRetain = (flags & 0x20) == 0x20,
            WillFlag = (flags & 0x04) == 0x04,
            CleanSession = (flags & 0x02) == 0x02
        };

        if (result.WillFlag)
            result.WillQoS = (QualityOfService)((flags & 0x18) >> 3);
        else if ((flags & 0x38) != 0) // reserved bit for Will 3,4,5
        {
            throw new ArgumentOutOfRangeException(nameof(flags), "[MQTT-3.1.2-11]");
        }

        return result;
    }
}
