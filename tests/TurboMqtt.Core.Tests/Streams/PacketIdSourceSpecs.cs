// -----------------------------------------------------------------------
// <copyright file="PacketIdSourceSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Tests.Streams;

public class PacketIdFlowSpecs : TestKit
{
    [Fact]
    public async Task PacketIdSource_should_generate_unique_packet_ids()
    {
        var flow = new PacketIdFlow();
        var probe = this.CreateManualSubscriberProbe<MqttPacket>();
        
        // create a Publish packet with QoS 1
        var publish = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic");
        var source = Source.Repeat<MqttPacket>(publish).Take(3);

        source.Via(flow).RunWith(Sink.FromSubscriber(probe), Sys);

        var sub = await probe.ExpectSubscriptionAsync();
        sub.Request(1);

        var packet1 = await probe.ExpectNextAsync();
        ((PublishPacket)packet1).PacketId.Should().Be(new NonZeroUInt16(1));
        sub.Request(1);
        var packet2 =await probe.ExpectNextAsync();
        ((PublishPacket)packet2).PacketId.Should().Be(new NonZeroUInt16(2));
        sub.Request(1);
        var packet3 =await probe.ExpectNextAsync();
        ((PublishPacket)packet3).PacketId.Should().Be(new NonZeroUInt16(3));
    }
    
    [Fact]
    public async Task PacketIdSource_should_wrap_around_when_max_value_reached()
    {
        var flow = new PacketIdFlow(ushort.MaxValue-1);
        var probe = this.CreateManualSubscriberProbe<MqttPacket>();
        
        // create a Publish packet with QoS 1
        var publish = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic");
        var source = Source.Repeat<MqttPacket>(publish).Take(3);

        source.Via(flow).RunWith(Sink.FromSubscriber(probe), Sys);

        var sub = await probe.ExpectSubscriptionAsync();
        sub.Request(1);

        var packet1 = await probe.ExpectNextAsync();
        ((PublishPacket)packet1).PacketId.Should().Be(new NonZeroUInt16(ushort.MaxValue));
        
        sub.Request(1);
        var packet2 =await probe.ExpectNextAsync();
        // we should always skip zero, since it's an illegal value
        ((PublishPacket)packet2).PacketId.Should().Be(new NonZeroUInt16(1));
        
        sub.Request(1);
        var packet3 =await probe.ExpectNextAsync();
        ((PublishPacket)packet3).PacketId.Should().Be(new NonZeroUInt16(2));
    }
}