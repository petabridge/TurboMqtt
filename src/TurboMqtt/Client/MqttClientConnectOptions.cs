// -----------------------------------------------------------------------
// <copyright file="MqttClientConnectOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Client;

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

    // MQTT 5.0 Will properties
    public string? ResponseTopic { get; init; } // MQTT 5.0 only
    public ReadOnlyMemory<byte>? WillCorrelationData { get; init; } // MQTT 5.0 only
    public string? ContentType { get; init; } // MQTT 5.0 only
    public PayloadFormatIndicator PayloadFormatIndicator { get; init; } // MQTT 5.0 only
    public NonZeroUInt16 DelayInterval { get; init; } // MQTT 5.0 only
    public uint MessageExpiryInterval { get; init; } // MQTT 5.0 only
    public IReadOnlyDictionary<string, string>? WillProperties { get; init; } // MQTT 5.0 custom properties
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

    public string? UserName { get; init; }
    public string? Password { get; init; }
    public LastWillAndTestament? LastWill { get; init; }
    public bool CleanSession { get; init; } = true;
    public ushort KeepAliveSeconds { get; init; } = 5;

    public uint MaximumPacketSize { get; init; } = 1024 * 32;

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

    public ushort ReceiveMaximum { get; init; }

    /// <summary>
    /// When set to <c>true</c> (default), causes each <see cref="IMqttClient"/> to emit OpenTelemetry metrics.
    /// </summary>
    public bool EnableOpenTelemetry { get; init; } = true;

    /// <summary>
    /// Maximum number of consecutive times we should attempt to reconnect to the broker before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>
    /// Maximum amount of time to wait for a single reconnect attempt to complete before giving up.
    /// </summary>
    /// <remarks>
    /// Defaults to 5 seconds.
    /// </remarks>
    public TimeSpan ReconnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Optional MQTT 5.0 Enhanced Authentication handler.
    /// When set, the client sends <c>Authentication Method</c> and <c>Authentication Data</c>
    /// with the CONNECT packet and participates in challenge-response authentication.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// Ignored for MQTT 3.1.1 connections.
    /// </remarks>
    public IMqtt5AuthHandler? AuthHandler { get; init; }

    // ── MQTT 5.0 CONNECT properties ──────────────────────────────────────────

    /// <summary>
    /// Session Expiry Interval in seconds (MQTT 5.0 only).
    /// A value of 0 or absent means the session expires on disconnection.
    /// 0xFFFFFFFF means the session does not expire.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// </remarks>
    public uint SessionExpiryInterval { get; init; }

    /// <summary>
    /// Maximum number of topic aliases the client will accept from the server (MQTT 5.0 only).
    /// A value of 0 means the client does not accept topic aliases from the server.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// </remarks>
    public ushort TopicAliasMaximum { get; init; }

    /// <summary>
    /// When <c>true</c>, the client requests that the server send response information
    /// in the CONNACK packet (MQTT 5.0 only).
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// </remarks>
    public bool RequestResponseInformation { get; init; }

    /// <summary>
    /// When <c>false</c>, the server may omit Reason String and User Properties from
    /// CONNACK, PUBACK, PUBREC, PUBREL, PUBCOMP, SUBACK, UNSUBACK, and DISCONNECT packets
    /// to reduce overhead (MQTT 5.0 only).
    /// Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// </remarks>
    public bool RequestProblemInformation { get; init; } = true;

    /// <summary>
    /// Optional user-defined name-value pairs to send with the CONNECT packet (MQTT 5.0 only).
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ProtocolVersion"/> is <see cref="MqttProtocolVersion.V5_0"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? UserProperties { get; init; }
}
