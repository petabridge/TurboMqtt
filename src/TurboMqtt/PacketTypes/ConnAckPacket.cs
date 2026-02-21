// -----------------------------------------------------------------------
// <copyright file="ConnAckPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.PacketTypes;

/// <summary>
/// Used by the broker to acknowledge a connection request from a client.
/// </summary>
public sealed class ConnAckPacket : MqttPacket
{
    public override MqttPacketType PacketType => MqttPacketType.ConnAck;

    public bool SessionPresent { get; set; }
    public ConnAckReasonCode ReasonCode { get; set; } // Enum defined below

    // MQTT 5.0 - Optional Properties

    /// <summary>MQTT 5.0: The session expiry interval in seconds. Property 0x11.</summary>
    public uint? SessionExpiryInterval { get; set; }

    /// <summary>MQTT 5.0: Client identifier assigned by the server when client sent empty ClientId. Property 0x12.</summary>
    public string? AssignedClientIdentifier { get; set; }

    /// <summary>MQTT 5.0: Keep alive value that the server requests the client use. Property 0x13.</summary>
    public ushort? ServerKeepAlive { get; set; }

    /// <summary>MQTT 5.0: Authentication method for enhanced authentication. Property 0x15.</summary>
    public string? AuthenticationMethod { get; set; }

    /// <summary>MQTT 5.0: Authentication data for enhanced authentication. Property 0x16.</summary>
    public ReadOnlyMemory<byte>? AuthenticationData { get; set; }

    /// <summary>MQTT 5.0: Information about the state of the response. Property 0x1A.</summary>
    public string? ResponseInformation { get; set; }

    /// <summary>MQTT 5.0: Reference to another server for the client to use. Property 0x1C.</summary>
    public string? ServerReference { get; set; }

    /// <summary>MQTT 5.0: Maximum number of topic aliases the server will accept. Property 0x22.</summary>
    public ushort? TopicAliasMaximum { get; set; }

    /// <summary>MQTT 5.0: Maximum QoS level the server supports. Property 0x24.</summary>
    public QualityOfService? MaximumQoS { get; set; }

    /// <summary>MQTT 5.0: Whether the server supports retained messages. Property 0x25.</summary>
    public bool? RetainAvailable { get; set; }

    /// <summary>MQTT 5.0: Whether the server supports wildcard subscriptions. Property 0x28.</summary>
    public bool? WildcardSubscriptionAvailable { get; set; }

    /// <summary>MQTT 5.0: Whether the server supports subscription identifiers. Property 0x29.</summary>
    public bool? SubscriptionIdentifiersAvailable { get; set; }

    /// <summary>MQTT 5.0: Whether the server supports shared subscriptions. Property 0x2A.</summary>
    public bool? SharedSubscriptionAvailable { get; set; }

    /// <summary>MQTT 5.0: Maximum packet size the server will accept. Property 0x27.</summary>
    public uint? MaximumPacketSize { get; set; }

    /// <summary>MQTT 5.0: Maximum number of in-flight QoS 1 and QoS 2 messages. Property 0x21.</summary>
    public ushort? ReceiveMaximum { get; set; }

    /// <summary>MQTT 5.0: Human-readable string describing the reason for the response. Property 0x1F.</summary>
    public string? ReasonString { get; set; }

    public IReadOnlyList<KeyValuePair<string, string>>? UserProperties { get; set; }

    public override string ToString()
    {
        return $"ConnAck: [SessionPresent={SessionPresent}] [ReasonCode={ReasonCode}]";
    }
}

public enum ConnAckReasonCode : byte
{
    Success = 0x00,
    UnspecifiedError = 0x80,
    MalformedPacket = 0x81,
    ProtocolError = 0x82,
    ImplementationSpecificError = 0x83,
    UnsupportedProtocolVersion = 0x84,
    ClientIdentifierNotValid = 0x85,
    BadUsernameOrPassword = 0x86,
    NotAuthorized = 0x87,
    ServerUnavailable = 0x88,
    ServerBusy = 0x89,
    Banned = 0x8A,
    BadAuthenticationMethod = 0x8C,
    TopicNameInvalid = 0x90,
    PacketTooLarge = 0x95,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99,
    RetainNotSupported = 0x9A,
    QoSNotSupported = 0x9B,
    UseAnotherServer = 0x9C,
    ServerMoved = 0x9D,
    ConnectionRateExceeded = 0x9F
}