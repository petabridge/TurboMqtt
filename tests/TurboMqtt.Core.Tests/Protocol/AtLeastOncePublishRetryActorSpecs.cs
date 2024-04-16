// -----------------------------------------------------------------------
// <copyright file="AtLeastOncePublishRetryActorSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Protocol;

public class AtLeastOncePublishRetryActorSpecs : TestKit
{
    [Fact]
    public async Task AtLeastOncePublishRetryActor_should_publish_packet_with_puback()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() => new AtLeastOncePublishRetryActor(channel, 1, TimeSpan.FromMinutes(1))));

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
        {
            PacketId = 2
        };
        var pubAck = packet.ToPubAck();
        
        actor.Tell(packet, probe);
        actor.Tell(pubAck, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>();
    }

    [Fact]
    public void AtLeastOncePublishRetryActor_should_not_publish_duplicate_packet()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 1, TimeSpan.FromMinutes(1))));

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
        {
            PacketId = 1
        };
        actor.Tell(packet, probe);
        actor.Tell(packet, probe);
        
        probe.ExpectMsg<PublishingProtocol.PublishFailure>().Reason.Should().Be("Duplicate packet ID");
    }
}