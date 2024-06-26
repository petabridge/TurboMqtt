// -----------------------------------------------------------------------
// <copyright file="PacketEncodingTestHelper.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets;

public static class PacketEncodingTestHelper
{
    public static TPacket EncodeAndDecodeMqtt311Packet<TPacket>(TPacket packet,Mqtt311Decoder decoder)
        where TPacket : MqttPacket
    {
        var buffer = EncodePacketOnly(packet);

        var decoded = decoder.TryDecode(buffer, out var packets);
        decoded.Should().BeTrue();
        packets.Count.Should().Be(1);
        
        return (TPacket)packets[0];
    }

    public static Memory<byte> EncodePacketOnly<TPacket>(TPacket packet) where TPacket : MqttPacket
    {
        var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
        var buffer = new Memory<byte>(new byte[estimatedSize.TotalSize]);
        
        var actualBytesWritten = Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);
        actualBytesWritten.Should().Be(estimatedSize.TotalSize);
        return buffer;
    }
}