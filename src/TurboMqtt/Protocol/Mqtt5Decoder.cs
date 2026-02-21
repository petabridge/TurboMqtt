// -----------------------------------------------------------------------
// <copyright file="Mqtt5Decoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

/// <summary>
/// Decodes MQTT 5.0 binary frames into <see cref="MqttPacket"/> instances.
/// </summary>
/// <remarks>
/// Inherits the core decode loop and most packet decode methods from <see cref="Mqtt311Decoder"/>.
/// Overrides methods for packets whose wire format changed in MQTT 5.0 (properties sections,
/// reason codes, compact ACK forms) and adds <see cref="DecodeAuth"/> for the new AUTH packet type.
/// </remarks>
public class Mqtt5Decoder : Mqtt311Decoder
{
    // ── Properties section helper ──────────────────────────────────────────

    /// <summary>
    /// Reads the Variable Byte Integer property-length prefix, then slices out the
    /// property bytes from <paramref name="buffer"/>, advancing both
    /// <paramref name="buffer"/> and <paramref name="remainingLength"/> past the section.
    /// </summary>
    /// <returns>A span over the raw property bytes (may be empty).</returns>
    private static ReadOnlySpan<byte> ConsumePropertiesSection(ref ReadOnlyMemory<byte> buffer, ref int remainingLength)
    {
        var span = buffer.Span;
        if (!TryGetPacketLength(ref span, out var propsLength))
            throw new MqttDecoderException(
                "Failed to read MQTT 5.0 properties length VBI.",
                MqttProtocolVersion.V5_0);

        // vbiBytes = number of bytes the VBI itself occupies
        var vbiBytes = buffer.Length - span.Length;
        buffer = buffer.Slice(vbiBytes);
        DecreaseRemainingLength(ref remainingLength, vbiBytes + (int)propsLength);

        var propsSlice = buffer.Span.Slice(0, (int)propsLength);
        buffer = buffer.Slice((int)propsLength);
        return propsSlice;
    }

    // ── CONNECT ─────────────────────────────────────────────────────────────

    protected override ConnectPacket DecodeConnect(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        buffer = buffer.Slice(headerLength);

        var protocolName = DecodeString(ref buffer, ref remainingLength);
        if (!protocolName.Equals("MQTT", StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(protocolName), $"Invalid protocol name: {protocolName}");

        var protocolLevel = (MqttProtocolVersion)buffer.Span[0];
        DecreaseRemainingLength(ref remainingLength, 1);
        buffer = buffer.Slice(1);

        var flags = ConnectFlags.Decode(buffer.Span[0]);
        DecreaseRemainingLength(ref remainingLength, 1);
        buffer = buffer.Slice(1);

        var packet = new ConnectPacket(protocolLevel)
        {
            Flags = flags,
            ProtocolName = protocolName
        };

        packet.KeepAliveSeconds = DecodeUnsignedShort(ref buffer, ref remainingLength);

        // MQTT 5.0: Connect Properties
        var connectProps = ConsumePropertiesSection(ref buffer, ref remainingLength);
        ReadConnectProperties(connectProps, packet);

        // Payload: Client ID (may be empty in MQTT 5.0 — broker assigns one)
        var clientId = DecodeString(ref buffer, ref remainingLength);
        packet.ClientId = clientId;

        // Payload: Will (if present)
        if (flags.WillFlag)
        {
            // MQTT 5.0: Will Properties precede Will Topic and Will Payload
            var willProps = ConsumePropertiesSection(ref buffer, ref remainingLength);
            var willTopic = DecodeString(ref buffer, ref remainingLength);
            var willMessageLength = DecodeUnsignedShort(ref buffer, ref remainingLength);
            DecreaseRemainingLength(ref remainingLength, willMessageLength);
            var will = new MqttLastWill(willTopic, buffer.Slice(0, willMessageLength).ToArray());
            buffer = buffer.Slice(willMessageLength);
            ReadWillProperties(willProps, will);
            packet.Will = will;
        }

        if (flags.UsernameFlag)
            packet.UserName = DecodeString(ref buffer, ref remainingLength);

        if (flags.PasswordFlag)
            packet.Password = DecodeString(ref buffer, ref remainingLength);

        return packet;
    }

    // ── CONNACK ─────────────────────────────────────────────────────────────

    public override ConnAckPacket DecodeConnAck(ref ReadOnlyMemory<byte> bufferForMsg, int remainingLength, int headerLength)
    {
        var packet = new ConnAckPacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);

        // Byte 1: Session Present flag (bit 0)
        packet.SessionPresent = (bufferForMsg.Span[0] & 0x01) != 0;
        bufferForMsg = bufferForMsg.Slice(1);
        DecreaseRemainingLength(ref remainingLength, 1);

        // Byte 2: Reason Code
        packet.ReasonCode = (ConnAckReasonCode)bufferForMsg.Span[0];
        bufferForMsg = bufferForMsg.Slice(1);
        DecreaseRemainingLength(ref remainingLength, 1);

        // MQTT 5.0 Properties (present when remainingLength > 0)
        if (remainingLength > 0)
        {
            var props = ConsumePropertiesSection(ref bufferForMsg, ref remainingLength);
            ReadConnAckProperties(props, packet);
        }

        return packet;
    }

    // ── PUBLISH ─────────────────────────────────────────────────────────────

    public override PublishPacket DecodePublish(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var buffSpan = buffer.Span;
        var qualityOfService = (QualityOfService)((buffSpan[0] & 0x06) >> 1);
        var duplicate = (buffSpan[0] & 0x08) == 0x08;
        var retain = (buffSpan[0] & 0x01) == 0x01;
        buffer = buffer.Slice(headerLength);

        // MQTT 5.0 §4.7.3 allows an empty topic name when a Topic Alias is used
        var topicName = DecodeString(ref buffer, ref remainingLength, 0, int.MaxValue);
        var packet = new PublishPacket(qualityOfService, duplicate, retain, topicName);

        if (qualityOfService > QualityOfService.AtMostOnce)
            DecodePacketId(ref buffer, packet, ref remainingLength);

        // MQTT 5.0: Properties
        var props = ConsumePropertiesSection(ref buffer, ref remainingLength);
        ReadPublishProperties(props, packet);

        if (remainingLength > 0)
        {
            packet.Payload = buffer; // the rest of the buffer is the payload
            DecreaseRemainingLength(ref remainingLength, buffer.Length);
        }
        else
        {
            packet.Payload = ReadOnlyMemory<byte>.Empty;
        }

        return packet;
    }

    // ── PUBACK ──────────────────────────────────────────────────────────────

    public override PubAckPacket DecodePubAck(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var packet = new PubAckPacket();
        buffer = buffer.Slice(headerLength);
        DecodePacketId(ref buffer, packet, ref remainingLength);

        // Compact form: remaining = 2 → just packet ID, reason code = Success
        if (remainingLength == 0)
        {
            packet.ReasonCode = MqttPubAckReasonCode.Success;
            return packet;
        }

        // Full form: reason code + properties
        packet.ReasonCode = (MqttPubAckReasonCode)buffer.Span[0];
        buffer = buffer.Slice(1);
        DecreaseRemainingLength(ref remainingLength, 1);

        if (remainingLength > 0)
        {
            var props = ConsumePropertiesSection(ref buffer, ref remainingLength);
            ReadAckProperties(props, out var reasonString, out var userProps);
            packet.ReasonString = reasonString;
            packet.UserProperties = userProps;
        }

        return packet;
    }

    // ── PUBREC ──────────────────────────────────────────────────────────────

    public override PubRecPacket DecodePubRec(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var packet = new PubRecPacket();
        buffer = buffer.Slice(headerLength);
        DecodePacketId(ref buffer, packet, ref remainingLength);

        if (remainingLength == 0)
        {
            packet.ReasonCode = PubRecReasonCode.Success;
            return packet;
        }

        packet.ReasonCode = (PubRecReasonCode)buffer.Span[0];
        buffer = buffer.Slice(1);
        DecreaseRemainingLength(ref remainingLength, 1);

        if (remainingLength > 0)
        {
            var props = ConsumePropertiesSection(ref buffer, ref remainingLength);
            ReadAckProperties(props, out var reasonString, out var userProps);
            packet.ReasonString = reasonString ?? string.Empty;
            packet.UserProperties = userProps;
        }

        return packet;
    }

    // ── PUBREL ──────────────────────────────────────────────────────────────

    public override PubRelPacket DecodePubRel(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        var packet = new PubRelPacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref packetSize);

        if (packetSize == 0)
        {
            packet.ReasonCode = PubRelReasonCode.Success;
            return packet;
        }

        packet.ReasonCode = (PubRelReasonCode)bufferForMsg.Span[0];
        bufferForMsg = bufferForMsg.Slice(1);
        DecreaseRemainingLength(ref packetSize, 1);

        if (packetSize > 0)
        {
            var props = ConsumePropertiesSection(ref bufferForMsg, ref packetSize);
            ReadAckProperties(props, out var reasonString, out var userProps);
            packet.ReasonString = reasonString ?? string.Empty;
            packet.UserProperties = userProps;
        }

        return packet;
    }

    // ── PUBCOMP ─────────────────────────────────────────────────────────────

    public override PubCompPacket DecodePubComp(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        var packet = new PubCompPacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref packetSize);

        if (packetSize == 0)
        {
            packet.ReasonCode = PubCompReasonCode.Success;
            return packet;
        }

        packet.ReasonCode = (PubCompReasonCode)bufferForMsg.Span[0];
        bufferForMsg = bufferForMsg.Slice(1);
        DecreaseRemainingLength(ref packetSize, 1);

        if (packetSize > 0)
        {
            var props = ConsumePropertiesSection(ref bufferForMsg, ref packetSize);
            ReadAckProperties(props, out var reasonString, out var userProps);
            packet.ReasonString = reasonString ?? string.Empty;
            packet.UserProperties = userProps;
        }

        return packet;
    }

    // ── SUBSCRIBE ───────────────────────────────────────────────────────────

    public override SubscribePacket DecodeSubscribe(ref ReadOnlyMemory<byte> bufferForMsg, int remainingLength, int headerLength)
    {
        var packet = new SubscribePacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref remainingLength);

        // MQTT 5.0: Properties before topics
        var props = ConsumePropertiesSection(ref bufferForMsg, ref remainingLength);
        ReadSubscribeProperties(props, packet);

        var topics = new List<TopicSubscription>();
        while (remainingLength > 0)
        {
            var topicFilter = DecodeString(ref bufferForMsg, ref remainingLength);
            DecreaseRemainingLength(ref remainingLength, 1);
            var options = bufferForMsg.Span[0].ToSubscriptionOptions();
            topics.Add(new TopicSubscription(topicFilter) { Options = options });
            bufferForMsg = bufferForMsg.Slice(1);
        }

        packet.Topics = topics;
        return packet;
    }

    // ── SUBACK ──────────────────────────────────────────────────────────────

    public override SubAckPacket DecodeSubAck(ref ReadOnlyMemory<byte> bufferForMsg, int remainingLength, int headerLength)
    {
        var packet = new SubAckPacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref remainingLength);

        // MQTT 5.0: Properties before reason codes
        var props = ConsumePropertiesSection(ref bufferForMsg, ref remainingLength);
        ReadSubAckProperties(props, packet);

        var reasonCodes = new List<MqttSubscribeReasonCode>();
        while (remainingLength > 0)
        {
            reasonCodes.Add((MqttSubscribeReasonCode)bufferForMsg.Span[0]);
            bufferForMsg = bufferForMsg.Slice(1);
            remainingLength--;
        }

        packet.ReasonCodes = reasonCodes;
        return packet;
    }

    // ── UNSUBSCRIBE ─────────────────────────────────────────────────────────

    public override UnsubscribePacket DecodeUnsubscribe(ref ReadOnlyMemory<byte> bufferForMsg, int remainingSize, int headerLength)
    {
        var packet = new UnsubscribePacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref remainingSize);

        // MQTT 5.0: Properties before topics
        var props = ConsumePropertiesSection(ref bufferForMsg, ref remainingSize);
        if (props.Length > 0)
        {
            Dictionary<string, string>? userProps = null;
            var propsSpan = props;
            while (propsSpan.Length > 0)
            {
                var id = propsSpan[0];
                propsSpan = propsSpan.Slice(1);
                switch (id)
                {
                    case Mqtt5PropertyIdentifiers.UserProperty:
                        var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref propsSpan);
                        userProps ??= new Dictionary<string, string>();
                        userProps[k] = v;
                        break;
                    default:
                        Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                        break;
                }
            }
            if (userProps != null)
                packet.UserProperties = userProps;
        }

        var topics = new List<string>();
        while (remainingSize > 0)
        {
            var topicFilter = DecodeString(ref bufferForMsg, ref remainingSize);
            topics.Add(topicFilter);
        }

        if (topics.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(topics),
                "Unsubscribe packet must contain at least one topic filter. [MQTT-3.10.3-2]");

        packet.Topics = topics;
        return packet;
    }

    // ── UNSUBACK ────────────────────────────────────────────────────────────

    public override UnsubAckPacket DecodeUnsubAck(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        var packet = new UnsubAckPacket();
        bufferForMsg = bufferForMsg.Slice(headerLength);
        DecodePacketId(ref bufferForMsg, packet, ref packetSize);

        // MQTT 5.0: Properties
        var props = ConsumePropertiesSection(ref bufferForMsg, ref packetSize);
        if (props.Length > 0)
        {
            Dictionary<string, string>? userProps = null;
            var propsSpan = props;
            while (propsSpan.Length > 0)
            {
                var id = propsSpan[0];
                propsSpan = propsSpan.Slice(1);
                switch (id)
                {
                    case Mqtt5PropertyIdentifiers.ReasonString:
                        packet.ReasonString = Mqtt5PropertyReader.ReadUtf8String(ref propsSpan);
                        break;
                    case Mqtt5PropertyIdentifiers.UserProperty:
                        var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref propsSpan);
                        userProps ??= new Dictionary<string, string>();
                        userProps[k] = v;
                        break;
                    default:
                        Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                        break;
                }
            }
            if (userProps != null)
                packet.UserProperties = userProps;
        }

        // Reason codes in the payload (one byte per unsubscribed topic)
        var reasonCodes = new List<MqttUnsubscribeReasonCode>();
        while (packetSize > 0)
        {
            reasonCodes.Add((MqttUnsubscribeReasonCode)bufferForMsg.Span[0]);
            bufferForMsg = bufferForMsg.Slice(1);
            packetSize--;
        }

        packet.ReasonCodes = reasonCodes;
        return packet;
    }

    // ── DISCONNECT ──────────────────────────────────────────────────────────

    protected override DisconnectPacket DecodeDisconnect(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        buffer = buffer.Slice(headerLength);

        // Compact form: Remaining Length = 0 → NormalDisconnection, no properties
        if (remainingLength == 0)
            return DisconnectPacket.Instance;

        var packet = new DisconnectPacket();
        packet.ReasonCode = (DisconnectReasonCode)buffer.Span[0];
        buffer = buffer.Slice(1);
        DecreaseRemainingLength(ref remainingLength, 1);

        if (remainingLength > 0)
        {
            var props = ConsumePropertiesSection(ref buffer, ref remainingLength);
            ReadDisconnectProperties(props, packet);
        }

        return packet;
    }

    // ── AUTH ─────────────────────────────────────────────────────────────────

    protected override MqttPacket DecodeAuth(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        bufferForMsg = bufferForMsg.Slice(headerLength);

        var reasonCode = (AuthReasonCode)bufferForMsg.Span[0];
        bufferForMsg = bufferForMsg.Slice(1);
        DecreaseRemainingLength(ref packetSize, 1);

        var props = ConsumePropertiesSection(ref bufferForMsg, ref packetSize);

        var authMethod = string.Empty;
        var authData = ReadOnlyMemory<byte>.Empty;
        string? reasonString = null;
        Dictionary<string, string>? userProps = null;

        var propsSpan = props;
        while (propsSpan.Length > 0)
        {
            var id = propsSpan[0];
            propsSpan = propsSpan.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.AuthenticationMethod:
                    authMethod = Mqtt5PropertyReader.ReadUtf8String(ref propsSpan);
                    break;
                case Mqtt5PropertyIdentifiers.AuthenticationData:
                    authData = Mqtt5PropertyReader.ReadBinaryData(ref propsSpan);
                    break;
                case Mqtt5PropertyIdentifiers.ReasonString:
                    reasonString = Mqtt5PropertyReader.ReadUtf8String(ref propsSpan);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref propsSpan);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }

        return new AuthPacket(authMethod, reasonCode)
        {
            AuthenticationData = authData,
            ReasonString = reasonString,
            UserProperties = userProps
        };
    }

    // ── Private: per-packet property readers ─────────────────────────────────

    private static void ReadConnectProperties(ReadOnlySpan<byte> props, ConnectPacket packet)
    {
        Dictionary<string, string>? userProps = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.SessionExpiryInterval:
                    packet.SessionExpiryInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ReceiveMaximum:
                    packet.ReceiveMaximum = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.MaximumPacketSize:
                    packet.MaximumPacketSize = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.TopicAliasMaximum:
                    packet.TopicAliasMaximum = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.RequestResponseInformation:
                    packet.RequestResponseInformation = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.RequestProblemInformation:
                    packet.RequestProblemInformation = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                case Mqtt5PropertyIdentifiers.AuthenticationMethod:
                    packet.AuthenticationMethod = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.AuthenticationData:
                    packet.AuthenticationData = Mqtt5PropertyReader.ReadBinaryData(ref props);
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
    }

    private static void ReadWillProperties(ReadOnlySpan<byte> props, MqttLastWill will)
    {
        Dictionary<string, string>? willProperties = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.WillDelayInterval:
                    will.DelayInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.PayloadFormatIndicator:
                    will.PayloadFormatIndicator = (PayloadFormatIndicator)Mqtt5PropertyReader.ReadByte(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.MessageExpiryInterval:
                    will.MessageExpiryInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ContentType:
                    will.ContentType = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ResponseTopic:
                    will.ResponseTopic = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.CorrelationData:
                    will.WillCorrelationData = Mqtt5PropertyReader.ReadBinaryData(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    willProperties ??= new Dictionary<string, string>();
                    willProperties[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (willProperties != null)
            will.WillProperties = willProperties;
    }

    private static void ReadConnAckProperties(ReadOnlySpan<byte> props, ConnAckPacket packet)
    {
        Dictionary<string, string>? userProps = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.SessionExpiryInterval:
                    packet.SessionExpiryInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ReceiveMaximum:
                    packet.ReceiveMaximum = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.MaximumQoS:
                    packet.MaximumQoS = (QualityOfService)Mqtt5PropertyReader.ReadByte(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.RetainAvailable:
                    packet.RetainAvailable = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.MaximumPacketSize:
                    packet.MaximumPacketSize = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.AssignedClientIdentifier:
                    packet.AssignedClientIdentifier = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.TopicAliasMaximum:
                    packet.TopicAliasMaximum = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ReasonString:
                    packet.ReasonString = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                case Mqtt5PropertyIdentifiers.WildcardSubscriptionAvailable:
                    packet.WildcardSubscriptionAvailable = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.SubscriptionIdentifiersAvailable:
                    packet.SubscriptionIdentifiersAvailable = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.SharedSubscriptionAvailable:
                    packet.SharedSubscriptionAvailable = Mqtt5PropertyReader.ReadByte(ref props) != 0;
                    break;
                case Mqtt5PropertyIdentifiers.ServerKeepAlive:
                    packet.ServerKeepAlive = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ResponseInformation:
                    packet.ResponseInformation = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ServerReference:
                    packet.ServerReference = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.AuthenticationMethod:
                    packet.AuthenticationMethod = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.AuthenticationData:
                    packet.AuthenticationData = Mqtt5PropertyReader.ReadBinaryData(ref props);
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
    }

    private static void ReadPublishProperties(ReadOnlySpan<byte> props, PublishPacket packet)
    {
        Dictionary<string, string>? userProps = null;
        List<uint>? subscriptionIdentifiers = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.PayloadFormatIndicator:
                    packet.PayloadFormatIndicator = (PayloadFormatIndicator)Mqtt5PropertyReader.ReadByte(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.MessageExpiryInterval:
                    packet.MessageExpiryInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.TopicAlias:
                    packet.TopicAlias = Mqtt5PropertyReader.ReadTwoByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ResponseTopic:
                    packet.ResponseTopic = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.CorrelationData:
                    packet.CorrelationData = Mqtt5PropertyReader.ReadBinaryData(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                case Mqtt5PropertyIdentifiers.SubscriptionIdentifier:
                    if (!Mqtt5PropertyReader.TryReadVariableByteInt(ref props, out var sid))
                        throw new MqttDecoderException(
                            "Failed to read Subscription Identifier VBI in PUBLISH properties.",
                            MqttProtocolVersion.V5_0);
                    subscriptionIdentifiers ??= new List<uint>();
                    subscriptionIdentifiers.Add(sid);
                    break;
                case Mqtt5PropertyIdentifiers.ContentType:
                    packet.ContentType = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
        if (subscriptionIdentifiers != null)
            packet.SubscriptionIdentifiers = subscriptionIdentifiers;
    }

    /// <summary>
    /// Reads ReasonString and UserProperty from an ACK packet properties section
    /// (applies to PUBACK, PUBREC, PUBREL, PUBCOMP).
    /// </summary>
    private static void ReadAckProperties(ReadOnlySpan<byte> props, out string? reasonString, out IReadOnlyDictionary<string, string>? userProps)
    {
        reasonString = null;
        Dictionary<string, string>? dict = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.ReasonString:
                    reasonString = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    dict ??= new Dictionary<string, string>();
                    dict[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        userProps = dict;
    }

    private static void ReadSubscribeProperties(ReadOnlySpan<byte> props, SubscribePacket packet)
    {
        Dictionary<string, string>? userProps = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.SubscriptionIdentifier:
                    if (!Mqtt5PropertyReader.TryReadVariableByteInt(ref props, out var sid))
                        throw new MqttDecoderException(
                            "Failed to read Subscription Identifier VBI in SUBSCRIBE properties.",
                            MqttProtocolVersion.V5_0);
                    packet.SubscriptionIdentifier = sid;
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
    }

    private static void ReadSubAckProperties(ReadOnlySpan<byte> props, SubAckPacket packet)
    {
        Dictionary<string, string>? userProps = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.ReasonString:
                    packet.ReasonString = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
    }

    private static void ReadDisconnectProperties(ReadOnlySpan<byte> props, DisconnectPacket packet)
    {
        Dictionary<string, string>? userProps = null;
        while (props.Length > 0)
        {
            var id = props[0];
            props = props.Slice(1);
            switch (id)
            {
                case Mqtt5PropertyIdentifiers.SessionExpiryInterval:
                    packet.SessionExpiryInterval = Mqtt5PropertyReader.ReadFourByteInt(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ReasonString:
                    packet.ReasonString = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.ServerReference:
                    packet.ServerReference = Mqtt5PropertyReader.ReadUtf8String(ref props);
                    break;
                case Mqtt5PropertyIdentifiers.UserProperty:
                    var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref props);
                    userProps ??= new Dictionary<string, string>();
                    userProps[k] = v;
                    break;
                default:
                    Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(id);
                    break;
            }
        }
        if (userProps != null)
            packet.UserProperties = userProps;
    }
}
