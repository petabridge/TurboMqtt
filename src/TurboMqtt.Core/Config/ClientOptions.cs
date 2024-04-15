﻿// -----------------------------------------------------------------------
// <copyright file="ClientOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core;

/// <summary>
/// Last Will and Testament (LWT) message that will be published by the broker on behalf of the client
/// upon the client's disconnection.
/// </summary>
public sealed record LastWillAndTestament
{
    public LastWillAndTestament(string topic, ReadOnlyMemory<byte> message)
    {
        // validate the topic
        var (isValid, errorMessage) = MqttTopicValidator.ValidateSubscribeTopic(topic);
        if (!isValid)
        {
            throw new ArgumentException(errorMessage, nameof(topic));
        }
        
        Topic = topic;
        Message = message;
    }
    
    public string Topic { get; }
    public ReadOnlyMemory<byte> Message { get; }
    public QualityOfService QosLevel { get; init; }
    public bool Retain { get; init; }
   
    /* TODO: add these later once we get MQTT 5.0 support
    public string? ResponseTopic { get; set; } // MQTT 5.0 only
    public ReadOnlyMemory<byte>? WillCorrelationData { get; set; } // MQTT 5.0 only
    public string? ContentType { get; set; } // MQTT 5.0 only
    public PayloadFormatIndicator PayloadFormatIndicator { get; set; } // MQTT 5.0 only
    public NonZeroUInt32 DelayInterval { get; set; } // MQTT 5.0 only
    public uint MessageExpiryInterval { get; set; } // MQTT 5.0 only
    public IReadOnlyDictionary<string, string>? WillProperties { get; set; } // MQTT 5.0 custom properties
    */
}

public sealed record ClientOptions
{
    public ClientOptions(string clientId, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1)
    {
        ClientId = clientId;
        ProtocolVersion = protocolVersion;
    }

    public string ClientId { get; }
    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V3_1_1;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public LastWillAndTestament? LastWill { get; init; }
    public bool CleanSession { get; init; } = true;
}