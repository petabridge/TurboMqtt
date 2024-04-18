// -----------------------------------------------------------------------
// <copyright file="MqttClientConnectOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Client;

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
    public NonZeroUInt16 DelayInterval { get; set; } // MQTT 5.0 only
    public uint MessageExpiryInterval { get; set; } // MQTT 5.0 only
    public IReadOnlyDictionary<string, string>? WillProperties { get; set; } // MQTT 5.0 custom properties
    */
}

/// <summary>
/// All of the MQTT protocol-specific options that can be set for a given client.
/// </summary>
public sealed record MqttClientConnectOptions
{
    public MqttClientConnectOptions(string clientId, MqttProtocolVersion protocolVersion)
    {
        // validate the client ID
        var (isValid, errorMessage) = MqttClientIdValidator.ValidateClientId(clientId);
        if (!isValid)
        {
            throw new ArgumentException(errorMessage, nameof(clientId));
        }
        
        ClientId = clientId;
        ProtocolVersion = protocolVersion;
    }
    
    public string ClientId { get; }
    public MqttProtocolVersion ProtocolVersion { get; }

    public string? Username { get; init; }
    public string? Password { get; init; }
    public LastWillAndTestament? LastWill { get; init; }
    public bool CleanSession { get; init; } = true;
    public ushort KeepAliveSeconds { get; init; } = 60;

    public uint MaximumPacketSize { get; set; } = 1024 * 8;
    
    /// <summary>
    /// Used for de-duplication across all clients.
    /// </summary>
    /// <remarks>
    /// Defaults to 5000 retained packet IDs
    /// </remarks>
    public int MaxRetainedPacketIds { get; init; } = 5000;
    
    /// <summary>
    /// Maximum amount of time a packet ID can be retained for before it is considered stale and can be reused.
    /// </summary>
    /// <remarks>
    /// Defaults to 5 seconds.
    /// </remarks>
    public TimeSpan MaxPacketIdRetentionTime { get; init; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Maximum number of times a message can be retried before it is considered a failure.
    /// </summary>
    public int MaxPublishRetries { get; init; } = 3;
    
    /// <summary>
    /// The interval at which we should retry publishing a message if it fails.
    /// </summary>
    public TimeSpan PublishRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public ushort ReceiveMaximum { get; set; }
}