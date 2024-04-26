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
            from payloadLength in Gen.Choose(0, 16 * 1024) // You can adjust the max size as needed
            from bytes in Gen.ArrayOf(payloadLength, Arb.Generate<byte>())
            from packetId in Arb.Generate<ushort>()
            select (MqttPacket)new PublishPacket(qos, duplicate, retainRequested, topicName)
            {
                Payload = new ReadOnlyMemory<byte>(bytes),
                PacketId = packetId
            }).ToArbitrary();
    }
    
    public static Arbitrary<MqttPacket> PacketArb()
    {
        return Gen.OneOf(
            ConnectPacketArb().Generator,
            PublishPacketArb().Generator
        ).ToArbitrary();
    }
    
    public static Arbitrary<ReadOnlyMemory<byte>[]> FragmentedPackets(Arbitrary<MqttPacket> packetArb)
    {
        // need help here
        var serializedPackets = packetArb.Generator.Select(packet =>
        {
            var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
            
            // need to always add 1 for the fixed byte
            var fullSize = MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimatedSize) + estimatedSize + 1;
            Memory<byte> bytes = new byte[fullSize];
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