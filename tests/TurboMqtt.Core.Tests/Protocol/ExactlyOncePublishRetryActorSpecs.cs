// -----------------------------------------------------------------------
// <copyright file="ExactlyOncePublishRetryActorSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Protocol;

public class ExactlyOncePublishRetryActorSpecs : TestKit
{
    /// <summary>
    /// Happy path test for <see cref="ExactlyOncePublishRetryActor"/> - should publish a packet and receive a <see cref="PublishingProtocol.PublishSuccess"/>
    /// </summary>
    [Fact]
    public async Task ExactlyOncePublishRetryActor_should_publish_packet_completely()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() => new ExactlyOncePublishRetryActor(channel, 1, TimeSpan.FromMinutes(1))));

        var packet = new PublishPacket(QualityOfService.ExactlyOnce, false, false, "topic")
        {
            PacketId = 2
        };
        var pubRec = packet.ToPubRec();
        
        actor.Tell(packet, probe);
        actor.Tell(pubRec, probe);
        
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var msg = await channel.Reader.ReadAsync(cts.Token);
        msg.PacketType.Should().Be(MqttPacketType.PubRel);
        // check the packet ids - they should all match
        ((PubRelPacket)msg).PacketId.Should().Be(packet.PacketId);
        
        // send a pubcomp
        actor.Tell(packet.ToPubComp(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public void ExactlyOncePublishRetryActor_should_not_publish_duplicate_packet()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ExactlyOncePublishRetryActor(channel.Writer, 1, TimeSpan.FromMinutes(1))));

        var packet = new PublishPacket(QualityOfService.ExactlyOnce, false, false, "topic")
        {
            PacketId = 1
        };
        actor.Tell(packet, probe);
        actor.Tell(packet, probe);
        
        probe.ExpectMsg<PublishingProtocol.PublishFailure>().Reason.Should().Be("Duplicate packet ID");
    }

    [Fact]
    public async Task ExactlyOncePublishRetryActor_should_resend_overdue_publish_packets()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();

        // set the timespan to zero, so we have to immediately retry
        var actor = Sys.ActorOf(Props.Create(() =>
            new ExactlyOncePublishRetryActor(channel.Writer, 3, TimeSpan.Zero)));

        var packet = new PublishPacket(QualityOfService.ExactlyOnce, false, false, "topic")
        {
            PacketId = 2
        };
        
        actor.Tell(packet, probe);
        actor.Tell(PublishProtocolDefaults.CheckPublishTimeout.Instance);
        
        // we should have received the packet back
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var result = await channel.Reader.ReadAsync(cts.Token);
        result.Should().Be(packet);
        packet.Duplicate.Should().BeTrue();
    }
}