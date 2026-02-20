// -----------------------------------------------------------------------
// <copyright file="PacketGenerators.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests;

public class PacketGenerators
{
    /// <summary>
    /// No AUTH packets in MQTT 3.1.1
    /// </summary>
    public static Arbitrary<MqttPacketType> Mqtt311PacketTypeArbitrary()
    {
        var g = Gen.Elements([
            MqttPacketType.Connect,
            MqttPacketType.ConnAck,
            MqttPacketType.Publish,
            MqttPacketType.PubAck,
            MqttPacketType.PubRec,
            MqttPacketType.PubRel,
            MqttPacketType.PubComp,
            MqttPacketType.Subscribe,
            MqttPacketType.SubAck,
            MqttPacketType.Unsubscribe,
            MqttPacketType.UnsubAck,
            MqttPacketType.PingReq,
            MqttPacketType.PingResp,
            MqttPacketType.Disconnect
        ]);

        return Arb.From(g);
    }

    public static Arbitrary<MqttPacket> ConnectPacketArb()
    {
        return (from protocolVersion in Gen.Constant(MqttProtocolVersion.V3_1_1)
            from clientId in
                Arb.Generate<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0 && MqttClientIdValidator.ValidateClientId(s).IsValid) // ensure clientId is not null or whitespace
            from cleanSession in Arb.Generate<bool>()
            from keepAlive in Arb.Generate<ushort>()
            select (MqttPacket)new ConnectPacket(protocolVersion)
            {
                ClientId = clientId,
                ConnectFlags = new ConnectFlags()
                {
                    CleanSession = cleanSession
                },
                KeepAliveSeconds = keepAlive
            }).ToArbitrary();
    }

    public static Arbitrary<MqttPacket> PublishPacketArb()
    {
        return (from qos in Arb.Generate<QualityOfService>()
            from duplicate in Arb.Generate<bool>()
            from retainRequested in Arb.Generate<bool>()
            from topicName in
                Arb.Generate<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0 && MqttTopicValidator.ValidatePublishTopic(s).IsValid) // ensure topicName is not null or whitespace
            from payloadLength in Gen.Choose(0, 32 * 1024) // You can adjust the max size as needed
            from bytes in Gen.ArrayOf(payloadLength, Arb.Generate<byte>())
            from packetId in Arb.Generate<ushort>()
            select (MqttPacket)new PublishPacket(qos, duplicate, retainRequested, topicName)
            {
                Payload = new ReadOnlyMemory<byte>(bytes),
                PacketId = packetId
            }).ToArbitrary();
    }
    
    /// <summary>
    /// Generates valid MQTT 3.1.1 CONNACK packets.
    /// Valid return codes per OASIS MQTT 3.1.1 §3.2.2.3: 0x00-0x05.
    /// Per spec, SessionPresent MUST be 0 when return code != 0 (not accepted).
    /// </summary>
    public static Arbitrary<MqttPacket> ConnAckPacketArb()
    {
        var mqtt311ReturnCodes = new ConnAckReasonCode[]
        {
            ConnAckReasonCode.Success,        // 0x00: Connection Accepted
            (ConnAckReasonCode)0x01,          // Connection Refused - unacceptable protocol version
            (ConnAckReasonCode)0x02,          // Connection Refused - identifier rejected
            (ConnAckReasonCode)0x03,          // Connection Refused - server unavailable
            (ConnAckReasonCode)0x04,          // Connection Refused - bad user name or password
            (ConnAckReasonCode)0x05,          // Connection Refused - not authorized
        };

        return (from reasonCode in Gen.Elements(mqtt311ReturnCodes)
                // Per MQTT 3.1.1 spec §3.2.2.2, SessionPresent must be 0 if return code != Success
                from sessionPresent in reasonCode == ConnAckReasonCode.Success
                    ? Arb.Generate<bool>()
                    : Gen.Constant(false)
                select (MqttPacket)new ConnAckPacket
                {
                    SessionPresent = sessionPresent,
                    ReasonCode = reasonCode
                }).ToArbitrary();
    }

    /// <summary>Generates valid MQTT 3.1.1 PUBACK packets with randomized packet identifiers.</summary>
    public static Arbitrary<MqttPacket> PubAckPacketArb()
    {
        return (from packetId in Gen.Choose(1, 65535)
                select (MqttPacket)new PubAckPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId
                }).ToArbitrary();
    }

    /// <summary>Generates valid MQTT 3.1.1 PUBREC packets with randomized packet identifiers.</summary>
    public static Arbitrary<MqttPacket> PubRecPacketArb()
    {
        return (from packetId in Gen.Choose(1, 65535)
                select (MqttPacket)new PubRecPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId
                }).ToArbitrary();
    }

    /// <summary>Generates valid MQTT 3.1.1 PUBREL packets with randomized packet identifiers.</summary>
    public static Arbitrary<MqttPacket> PubRelPacketArb()
    {
        return (from packetId in Gen.Choose(1, 65535)
                select (MqttPacket)new PubRelPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId
                }).ToArbitrary();
    }

    /// <summary>Generates valid MQTT 3.1.1 PUBCOMP packets with randomized packet identifiers.</summary>
    public static Arbitrary<MqttPacket> PubCompPacketArb()
    {
        return (from packetId in Gen.Choose(1, 65535)
                select (MqttPacket)new PubCompPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId
                }).ToArbitrary();
    }

    /// <summary>
    /// Generates valid MQTT 3.1.1 SUBSCRIBE packets with 1-5 valid topic filters.
    /// SubscriptionIdentifier is set to 1 (not encoded in MQTT 3.1.1).
    /// </summary>
    public static Arbitrary<MqttPacket> SubscribePacketArb()
    {
        var validSubscribeTopic = Arb.Generate<string>()
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                        && MqttTopicValidator.ValidateSubscribeTopic(s).IsValid);

        var topicSubscriptionGen = from topic in validSubscribeTopic
                                   from qos in Arb.Generate<QualityOfService>()
                                   select new TopicSubscription(topic)
                                   {
                                       Options = new SubscriptionOptions { QoS = qos }
                                   };

        return (from packetId in Gen.Choose(1, 65535)
                from topicCount in Gen.Choose(1, 5)
                from topics in Gen.ArrayOf(topicCount, topicSubscriptionGen)
                select (MqttPacket)new SubscribePacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId,
                    SubscriptionIdentifier = new NonZeroUInt16(1), // not encoded in MQTT 3.1.1
                    Topics = topics
                }).ToArbitrary();
    }

    /// <summary>
    /// Generates valid MQTT 3.1.1 SUBACK packets with 1-5 reason codes.
    /// Only MQTT 3.1.1 valid codes: GrantedQoS0, GrantedQoS1, GrantedQoS2, UnspecifiedError.
    /// </summary>
    public static Arbitrary<MqttPacket> SubAckPacketArb()
    {
        var mqtt311SubAckCodes = new MqttSubscribeReasonCode[]
        {
            MqttSubscribeReasonCode.GrantedQoS0,
            MqttSubscribeReasonCode.GrantedQoS1,
            MqttSubscribeReasonCode.GrantedQoS2,
            MqttSubscribeReasonCode.UnspecifiedError
        };

        return (from packetId in Gen.Choose(1, 65535)
                from codeCount in Gen.Choose(1, 5)
                from codes in Gen.ArrayOf(codeCount, Gen.Elements(mqtt311SubAckCodes))
                select (MqttPacket)new SubAckPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId,
                    ReasonCodes = codes
                }).ToArbitrary();
    }

    /// <summary>
    /// Generates valid MQTT 3.1.1 UNSUBSCRIBE packets with 1-5 valid topic filters.
    /// </summary>
    public static Arbitrary<MqttPacket> UnsubscribePacketArb()
    {
        var validSubscribeTopic = Arb.Generate<string>()
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 0
                        && MqttTopicValidator.ValidateSubscribeTopic(s).IsValid);

        return (from packetId in Gen.Choose(1, 65535)
                from topicCount in Gen.Choose(1, 5)
                from topics in Gen.ArrayOf(topicCount, validSubscribeTopic)
                select (MqttPacket)new UnsubscribePacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId,
                    Topics = topics
                }).ToArbitrary();
    }

    /// <summary>
    /// Generates valid MQTT 3.1.1 UNSUBACK packets.
    /// In MQTT 3.1.1 this packet contains only the packet identifier.
    /// </summary>
    public static Arbitrary<MqttPacket> UnsubAckPacketArb()
    {
        return (from packetId in Gen.Choose(1, 65535)
                select (MqttPacket)new UnsubAckPacket
                {
                    PacketId = (NonZeroUInt16)(ushort)packetId
                }).ToArbitrary();
    }

    /// <summary>Generates the PINGREQ singleton. No variable fields in MQTT 3.1.1.</summary>
    public static Arbitrary<MqttPacket> PingReqPacketArb()
        => Gen.Constant((MqttPacket)PingReqPacket.Instance).ToArbitrary();

    /// <summary>Generates the PINGRESP singleton. No variable fields in MQTT 3.1.1.</summary>
    public static Arbitrary<MqttPacket> PingRespPacketArb()
        => Gen.Constant((MqttPacket)PingRespPacket.Instance).ToArbitrary();

    /// <summary>
    /// Generates MQTT 3.1.1 DISCONNECT packets.
    /// In MQTT 3.1.1 this is a fixed-header-only packet with no variable content.
    /// </summary>
    public static Arbitrary<MqttPacket> DisconnectPacketArb()
        => Gen.Constant((MqttPacket)new DisconnectPacket()).ToArbitrary();

    /// <summary>
    /// Combined generator using all 14 MQTT 3.1.1 packet types via Gen.OneOf.
    /// </summary>
    public static Arbitrary<MqttPacket> PacketArb()
    {
        return Gen.OneOf(
            ConnectPacketArb().Generator,
            ConnAckPacketArb().Generator,
            PublishPacketArb().Generator,
            PubAckPacketArb().Generator,
            PubRecPacketArb().Generator,
            PubRelPacketArb().Generator,
            PubCompPacketArb().Generator,
            SubscribePacketArb().Generator,
            SubAckPacketArb().Generator,
            UnsubscribePacketArb().Generator,
            UnsubAckPacketArb().Generator,
            PingReqPacketArb().Generator,
            PingRespPacketArb().Generator,
            DisconnectPacketArb().Generator
        ).ToArbitrary();
    }

    public static Arbitrary<ReadOnlyMemory<byte>[]> FragmentedPackets(Arbitrary<MqttPacket> packetArb)
    {
        // need help here
        var serializedPackets = packetArb.Generator.Select(packet =>
        {
            var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
            
            Memory<byte> bytes = new byte[estimatedSize.TotalSize];
            var serializedPacket = Mqtt311Encoder.EncodePacket(packet, ref bytes, estimatedSize);

            return (bytes, estimatedSize);
        });
        
        return (from d in serializedPackets
            from fragmentCount in Gen.Choose(1, 10)
            from sizes in Gen.ArrayOf(fragmentCount, Gen.Choose(1, d.bytes.Length / fragmentCount + 1))
            where sizes.Sum() == d.bytes.Length
            select CreateFragments(d.bytes, sizes)).ToArbitrary();
    }
    
    private static ReadOnlyMemory<byte>[] CreateFragments(in ReadOnlyMemory<byte> source, int[] sizes)
    {
        var fragments = new List<ReadOnlyMemory<byte>>();
        int offset = 0;
        foreach (var size in sizes)
        {
            var fragment = source.Slice(offset, size);
            fragments.Add(fragment);
            offset += size;
        }
        return fragments.ToArray();
    }
}