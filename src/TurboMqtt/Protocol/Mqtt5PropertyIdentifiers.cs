// -----------------------------------------------------------------------
// <copyright file="Mqtt5PropertyIdentifiers.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Protocol;

/// <summary>
/// Constants for all MQTT 5.0 property identifiers as defined in OASIS MQTT 5.0 spec Table 2-4.
/// </summary>
internal static class Mqtt5PropertyIdentifiers
{
    // ── Byte properties ────────────────────────────────────────────────────

    /// <summary>0x01 — Payload Format Indicator (Byte). Appears in: PUBLISH, Will Properties.</summary>
    public const byte PayloadFormatIndicator = 0x01;

    /// <summary>0x17 — Request Problem Information (Byte). Appears in: CONNECT.</summary>
    public const byte RequestProblemInformation = 0x17;

    /// <summary>0x19 — Request Response Information (Byte). Appears in: CONNECT.</summary>
    public const byte RequestResponseInformation = 0x19;

    /// <summary>0x24 — Maximum QoS (Byte). Appears in: CONNACK.</summary>
    public const byte MaximumQoS = 0x24;

    /// <summary>0x25 — Retain Available (Byte). Appears in: CONNACK.</summary>
    public const byte RetainAvailable = 0x25;

    /// <summary>0x28 — Wildcard Subscription Available (Byte). Appears in: CONNACK.</summary>
    public const byte WildcardSubscriptionAvailable = 0x28;

    /// <summary>0x29 — Subscription Identifiers Available (Byte). Appears in: CONNACK.</summary>
    public const byte SubscriptionIdentifiersAvailable = 0x29;

    /// <summary>0x2A — Shared Subscription Available (Byte). Appears in: CONNACK.</summary>
    public const byte SharedSubscriptionAvailable = 0x2A;

    // ── Two Byte Integer properties ────────────────────────────────────────

    /// <summary>0x13 — Server Keep Alive (Two Byte Integer). Appears in: CONNACK.</summary>
    public const byte ServerKeepAlive = 0x13;

    /// <summary>0x21 — Receive Maximum (Two Byte Integer). Appears in: CONNECT, CONNACK.</summary>
    public const byte ReceiveMaximum = 0x21;

    /// <summary>0x22 — Topic Alias Maximum (Two Byte Integer). Appears in: CONNECT, CONNACK.</summary>
    public const byte TopicAliasMaximum = 0x22;

    /// <summary>0x23 — Topic Alias (Two Byte Integer). Appears in: PUBLISH.</summary>
    public const byte TopicAlias = 0x23;

    // ── Four Byte Integer properties ───────────────────────────────────────

    /// <summary>0x02 — Message Expiry Interval (Four Byte Integer). Appears in: PUBLISH, Will Properties.</summary>
    public const byte MessageExpiryInterval = 0x02;

    /// <summary>0x11 — Session Expiry Interval (Four Byte Integer). Appears in: CONNECT, CONNACK, DISCONNECT.</summary>
    public const byte SessionExpiryInterval = 0x11;

    /// <summary>0x18 — Will Delay Interval (Four Byte Integer). Appears in: Will Properties.</summary>
    public const byte WillDelayInterval = 0x18;

    /// <summary>0x27 — Maximum Packet Size (Four Byte Integer). Appears in: CONNECT, CONNACK.</summary>
    public const byte MaximumPacketSize = 0x27;

    // ── Variable Byte Integer properties ──────────────────────────────────

    /// <summary>0x0B — Subscription Identifier (Variable Byte Integer). Appears in: SUBSCRIBE, PUBLISH.</summary>
    public const byte SubscriptionIdentifier = 0x0B;

    // ── UTF-8 Encoded String properties ───────────────────────────────────

    /// <summary>0x03 — Content Type (UTF-8 Encoded String). Appears in: PUBLISH, Will Properties.</summary>
    public const byte ContentType = 0x03;

    /// <summary>0x08 — Response Topic (UTF-8 Encoded String). Appears in: PUBLISH, Will Properties.</summary>
    public const byte ResponseTopic = 0x08;

    /// <summary>0x12 — Assigned Client Identifier (UTF-8 Encoded String). Appears in: CONNACK.</summary>
    public const byte AssignedClientIdentifier = 0x12;

    /// <summary>0x15 — Authentication Method (UTF-8 Encoded String). Appears in: CONNECT, CONNACK, AUTH.</summary>
    public const byte AuthenticationMethod = 0x15;

    /// <summary>0x1A — Response Information (UTF-8 Encoded String). Appears in: CONNACK.</summary>
    public const byte ResponseInformation = 0x1A;

    /// <summary>0x1C — Server Reference (UTF-8 Encoded String). Appears in: CONNACK, DISCONNECT.</summary>
    public const byte ServerReference = 0x1C;

    /// <summary>0x1F — Reason String (UTF-8 Encoded String). Appears in: CONNACK, PUBACK, PUBREC, PUBREL, PUBCOMP, SUBACK, UNSUBACK, DISCONNECT, AUTH.</summary>
    public const byte ReasonString = 0x1F;

    // ── Binary Data properties ─────────────────────────────────────────────

    /// <summary>0x09 — Correlation Data (Binary Data). Appears in: PUBLISH, Will Properties.</summary>
    public const byte CorrelationData = 0x09;

    /// <summary>0x16 — Authentication Data (Binary Data). Appears in: CONNECT, CONNACK, AUTH.</summary>
    public const byte AuthenticationData = 0x16;

    // ── UTF-8 String Pair properties ───────────────────────────────────────

    /// <summary>0x26 — User Property (UTF-8 String Pair). Appears in: all packet types that support it. May appear multiple times.</summary>
    public const byte UserProperty = 0x26;
}
