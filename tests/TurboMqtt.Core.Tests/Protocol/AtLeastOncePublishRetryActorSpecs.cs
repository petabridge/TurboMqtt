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

    [Fact] public async Task AtLeastOncePublishRetryActor_should_resend_overdue_publish_packets()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        
        // set the timespan to zero, so we have to immediately retry
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 3, TimeSpan.Zero)));

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
        {
            PacketId = 1
        };
        
        actor.Tell(packet, probe);
        actor.Tell(PublishProtocolDefaults.CheckPublishTimeout.Instance);
        
        // we should have received the packet back
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var result = await channel.Reader.ReadAsync(cts.Token);
        result.Should().Be(packet);
        result.Duplicate.Should().BeTrue(); // duplicate flag needs to be set on retries
        
        // ack the packet
        var pubAck = packet.ToPubAck();
        actor.Tell(pubAck, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }
    
    [Fact] public async Task AtLeastOncePublishRetryActor_should_fail_undeliverable_packets()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        
        // set the timespan to zero, so we have to immediately retry - no retries available, so we'll immediately fail
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 0, TimeSpan.Zero)));

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic")
        {
            PacketId = 1
        };
        
        actor.Tell(packet, probe);
        actor.Tell(PublishProtocolDefaults.CheckPublishTimeout.Instance);
        // should get a failure message
        await probe.ExpectMsgAsync<PublishingProtocol.PublishFailure>();
    }
}