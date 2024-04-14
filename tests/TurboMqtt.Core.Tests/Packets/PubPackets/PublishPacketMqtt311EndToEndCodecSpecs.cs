// -----------------------------------------------------------------------
// <copyright file="PublishPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.PubPackets;

public class PublishPacketMqtt311EndToEndCodecSpecs
{
    public class MustWorkWithSingleMessage()
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PublishPacket> PublishPackets = new()
        {
            // zero payload with no QoS
            new PublishPacket(QualityOfService.AtMostOnce, false, false, "topic")
            {
                PacketId = 1,
            },
            
            // zero payload with QoS
            new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>(Array.Empty<byte>())
            },
            
            // payload with no QoS
            new PublishPacket(QualityOfService.AtMostOnce, false, false, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>([0, 1, 2, 3])
            },
            
            // payload with QoS
            new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>([0, 1, 2, 3])
            },
            
            // payload with QoS and retain
            new PublishPacket(QualityOfService.AtLeastOnce, false, true, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>([0, 1, 2, 3])
            },
            
            // payload with QoS and retain and duplicate
            new PublishPacket(QualityOfService.AtLeastOnce, true, true, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>([0, 1, 2, 3])
            },
        };
        
        [Theory]
        [MemberData(nameof(PublishPackets))]
        public void ShouldEncodeAndDecodePublishPacket(PublishPacket packet)
        {
            if(packet.QualityOfService == QualityOfService.AtMostOnce)
                packet.PacketId = default; 
            
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.Should().BeEquivalentTo(packet, options => options.Excluding(x => x.Payload));
            
            if(packet.Payload.Length > 0)
                decodedPacket.Payload.ToArray().Should().BeEquivalentTo(packet.Payload.ToArray());
        }
    }

    public class MustWorkWithLargerMessages
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PublishPacket> PublishPackets = new()
        {
            // 1kb payload with no QoS
            new PublishPacket(QualityOfService.AtMostOnce, false, false, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>(Enumerable.Range(0, 1024).Select(x => (byte)8).ToArray())
            },
            
            // 1kb payload with QoS
            new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>(Enumerable.Range(0, 1024).Select(x => (byte)8).ToArray())
            },
            
            // 100kb payload with no QoS
            new PublishPacket(QualityOfService.AtMostOnce, false, false, "test/2/3")
            {
                PacketId = 1,
                Payload = new ReadOnlyMemory<byte>(Enumerable.Range(0, 1024*128).Select(x => (byte)8).ToArray())
            },
            
            // 1mb payload with QoS, retain, dup
            new PublishPacket(QualityOfService.AtMostOnce, true, true, "test/2/3")
            {
                PacketId = 112,
                Payload = new ReadOnlyMemory<byte>(Enumerable.Range(0, 1024*1024).Select(x => (byte)8).ToArray())
            },
        };
        
        [Theory]
        [MemberData(nameof(PublishPackets))]
        public void ShouldEncodeAndDecodePublishPacket(PublishPacket packet)
        {
            if(packet.QualityOfService == QualityOfService.AtMostOnce)
                packet.PacketId = default; 
            
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.Should().BeEquivalentTo(packet, options => options.Excluding(x => x.Payload));
            
            if(packet.Payload.Length > 0)
                decodedPacket.Payload.ToArray().Should().BeEquivalentTo(packet.Payload.ToArray());
        }
    }
}