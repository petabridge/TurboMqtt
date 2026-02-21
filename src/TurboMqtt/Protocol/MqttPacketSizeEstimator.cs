// -----------------------------------------------------------------------
// <copyright file="MqttEncoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

internal static class MqttPacketSizeEstimator
{
    internal const int PacketIdLength = 2;
    internal const int StringSizeLength = 2;
    internal const int MaxVariableLength = 4;
    internal const string Mqtt5ProtocolName = "MQTT";
    internal const string Mqtt311ProtocolName = "MQTT";
    internal const string Mqtt31ProtocolName = "MQIsdp"; // probably not going to support this version

    /// <summary>
    /// Estimates the size of the packet WITHOUT the length header.
    /// </summary>
    /// <param name="packet">The packet to estimate.</param>
    /// <param name="protocolVersion">The version of the MQTT protocol we're encoding the packet for.</param>
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketLengthHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized MQTT protocol version is supplied.</exception>
    public static PacketSize EstimatePacketSize(MqttPacket packet,
        MqttProtocolVersion protocolVersion)
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
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketLengthHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized packet type is supplied.</exception>
    public static PacketSize EstimateMqtt3PacketSize(MqttPacket packet)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Connect:
                return new PacketSize(EstimateConnectPacketSizeMqtt311((ConnectPacket)packet));
            case MqttPacketType.ConnAck:
                return new PacketSize(2); //1 byte for session present + 1 byte for reason code
            case MqttPacketType.Publish:
                return new PacketSize(EstimatePublishPacketSizeMqtt311((PublishPacket)packet));
            case MqttPacketType.PubAck:
            case MqttPacketType.PubRec:
            case MqttPacketType.PubRel:
            case MqttPacketType.PubComp:
            case MqttPacketType.UnsubAck:
                return new PacketSize(PacketIdLength); // packet id only
            case MqttPacketType.SubAck:
                return new PacketSize(EstimateSubAckPacketSizeMqtt311((SubAckPacket)packet));
            case MqttPacketType.Subscribe:
                return new PacketSize(EstimateSubscribePacketSizeMqtt311((SubscribePacket)packet));
            case MqttPacketType.Unsubscribe:
                return new PacketSize(EstimateUnsubscribePacketSizeMqtt311((UnsubscribePacket)packet));
            case MqttPacketType.PingReq:
            case MqttPacketType.PingResp:
            case MqttPacketType.Disconnect:
                return PacketSize.NoContent; // fixed header only
            case MqttPacketType.Auth
                : // this should throw for AUTH packets in MQTT3, which are not supported (MQTT5 and up only)
            default:
                throw new ArgumentOutOfRangeException(nameof(packet), packet.PacketType, null);
        }
    }

    private static int EstimateSubAckPacketSizeMqtt311(SubAckPacket packet)
    {
        var size = 0; // fixed header not included in length calculation
        // packet id
        size += PacketIdLength;

        foreach (var reasonCode in packet.ReasonCodes)
        {
            size += 1; // Reason code
        }

        return size;
    }

    private static int EstimateUnsubscribePacketSizeMqtt311(UnsubscribePacket packet)
    {
        var size = 0; // fixed header not included in length calculation


        // packet id
        size += PacketIdLength;

        foreach (var topic in packet.Topics)
        {
            size += 2 + Encoding.UTF8.GetByteCount(topic); // Topic name
        }

        return size;
    }

    private static int EstimateSubscribePacketSizeMqtt311(SubscribePacket packet)
    {
        var size = 0; // fixed header not included in length calculation


        // packet id
        size += PacketIdLength;

        foreach (var topic in packet.Topics)
        {
            size += 2 + Encoding.UTF8.GetByteCount(topic.Topic); // Topic name
            size += 1; // Settings
        }

        return size;
    }

    private static int EstimatePublishPacketSizeMqtt311(PublishPacket packet)
    {
        var size = 0; // fixed header not included in length calculation

        /*
        +-------------------+-------------------+-------------------+
        | Topic Name        | Packet Identifier | Payload           |
        | X Bytes           | 2 Bytes           | X Bytes           |
        +-------------------+-------------------+-------------------+
        */

        size += StringSizeLength + Encoding.UTF8.GetByteCount(packet.TopicName); // Topic Name

        // Start calculating the properties size
        var propertiesSize = 0;

        if (packet.QualityOfService > QualityOfService.AtMostOnce)
        {
            propertiesSize += PacketIdLength; // Packet Identifier
        }

        return size + propertiesSize + packet.Payload.Length;
    }

    /// <summary>
    /// Estimates the size of the packet WITHOUT the length header.
    /// </summary>
    /// <param name="packet">The packet to estimate.</param>
    /// <returns>The length of the packet NOT INCLUDING the length header, which gets calculated separately via <see cref="GetPacketLengthHeaderSize"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an recognized packet type is supplied.</exception>
    /// <remarks>
    /// MQTT5 includes many additional properties aimed at making debuggability easier, but they also increase the size of the packet.
    /// </remarks>
    public static PacketSize EstimateMqtt5PacketSize(MqttPacket packet)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Connect:
                return new PacketSize(EstimateConnectPacketSizeMqtt5((ConnectPacket)packet));
            case MqttPacketType.ConnAck:
                return new PacketSize(EstimateConnAckPacketSizeMqtt5((ConnAckPacket)packet));
            case MqttPacketType.Publish:
                return new PacketSize(EstimatePublishPacketSizeMqtt5((PublishPacket)packet));
            case MqttPacketType.PubAck:
                return new PacketSize(EstimatePubAckPacketSizeMqtt5((PubAckPacket)packet));
            case MqttPacketType.PubRec:
                return new PacketSize(EstimatePubRecPacketSizeMqtt5((PubRecPacket)packet));
            case MqttPacketType.PubRel:
                return new PacketSize(EstimatePubRelPacketSizeMqtt5((PubRelPacket)packet));
            case MqttPacketType.PubComp:
                return new PacketSize(EstimatePubCompPacketSizeMqtt5((PubCompPacket)packet));
            case MqttPacketType.Subscribe:
                return new PacketSize(EstimateSubscribePacketSizeMqtt5((SubscribePacket)packet));
            case MqttPacketType.SubAck:
                return new PacketSize(EstimateSubAckPacketSizeMqtt5((SubAckPacket)packet));
            case MqttPacketType.Unsubscribe:
                return new PacketSize(EstimateUnsubscribePacketSizeMqtt5((UnsubscribePacket)packet));
            case MqttPacketType.UnsubAck:
                return new PacketSize(EstimateUnsubscribeAckPacketSizeMqtt5((UnsubAckPacket)packet));
            case MqttPacketType.PingReq:
            case MqttPacketType.PingResp:
                return PacketSize.NoContent; // fixed header only
            case MqttPacketType.Disconnect:
                return new PacketSize(EstimateDisconnectPacketSizeMqtt5((DisconnectPacket)packet));
            case MqttPacketType.Auth:
                return new PacketSize(EstimateAuthPacketSizeMqtt5((AuthPacket)packet));
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // ── MQTT 5.0 estimator helpers ───────────────────────────────────────────

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

    // ── AUTH ─────────────────────────────────────────────────────────────────

    private static int EstimateAuthPacketSizeMqtt5(AuthPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeAuthPacket
        var propsSize = 0;
        // Authentication Method is always present on AuthPacket
        propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (!packet.AuthenticationData.IsEmpty)
            propsSize += 1 + 2 + packet.AuthenticationData.Length;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = reason code (1) + VBI(propsSize) + propsSize
        return 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── UNSUBACK ─────────────────────────────────────────────────────────────

    private static int EstimateUnsubscribeAckPacketSizeMqtt5(UnsubAckPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeUnsubAckPacket
        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + VBI(propsSize) + propsSize + reason codes (1 each)
        return 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.ReasonCodes.Count;
    }

    // ── UNSUBSCRIBE ───────────────────────────────────────────────────────────

    private static int EstimateUnsubscribePacketSizeMqtt5(UnsubscribePacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeUnsubscribePacket
        var propsSize = packet.UserProperties != null && packet.UserProperties.Any()
            ? ComputeUserPropertiesSize(packet.UserProperties)
            : 0;
        var topicsPayloadSize = packet.Topics.Sum(t => 2 + Encoding.UTF8.GetByteCount(t));

        // contentSize = packet ID (2) + VBI(propsSize) + propsSize + topics payload
        return 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + topicsPayloadSize;
    }

    // ── SUBACK ───────────────────────────────────────────────────────────────

    private static int EstimateSubAckPacketSizeMqtt5(SubAckPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeSubAckPacket
        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + VBI(propsSize) + propsSize + reason codes (1 each)
        return 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.ReasonCodes.Count;
    }

    // ── SUBSCRIBE ─────────────────────────────────────────────────────────────

    private static int EstimateSubscribePacketSizeMqtt5(SubscribePacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeSubscribePacket
        var propsSize = 0;
        if (packet.SubscriptionIdentifier.HasValue)
            propsSize += 1 + Mqtt5PropertyWriter.GetVariableByteIntSize(packet.SubscriptionIdentifier.Value);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // Topics payload: 2 (length prefix) + topic bytes + 1 (subscription options)
        var topicsPayloadSize = packet.Topics.Sum(t => 2 + Encoding.UTF8.GetByteCount(t.Topic) + 1);

        // contentSize = packet ID (2) + VBI(propsSize) + propsSize + topics payload
        return 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + topicsPayloadSize;
    }

    // ── PUBCOMP ──────────────────────────────────────────────────────────────

    private static int EstimatePubCompPacketSizeMqtt5(PubCompPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodePubCompPacket (compact form logic)
        var reasonCode = packet.ReasonCode ?? PubCompReasonCode.Success;
        var hasProps = !string.IsNullOrEmpty(packet.ReasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubCompReasonCode.Success && !hasProps;

        if (isCompact)
            return 2; // packet ID only

        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + reason code (1) + VBI(propsSize) + propsSize
        return 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── PUBREL ───────────────────────────────────────────────────────────────

    private static int EstimatePubRelPacketSizeMqtt5(PubRelPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodePubRelPacket (compact form logic)
        var reasonCode = packet.ReasonCode ?? PubRelReasonCode.Success;
        var hasProps = !string.IsNullOrEmpty(packet.ReasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubRelReasonCode.Success && !hasProps;

        if (isCompact)
            return 2; // packet ID only

        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + reason code (1) + VBI(propsSize) + propsSize
        return 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── PUBREC ───────────────────────────────────────────────────────────────

    private static int EstimatePubRecPacketSizeMqtt5(PubRecPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodePubRecPacket (compact form logic)
        var reasonCode = packet.ReasonCode ?? PubRecReasonCode.Success;
        var hasProps = !string.IsNullOrEmpty(packet.ReasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubRecReasonCode.Success && !hasProps;

        if (isCompact)
            return 2; // packet ID only

        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + reason code (1) + VBI(propsSize) + propsSize
        return 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── PUBACK ───────────────────────────────────────────────────────────────

    private static int EstimatePubAckPacketSizeMqtt5(PubAckPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodePubAckPacket (compact form logic)
        var hasProps = !string.IsNullOrEmpty(packet.ReasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = packet.ReasonCode == MqttPubAckReasonCode.Success && !hasProps;

        if (isCompact)
            return 2; // packet ID only

        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        // contentSize = packet ID (2) + reason code (1) + VBI(propsSize) + propsSize
        return 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── PUBLISH ──────────────────────────────────────────────────────────────

    private static int EstimatePublishPacketSizeMqtt5(PublishPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodePublishPacket / ComputePublishPropertiesSize
        var propsSize = 0;
        if (packet.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified) propsSize += 1 + 1;
        if (packet.MessageExpiryInterval != 0) propsSize += 1 + 4;
        if (packet.TopicAlias != 0) propsSize += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ResponseTopic))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ResponseTopic);
        if (packet.CorrelationData.HasValue && !packet.CorrelationData.Value.IsEmpty)
            propsSize += 1 + 2 + packet.CorrelationData.Value.Length;
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);
        if (packet.SubscriptionIdentifiers != null && packet.SubscriptionIdentifiers.Count > 0)
        {
            foreach (var sid in packet.SubscriptionIdentifiers)
                propsSize += 1 + Mqtt5PropertyWriter.GetVariableByteIntSize(sid);
        }
        if (!string.IsNullOrEmpty(packet.ContentType))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ContentType);

        var topicBytes = Encoding.UTF8.GetByteCount(packet.TopicName);

        // contentSize = topic (2+len) + [packet ID (2)] + VBI(propsSize) + propsSize + payload
        return 2 + topicBytes
            + (packet.QualityOfService > QualityOfService.AtMostOnce ? 2 : 0)
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.Payload.Length;
    }

    // ── CONNACK ──────────────────────────────────────────────────────────────

    private static int EstimateConnAckPacketSizeMqtt5(ConnAckPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeConnAckPacket / ComputeConnAckPropertiesSize
        var propsSize = 0;

        if (packet.SessionExpiryInterval.HasValue) propsSize += 1 + 4;
        if (packet.ReceiveMaximum.HasValue) propsSize += 1 + 2;
        if (packet.MaximumQoS.HasValue) propsSize += 1 + 1;
        if (packet.RetainAvailable.HasValue) propsSize += 1 + 1;
        if (packet.MaximumPacketSize.HasValue) propsSize += 1 + 4;
        if (!string.IsNullOrEmpty(packet.AssignedClientIdentifier))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AssignedClientIdentifier);
        if (packet.TopicAliasMaximum.HasValue) propsSize += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);
        if (packet.WildcardSubscriptionAvailable.HasValue) propsSize += 1 + 1;
        if (packet.SubscriptionIdentifiersAvailable.HasValue) propsSize += 1 + 1;
        if (packet.SharedSubscriptionAvailable.HasValue) propsSize += 1 + 1;
        if (packet.ServerKeepAlive.HasValue) propsSize += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ResponseInformation))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ResponseInformation);
        if (!string.IsNullOrEmpty(packet.ServerReference))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ServerReference);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            propsSize += 1 + 2 + packet.AuthenticationData.Value.Length;

        // contentSize = session present (1) + reason code (1) + VBI(propsSize) + propsSize
        return 2 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    /// <summary>
    ///  Helper method to calculate the length of the Variable Byte Integer for MQTT packet lengths
    /// </summary>
    /// <remarks>
    /// MQTT variable-length encoding uses 1-4 bytes depending on the packet body length:
    /// 1 byte for 0-127, 2 bytes for 128-16383, 3 bytes for 16384-2097151, 4 bytes for larger.
    /// </remarks>
    public static int GetPacketLengthHeaderSize(int packetBodyLength)
    {
        // MQTT variable-length encoding: 1 byte for 0-127, 2 bytes for 128-16383, etc.
        return packetBodyLength switch
        {
            < 128 => 1,
            < 16384 => 2,
            < 2097152 => 3,
            _ => 4
        };
    }

    // ── DISCONNECT ───────────────────────────────────────────────────────────

    /// <summary>
    /// Gets just the packet size back - **does not include the size of the length header**
    /// </summary>
    private static int EstimateDisconnectPacketSizeMqtt5(DisconnectPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeDisconnectPacket (compact form logic)
        var reasonCode = packet.ReasonCode ?? DisconnectReasonCode.NormalDisconnection;
        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (!string.IsNullOrEmpty(packet.ServerReference))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ServerReference);
        if (packet.SessionExpiryInterval.HasValue)
            propsSize += 1 + 4;
        if (packet.UserProperties != null && packet.UserProperties.Any())
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        var isCompact = reasonCode == DisconnectReasonCode.NormalDisconnection && propsSize == 0;

        if (isCompact)
            return 0; // compact form: Remaining Length = 0

        // non-compact: reason code (1) + VBI(propsSize) + propsSize
        return 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
    }

    // ── CONNECT (MQTT 3.1.1) ─────────────────────────────────────────────────

    private static int EstimateConnectPacketSizeMqtt311(ConnectPacket packet)
    {
        var size = 0; // fixed header not included in length calculation
        // Variable header:

        /*
            +-------------------+-----------------+----------------+------------+----------------+
            |  Protocol Name    | Protocol Version| Connect Flags  |  Keep Alive|    Properties  |
            |      X Bytes      |      1 Byte     |     1 Byte     |   2 Bytes  |      X Bytes   |
            +-------------------+-----------------+----------------+------------+----------------+
            */


        // Protocol Name (2 bytes length + actual length of string)

        size += 2 + Encoding.UTF8.GetByteCount(packet
            .ProtocolName); // MQTT uses ASCII, not UTF8, for the protocol name: https://www.emqx.com/en/blog/mqtt-5-0-control-packets-01-connect-connack#connect-packet-structure

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

        if (!string.IsNullOrEmpty(packet.UserName))
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.UserName);
        }

        if (!string.IsNullOrEmpty(packet.Password))
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(packet.Password);
        }

        return size + payloadSize;
    }

    // ── CONNECT (MQTT 5.0) ───────────────────────────────────────────────────

    private static int EstimateConnectPacketSizeMqtt5(ConnectPacket packet)
    {
        // Mirror Mqtt5Encoder.EncodeConnectPacket / ComputeConnectContentSize
        var connectPropsSize = ComputeConnectPropertiesSizeMqtt5(packet);
        var willPropsSize = packet.Flags.WillFlag && packet.Will != null
            ? ComputeWillPropertiesSizeMqtt5(packet.Will)
            : 0;

        var size = 0;

        // Variable header:
        // Protocol Name "MQTT": 2-byte length prefix + 4 bytes
        size += 2 + 4;
        // Protocol Version (1 byte)
        size += 1;
        // Connect Flags (1 byte)
        size += 1;
        // Keep Alive (2 bytes)
        size += 2;
        // Connect Properties section: VBI + properties
        size += Mqtt5PropertyWriter.GetVariableByteIntSize((uint)connectPropsSize) + connectPropsSize;

        // Payload: Client ID
        size += 2 + Encoding.UTF8.GetByteCount(packet.ClientId);

        // Payload: Will (if present)
        if (packet.Flags.WillFlag && packet.Will != null)
        {
            size += Mqtt5PropertyWriter.GetVariableByteIntSize((uint)willPropsSize) + willPropsSize;
            size += 2 + Encoding.UTF8.GetByteCount(packet.Will.Topic);
            size += 2 + packet.Will.Message.Length;
        }

        // Payload: Username
        if (packet.Flags.UsernameFlag && packet.UserName != null)
            size += 2 + Encoding.UTF8.GetByteCount(packet.UserName);

        // Payload: Password
        if (packet.Flags.PasswordFlag && packet.Password != null)
            size += 2 + Encoding.UTF8.GetByteCount(packet.Password);

        return size;
    }

    private static int ComputeConnectPropertiesSizeMqtt5(ConnectPacket packet)
    {
        // These 6 properties are always written (matching Mqtt5Encoder.ComputeConnectPropertiesSize)
        // SEI (1+4) + RcvMax (1+2) + MaxPktSz (1+4) + TopAlias (1+2) + RRI (1+1) + RPI (1+1) = 20
        var size = 5 + 3 + 5 + 3 + 2 + 2;

        if (packet.UserProperties != null && packet.UserProperties.Any())
            size += ComputeUserPropertiesSize(packet.UserProperties);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            size += 1 + 2 + packet.AuthenticationData.Value.Length;

        return size;
    }

    private static int ComputeWillPropertiesSizeMqtt5(MqttLastWill will)
    {
        // Mirror Mqtt5Encoder.ComputeWillPropertiesSize
        var size = 0;
        if (will.DelayInterval.Value != 0) size += 1 + 4;
        if (will.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified) size += 1 + 1;
        if (will.MessageExpiryInterval != 0) size += 1 + 4;
        if (!string.IsNullOrEmpty(will.ContentType))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(will.ContentType);
        if (!string.IsNullOrEmpty(will.ResponseTopic))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(will.ResponseTopic);
        if (will.WillCorrelationData.HasValue && !will.WillCorrelationData.Value.IsEmpty)
            size += 1 + 2 + will.WillCorrelationData.Value.Length;
        if (will.WillProperties != null && will.WillProperties.Count > 0)
            size += ComputeUserPropertiesSize(will.WillProperties);
        return size;
    }
}
