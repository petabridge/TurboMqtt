// -----------------------------------------------------------------------
// <copyright file="Mqtt5PacketGenerators.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests;

/// <summary>
/// FsCheck generators for all 15 MQTT 5.0 packet types.
/// Randomizes V5-specific fields: reason codes, user properties, session expiry,
/// receive maximum, subscription identifiers, topic aliases, etc.
/// </summary>
public class Mqtt5PacketGenerators
{
    // ── String helpers ───────────────────────────────────────────────────────

    /// <summary>Valid MQTT UTF-8 string: non-null, no null characters, any length.</summary>
    private static readonly Gen<string> ValidMqtt5String =
        Arb.Generate<string>().Where(s => s != null && !s.Contains('\0'));

    /// <summary>Valid MQTT UTF-8 string: non-null, non-empty, no null characters.</summary>
    private static readonly Gen<string> ValidMqtt5StringNonEmpty =
        Arb.Generate<string>().Where(s => !string.IsNullOrEmpty(s) && !s.Contains('\0'));

    /// <summary>
    /// Generates a small random byte array (0 to <paramref name="maxLen"/> bytes).
    /// </summary>
    private static Gen<byte[]> BytesGen(int maxLen = 32) =>
        from len in Gen.Choose(0, maxLen)
        from bytes in Gen.ArrayOf(len, Arb.Generate<byte>())
        select bytes;

    /// <summary>
    /// Helper for "optional string" — generates null or a non-empty valid string.
    /// </summary>
    private static Gen<string?> OptionalString() =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            ValidMqtt5StringNonEmpty.Select(s => (string?)s));

    /// <summary>Helper for "optional uint" — generates null or a random uint.</summary>
    private static Gen<uint?> OptionalUInt32() =>
        Gen.OneOf(
            Gen.Constant<uint?>(null),
            Arb.Generate<uint>().Select(v => (uint?)v));

    /// <summary>Helper for "optional ushort" — generates null or a random ushort.</summary>
    private static Gen<ushort?> OptionalUInt16() =>
        Gen.OneOf(
            Gen.Constant<ushort?>(null),
            Arb.Generate<ushort>().Select(v => (ushort?)v));

    /// <summary>Helper for "optional bool" — generates null or a random bool.</summary>
    private static Gen<bool?> OptionalBool() =>
        Gen.OneOf(
            Gen.Constant<bool?>(null),
            Arb.Generate<bool>().Select(v => (bool?)v));

    /// <summary>
    /// Generates either <c>null</c> (no user properties) or a non-empty list
    /// of 1–4 key-value pairs valid for MQTT 5.0 User Properties.
    /// Duplicate keys are allowed per MQTT 5.0 spec §3.1.2.11.8.
    /// </summary>
    private static Gen<IReadOnlyList<KeyValuePair<string, string>>?> UserPropertiesGen()
    {
        var pairGen =
            from key in ValidMqtt5StringNonEmpty
            from value in ValidMqtt5String
            select new KeyValuePair<string, string>(key, value);

        var withProps =
            from count in Gen.Choose(1, 4)
            from pairs in Gen.ListOf(count, pairGen)
            select (IReadOnlyList<KeyValuePair<string, string>>?)pairs.ToList();

        return Gen.OneOf(Gen.Constant((IReadOnlyList<KeyValuePair<string, string>>?)null), withProps);
    }

    // ── CONNECT ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 CONNECT packets with randomized V5-specific properties
    /// (session expiry, receive maximum, topic alias maximum, auth method/data, user properties).
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5ConnectPacketArb()
    {
        var validWillTopic = Arb.Generate<string>()
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                        && MqttTopicValidator.ValidatePublishTopic(s).IsValid);

        var willPayloadGen =
            from length in Gen.Choose(0, 64)
            from bytes in Gen.ArrayOf(length, Arb.Generate<byte>())
            select new ReadOnlyMemory<byte>(bytes);

        var validCredential = Arb.Generate<string>()
            .Where(s => s != null && s.Length > 0 && !s.Contains('\0'));

        var authDataGen =
            from bytes in BytesGen(32)
            select bytes.Length > 0 ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(bytes) : null;

        return (
            from clientId in Arb.Generate<string>()
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                            && MqttClientIdValidator.ValidateClientId(s).IsValid)
            from cleanSession in Arb.Generate<bool>()
            from keepAlive in Arb.Generate<ushort>()
            from sessionExpiry in Arb.Generate<uint>()
            from receiveMax in Arb.Generate<ushort>()
            from maxPktSize in Arb.Generate<uint>()
            from topicAliasMax in Arb.Generate<ushort>()
            from requestResponse in Arb.Generate<bool>()
            from requestProblem in Arb.Generate<bool>()
            from hasAuth in Arb.Generate<bool>()
            from authMethod in hasAuth ? ValidMqtt5StringNonEmpty : Gen.Constant(string.Empty)
            from authData in hasAuth ? authDataGen : Gen.Constant<ReadOnlyMemory<byte>?>(null)
            from userProps in UserPropertiesGen()
            from hasWill in Arb.Generate<bool>()
            from willTopic in hasWill ? validWillTopic : Gen.Constant(string.Empty)
            from willPayload in hasWill ? willPayloadGen : Gen.Constant(ReadOnlyMemory<byte>.Empty)
            from willQos in hasWill ? Arb.Generate<QualityOfService>() : Gen.Constant(QualityOfService.AtMostOnce)
            from willRetain in hasWill ? Arb.Generate<bool>() : Gen.Constant(false)
            from willDelayInterval in hasWill ? Arb.Generate<uint>() : Gen.Constant(0u)
            from hasUsername in Arb.Generate<bool>()
            from username in hasUsername ? validCredential : Gen.Constant(string.Empty)
            from hasPassword in hasUsername ? Arb.Generate<bool>() : Gen.Constant(false)
            from password in hasPassword ? validCredential : Gen.Constant(string.Empty)
            select (MqttPacket)new ConnectPacket(MqttProtocolVersion.V5_0)
            {
                ClientId = clientId,
                SessionExpiryInterval = sessionExpiry,
                ReceiveMaximum = receiveMax,
                MaximumPacketSize = maxPktSize,
                TopicAliasMaximum = topicAliasMax,
                RequestResponseInformation = requestResponse,
                RequestProblemInformation = requestProblem,
                AuthenticationMethod = hasAuth ? authMethod : null,
                AuthenticationData = authData,
                UserProperties = userProps,
                KeepAliveSeconds = keepAlive,
                Will = hasWill ? new MqttLastWill(willTopic, willPayload) { DelayInterval = willDelayInterval } : null,
                UserName = hasUsername ? username : null,
                Password = hasPassword ? password : null,
                Flags = new ConnectFlags
                {
                    CleanSession = cleanSession,
                    WillFlag = hasWill,
                    WillQoS = hasWill ? willQos : QualityOfService.AtMostOnce,
                    WillRetain = hasWill && willRetain,
                    UsernameFlag = hasUsername,
                    PasswordFlag = hasPassword
                }
            }
        ).ToArbitrary();
    }

    // ── CONNACK ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 CONNACK packets with randomized V5 reason codes
    /// and optional properties (session expiry, assigned client ID, server keep alive, etc.).
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5ConnAckPacketArb()
    {
        var v5ReasonCodes = Enum.GetValues<ConnAckReasonCode>();

        var authDataGen =
            from bytes in BytesGen(32)
            select bytes.Length > 0 ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(bytes) : null;

        // Optional ReadOnlyMemory<byte>? — null or non-empty bytes
        var optAuthData = Gen.OneOf(
            Gen.Constant<ReadOnlyMemory<byte>?>(null),
            authDataGen.Where(x => x != null));

        // Optional QualityOfService?
        var optMaxQos = Gen.OneOf(
            Gen.Constant<QualityOfService?>(null),
            Gen.Elements(
                    QualityOfService.AtMostOnce,
                    QualityOfService.AtLeastOnce,
                    QualityOfService.ExactlyOnce)
                .Select(q => (QualityOfService?)q));

        return (
            from reasonCode in Gen.Elements(v5ReasonCodes)
            from sessionPresent in reasonCode == ConnAckReasonCode.Success
                ? Arb.Generate<bool>()
                : Gen.Constant(false)
            from sessionExpiry in OptionalUInt32()
            from assignedClientId in OptionalString()
            from serverKeepAlive in OptionalUInt16()
            from authMethod in OptionalString()
            from authData in optAuthData
            from responseInfo in OptionalString()
            from serverRef in OptionalString()
            from topicAliasMax in OptionalUInt16()
            from maxQos in optMaxQos
            from retainAvail in OptionalBool()
            from wildcardSubAvail in OptionalBool()
            from subIdsAvail in OptionalBool()
            from sharedSubAvail in OptionalBool()
            from maxPktSize in OptionalUInt32()
            from receiveMax in OptionalUInt16()
            from reasonString in OptionalString()
            from userProps in UserPropertiesGen()
            select (MqttPacket)new ConnAckPacket
            {
                SessionPresent = sessionPresent,
                ReasonCode = reasonCode,
                SessionExpiryInterval = sessionExpiry,
                AssignedClientIdentifier = assignedClientId,
                ServerKeepAlive = serverKeepAlive,
                AuthenticationMethod = authMethod,
                AuthenticationData = authData,
                ResponseInformation = responseInfo,
                ServerReference = serverRef,
                TopicAliasMaximum = topicAliasMax,
                MaximumQoS = maxQos,
                RetainAvailable = retainAvail,
                WildcardSubscriptionAvailable = wildcardSubAvail,
                SubscriptionIdentifiersAvailable = subIdsAvail,
                SharedSubscriptionAvailable = sharedSubAvail,
                MaximumPacketSize = maxPktSize,
                ReceiveMaximum = receiveMax,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── PUBLISH ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 PUBLISH packets with randomized V5 properties
    /// (topic alias, message expiry, response topic, correlation data, user properties,
    /// subscription identifiers, content type, payload format indicator).
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PublishPacketArb()
    {
        var correlationDataGen =
            from bytes in BytesGen(32)
            select bytes.Length > 0 ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(bytes) : null;

        var subscriptionIdentifiersGen = Gen.OneOf(
            Gen.Constant<IReadOnlyList<uint>?>(null),
            from count in Gen.Choose(1, 3)
            // VBI range: 1 to 268435455
            from ids in Gen.ArrayOf(count, Gen.Choose(1, 268435455).Select(i => (uint)i))
            select (IReadOnlyList<uint>?)ids);

        return (
            from qos in Arb.Generate<QualityOfService>()
            from duplicate in Arb.Generate<bool>()
            from retainRequested in Arb.Generate<bool>()
            from topicName in Arb.Generate<string>()
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                            && MqttTopicValidator.ValidatePublishTopic(s).IsValid)
            from payloadLength in Gen.Choose(0, 4096)
            from payloadBytes in Gen.ArrayOf(payloadLength, Arb.Generate<byte>())
            from packetId in Gen.Choose(1, 65535)
            from payloadFmt in Gen.Elements(
                PayloadFormatIndicator.Unspecified,
                PayloadFormatIndicator.Utf8Encoded)
            from msgExpiry in Arb.Generate<uint>()
            from topicAlias in Gen.Choose(0, 1000).Select(v => (ushort)v)
            from responseTopic in OptionalString()
            from correlationData in correlationDataGen
            from userProps in UserPropertiesGen()
            from subscriptionIds in subscriptionIdentifiersGen
            from contentType in OptionalString()
            select (MqttPacket)new PublishPacket(qos, duplicate, retainRequested, topicName)
            {
                Payload = new ReadOnlyMemory<byte>(payloadBytes),
                PacketId = (NonZeroUInt16)(ushort)packetId,
                PayloadFormatIndicator = payloadFmt,
                MessageExpiryInterval = msgExpiry,
                TopicAlias = topicAlias,
                ResponseTopic = responseTopic,
                CorrelationData = correlationData,
                UserProperties = userProps,
                SubscriptionIdentifiers = subscriptionIds,
                ContentType = contentType
            }
        ).ToArbitrary();
    }

    // ── PUBACK ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 PUBACK packets with randomized reason codes and optional properties.
    /// Generates both compact form (Success, no props) and full form.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PubAckPacketArb()
    {
        var reasonStringGen = OptionalString();

        return (
            from packetId in Gen.Choose(1, 65535)
            from reasonCode in Gen.Elements(Enum.GetValues<MqttPubAckReasonCode>())
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new PubAckPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCode = reasonCode,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── PUBREC ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 PUBREC packets with randomized reason codes and optional properties.
    /// ReasonCode is always non-null to avoid encoder/decoder null→Success conversion ambiguity.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PubRecPacketArb()
    {
        // ReasonString is non-nullable string (default ""); generate empty or non-empty.
        var reasonStringGen = Gen.OneOf(
            Gen.Constant(string.Empty),
            ValidMqtt5StringNonEmpty);

        return (
            from packetId in Gen.Choose(1, 65535)
            from reasonCode in Gen.Elements(Enum.GetValues<PubRecReasonCode>())
                .Select(rc => (PubRecReasonCode?)rc)
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new PubRecPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCode = reasonCode,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── PUBREL ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 PUBREL packets with randomized reason codes and optional properties.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PubRelPacketArb()
    {
        var reasonStringGen = Gen.OneOf(
            Gen.Constant(string.Empty),
            ValidMqtt5StringNonEmpty);

        return (
            from packetId in Gen.Choose(1, 65535)
            from reasonCode in Gen.Elements(Enum.GetValues<PubRelReasonCode>())
                .Select(rc => (PubRelReasonCode?)rc)
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new PubRelPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCode = reasonCode,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── PUBCOMP ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 PUBCOMP packets with randomized reason codes and optional properties.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PubCompPacketArb()
    {
        var reasonStringGen = Gen.OneOf(
            Gen.Constant(string.Empty),
            ValidMqtt5StringNonEmpty);

        return (
            from packetId in Gen.Choose(1, 65535)
            from reasonCode in Gen.Elements(Enum.GetValues<PubCompReasonCode>())
                .Select(rc => (PubCompReasonCode?)rc)
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new PubCompPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCode = reasonCode,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── SUBSCRIBE ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 SUBSCRIBE packets with full V5 subscription options
    /// (NoLocal, RetainAsPublished, RetainHandling), optional subscription identifier,
    /// and optional user properties.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5SubscribePacketArb()
    {
        var validSubscribeTopic = Arb.Generate<string>()
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                        && MqttTopicValidator.ValidateSubscribeTopic(s).IsValid);

        var subscriptionOptionsGen =
            from qos in Arb.Generate<QualityOfService>()
            from noLocal in Arb.Generate<bool>()
            from retainAsPublished in Arb.Generate<bool>()
            from retainHandling in Gen.Elements(
                RetainHandlingOption.SendAtSubscribe,
                RetainHandlingOption.SendAtSubscribeIfNew,
                RetainHandlingOption.DoNotSendAtSubscribe)
            select new SubscriptionOptions
            {
                QoS = qos,
                NoLocal = noLocal,
                RetainAsPublished = retainAsPublished,
                RetainHandling = retainHandling
            };

        var topicSubscriptionGen =
            from topic in validSubscribeTopic
            from options in subscriptionOptionsGen
            select new TopicSubscription(topic) { Options = options };

        var subscriptionIdGen = Gen.OneOf(
            Gen.Constant<uint?>(null),
            Gen.Choose(1, 268435455).Select(v => (uint?)v));

        return (
            from packetId in Gen.Choose(1, 65535)
            from topicCount in Gen.Choose(1, 5)
            from topics in Gen.ArrayOf(topicCount, topicSubscriptionGen)
            from subscriptionId in subscriptionIdGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new SubscribePacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                Topics = topics,
                SubscriptionIdentifier = subscriptionId,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── SUBACK ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 SUBACK packets including all V5-specific reason codes,
    /// optional reason string, and optional user properties.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5SubAckPacketArb()
    {
        var allReasonCodes = Enum.GetValues<MqttSubscribeReasonCode>();
        var reasonStringGen = OptionalString();

        return (
            from packetId in Gen.Choose(1, 65535)
            from codeCount in Gen.Choose(1, 5)
            from codes in Gen.ArrayOf(codeCount, Gen.Elements(allReasonCodes))
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new SubAckPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCodes = codes,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── UNSUBSCRIBE ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 UNSUBSCRIBE packets with 1–5 topic filters and optional user properties.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5UnsubscribePacketArb()
    {
        var validSubscribeTopic = Arb.Generate<string>()
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                        && MqttTopicValidator.ValidateSubscribeTopic(s).IsValid);

        return (
            from packetId in Gen.Choose(1, 65535)
            from topicCount in Gen.Choose(1, 5)
            from topics in Gen.ArrayOf(topicCount, validSubscribeTopic)
            from userProps in UserPropertiesGen()
            select (MqttPacket)new UnsubscribePacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                Topics = topics,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── UNSUBACK ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 UNSUBACK packets with 1–5 reason codes, optional reason string,
    /// and optional user properties. UNSUBACK is a new V5 feature (MQTT 3.1.1 UNSUBACK had no payload).
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5UnsubAckPacketArb()
    {
        var allReasonCodes = Enum.GetValues<MqttUnsubscribeReasonCode>();
        // ReasonString is non-nullable (default ""); generate empty or non-empty strings.
        var reasonStringGen = Gen.OneOf(
            Gen.Constant(string.Empty),
            ValidMqtt5StringNonEmpty);

        return (
            from packetId in Gen.Choose(1, 65535)
            from codeCount in Gen.Choose(1, 5)
            from codes in Gen.ArrayOf(codeCount, Gen.Elements(allReasonCodes))
            from reasonString in reasonStringGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new UnsubAckPacket
            {
                PacketId = (NonZeroUInt16)(ushort)packetId,
                ReasonCodes = codes,
                ReasonString = reasonString,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── PINGREQ / PINGRESP ──────────────────────────────────────────────────

    public static Arbitrary<MqttPacket> Mqtt5PingReqPacketArb()
        => Gen.Constant((MqttPacket)PingReqPacket.Instance).ToArbitrary();

    public static Arbitrary<MqttPacket> Mqtt5PingRespPacketArb()
        => Gen.Constant((MqttPacket)PingRespPacket.Instance).ToArbitrary();

    // ── DISCONNECT ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 DISCONNECT packets.
    /// Generates both compact form (null ReasonCode, no properties) and
    /// full form (non-NormalDisconnection reason codes with optional properties).
    /// <para>
    /// NormalDisconnection with no properties is excluded from the non-compact generator
    /// because the encoder converts it to compact form (Remaining Length = 0),
    /// and the decoder returns a packet with ReasonCode = null — causing a mismatch.
    /// </para>
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5DisconnectPacketArb()
    {
        // Exclude NormalDisconnection to avoid the compact-form ambiguity:
        // NormalDisconnection + no props → compact → decoded ReasonCode = null ≠ NormalDisconnection.
        var nonNormalCodes = Enum.GetValues<DisconnectReasonCode>()
            .Where(rc => rc != DisconnectReasonCode.NormalDisconnection)
            .ToArray();

        var sessionExpiryGen = OptionalUInt32();
        var serverRefGen = OptionalString();

        return Gen.OneOf(
            // Compact form: null ReasonCode, no properties
            Gen.Constant((MqttPacket)new DisconnectPacket()),
            // Non-compact form: non-NormalDisconnection reason code with optional V5 properties
            (from reasonCode in Gen.Elements(nonNormalCodes)
             from sessionExpiry in sessionExpiryGen
             from serverRef in serverRefGen
             from userProps in UserPropertiesGen()
             select (MqttPacket)new DisconnectPacket
             {
                 ReasonCode = reasonCode,
                 SessionExpiryInterval = sessionExpiry,
                 ServerReference = serverRef,
                 UserProperties = userProps
             }).ToArbitrary().Generator
        ).ToArbitrary();
    }

    // ── AUTH ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates MQTT 5.0 AUTH packets (new packet type not present in MQTT 3.1.1).
    /// Randomizes: authentication method, reason code, authentication data, user properties.
    /// The reason string defaults to the reason code's string representation (from constructor).
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5AuthPacketArb()
    {
        var authDataGen =
            from bytes in BytesGen(32)
            select new ReadOnlyMemory<byte>(bytes);

        return (
            from authMethod in ValidMqtt5StringNonEmpty
            from reasonCode in Gen.Elements(
                AuthReasonCode.Success,
                AuthReasonCode.ContinueAuthentication,
                AuthReasonCode.ReAuthenticate)
            from authData in authDataGen
            from userProps in UserPropertiesGen()
            select (MqttPacket)new AuthPacket(authMethod, reasonCode)
            {
                AuthenticationData = authData,
                UserProperties = userProps
            }
        ).ToArbitrary();
    }

    // ── Combined ────────────────────────────────────────────────────────────

    /// <summary>
    /// Combined generator using all 15 MQTT 5.0 packet types via <see cref="Gen.OneOf"/>.
    /// Used for combined roundtrip and error path property tests.
    /// </summary>
    public static Arbitrary<MqttPacket> Mqtt5PacketArb()
    {
        return Gen.OneOf(
            Mqtt5ConnectPacketArb().Generator,
            Mqtt5ConnAckPacketArb().Generator,
            Mqtt5PublishPacketArb().Generator,
            Mqtt5PubAckPacketArb().Generator,
            Mqtt5PubRecPacketArb().Generator,
            Mqtt5PubRelPacketArb().Generator,
            Mqtt5PubCompPacketArb().Generator,
            Mqtt5SubscribePacketArb().Generator,
            Mqtt5SubAckPacketArb().Generator,
            Mqtt5UnsubscribePacketArb().Generator,
            Mqtt5UnsubAckPacketArb().Generator,
            Mqtt5PingReqPacketArb().Generator,
            Mqtt5PingRespPacketArb().Generator,
            Mqtt5DisconnectPacketArb().Generator,
            Mqtt5AuthPacketArb().Generator
        ).ToArbitrary();
    }
}
