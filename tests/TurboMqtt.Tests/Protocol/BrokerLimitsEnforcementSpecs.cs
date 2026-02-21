// -----------------------------------------------------------------------
// <copyright file="BrokerLimitsEnforcementSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol.Pub;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Unit and integration tests verifying that broker-advertised limits from CONNACK
/// are enforced by the retry actors (Task 3.6).
/// </summary>
public class BrokerLimitsEnforcementSpecs : TestKit
{
    // =========================================================
    // AtLeastOncePublishRetryActor — ReceiveMaximum throttling
    // =========================================================

    [Fact]
    public async Task AtLeastOnceRetryActor_queues_publishes_beyond_receive_maximum_resumes_on_ack()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1))));

        // Set ReceiveMaximum to 1 so any second publish is buffered
        actor.Tell(new PublishingProtocol.SetReceiveMaximum(1));

        var packet1 = MakeQos1Packet(1);
        var packet2 = MakeQos1Packet(2); // should be buffered

        actor.Tell(packet1, probe);
        actor.Tell(packet2, probe);

        // packet1 should reach the channel
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var inChannel = await channel.Reader.ReadAsync(cts.Token);
        inChannel.Should().Be(packet1);

        // packet2 should NOT be in channel yet (buffered)
        // Give actor time to process packet2 message
        await Task.Delay(50, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("packet2 should still be buffered");

        // ACK packet1 — slot freed, packet2 should be dequeued and sent to channel
        actor.Tell(packet1.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        // packet2 should now be in channel
        var dequeued = await channel.Reader.ReadAsync(cts.Token);
        dequeued.Should().Be(packet2);

        // ACK packet2 to clean up
        actor.Tell(packet2.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public async Task AtLeastOnceRetryActor_sends_five_publishes_two_at_a_time_with_receive_max_2()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1))));

        actor.Tell(new PublishingProtocol.SetReceiveMaximum(2));

        // Send 5 publishes
        var packets = Enumerable.Range(1, 5).Select(i => MakeQos1Packet((ushort)i)).ToArray();
        foreach (var p in packets)
            actor.Tell(p, probe);

        using var cts = new CancellationTokenSource(RemainingOrDefault);

        // First 2 should go to channel immediately
        var r1 = await channel.Reader.ReadAsync(cts.Token);
        var r2 = await channel.Reader.ReadAsync(cts.Token);
        r1.Should().Be(packets[0]);
        r2.Should().Be(packets[1]);

        // Give actor time to process remaining messages; packets 3-5 should be buffered
        await Task.Delay(50, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("packets 3-5 should be buffered");

        // ACK packet1 and packet2 → packets 3 and 4 dequeued
        actor.Tell(packets[0].ToPubAck(), probe);
        actor.Tell(packets[1].ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var r3 = await channel.Reader.ReadAsync(cts.Token);
        var r4 = await channel.Reader.ReadAsync(cts.Token);
        r3.Should().Be(packets[2]);
        r4.Should().Be(packets[3]);

        // Give actor time; packet5 should still be buffered
        await Task.Delay(50, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("packet5 should still be buffered");

        // ACK packet3 → packet5 dequeued
        actor.Tell(packets[2].ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var r5 = await channel.Reader.ReadAsync(cts.Token);
        r5.Should().Be(packets[4]);

        // ACK packets 4 and 5 to finish cleanly
        actor.Tell(packets[3].ToPubAck(), probe);
        actor.Tell(packets[4].ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public void AtLeastOnceRetryActor_without_receive_maximum_does_not_write_to_channel_on_initial_receive()
    {
        // When ReceiveMaximum is NOT set, the actor should NOT write to channel on initial publish
        // (MqttClient is responsible for the initial write in that case).
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1))));

        var packet = MakeQos1Packet(1);
        actor.Tell(packet, probe);

        // Give actor time to process
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // Channel should be empty (actor does not write; MqttClient would write outside)
        channel.Reader.TryRead(out _).Should().BeFalse("actor should not write to channel when ReceiveMaximum is 0");
    }

    // =========================================================
    // ExactlyOncePublishRetryActor — ReceiveMaximum throttling
    // =========================================================

    [Fact]
    public async Task ExactlyOnceRetryActor_queues_publishes_beyond_receive_maximum_resumes_on_pubcomp()
    {
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ExactlyOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1))));

        actor.Tell(new PublishingProtocol.SetReceiveMaximum(1));

        var packet1 = MakeQos2Packet(1);
        var packet2 = MakeQos2Packet(2); // should be buffered

        actor.Tell(packet1, probe);
        actor.Tell(packet2, probe);

        using var cts = new CancellationTokenSource(RemainingOrDefault);

        // packet1 should reach the channel
        var inChannel = await channel.Reader.ReadAsync(cts.Token);
        inChannel.Should().Be(packet1);

        // packet2 should NOT be in channel yet
        await Task.Delay(50, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("packet2 should be buffered");

        // Complete QoS 2 exchange for packet1
        actor.Tell(packet1.ToPubRec(), probe);

        // Actor writes PubRel to channel
        var pubRelMsg = await channel.Reader.ReadAsync(cts.Token);
        pubRelMsg.PacketType.Should().Be(MqttPacketType.PubRel);

        // Send PubComp — slot freed on PubComp per MQTT 5 spec §4.9
        var pubComp = new PubCompPacket { PacketId = packet1.PacketId };
        actor.Tell(pubComp, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        // packet2 should now be in channel
        var dequeued = await channel.Reader.ReadAsync(cts.Token);
        dequeued.Should().Be(packet2);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static PublishPacket MakeQos1Packet(ushort id) =>
        new PublishPacket(QualityOfService.AtLeastOnce, false, false, $"topic/{id}")
        {
            PacketId = id
        };

    private static PublishPacket MakeQos2Packet(ushort id) =>
        new PublishPacket(QualityOfService.ExactlyOnce, false, false, $"topic/{id}")
        {
            PacketId = id
        };
}
