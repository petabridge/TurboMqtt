// -----------------------------------------------------------------------
// <copyright file="SharedReceiveMaximumQuotaSpecs.cs" company="Petabridge, LLC">
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
/// Tests that verify <see cref="SharedReceiveMaximumQuota"/> correctly limits the combined
/// in-flight count of QoS 1 + QoS 2 publishes (MQTT 5.0 §4.9).
/// </summary>
public class SharedReceiveMaximumQuotaSpecs : TestKit
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PublishPacket MakeQos1(ushort id) =>
        new(QualityOfService.AtLeastOnce, false, false, $"qos1/topic/{id}") { PacketId = id };

    private static PublishPacket MakeQos2(ushort id) =>
        new(QualityOfService.ExactlyOnce, false, false, $"qos2/topic/{id}") { PacketId = id };

    /// <summary>
    /// Returns the packet ID from any <see cref="MqttPacketWithId"/> in the channel.
    /// </summary>
    private static NonZeroUInt16 IdOf(MqttPacket p) => ((MqttPacketWithId)p).PacketId;

    /// <summary>
    /// Creates a QoS 1 and a QoS 2 actor that share a single <see cref="SharedReceiveMaximumQuota"/>
    /// and are cross-registered as siblings.
    /// </summary>
    private (IActorRef qos1, IActorRef qos2, SharedReceiveMaximumQuota quota) CreateActors(
        Channel<MqttPacket> channel)
    {
        var quota = new SharedReceiveMaximumQuota();
        var qos1 = Sys.ActorOf(Props.Create(() =>
            new AtLeastOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1), quota)), "qos1");
        var qos2 = Sys.ActorOf(Props.Create(() =>
            new ExactlyOncePublishRetryActor(channel.Writer, 3, TimeSpan.FromMinutes(1), quota)), "qos2");

        qos1.Tell(new SetSiblingPublisher(qos2));
        qos2.Tell(new SetSiblingPublisher(qos1));

        return (qos1, qos2, quota);
    }

    // -----------------------------------------------------------------------
    // SharedReceiveMaximumQuota unit tests (no actors)
    // -----------------------------------------------------------------------

    [Fact]
    public void SharedQuota_TryClaim_unlimited_always_succeeds()
    {
        var quota = new SharedReceiveMaximumQuota(); // max = 0 (unlimited)
        quota.TryClaim().Should().BeTrue();
        quota.TryClaim().Should().BeTrue();
        quota.TryClaim().Should().BeTrue();
    }

    [Fact]
    public void SharedQuota_TryClaim_limited_allows_up_to_maximum()
    {
        var quota = new SharedReceiveMaximumQuota();
        quota.SetMaximum(2);

        quota.TryClaim().Should().BeTrue();  // in-flight = 1
        quota.TryClaim().Should().BeTrue();  // in-flight = 2
        quota.TryClaim().Should().BeFalse(); // in-flight would be 3, rejected
    }

    [Fact]
    public void SharedQuota_Release_frees_a_claimed_slot()
    {
        var quota = new SharedReceiveMaximumQuota();
        quota.SetMaximum(1);

        quota.TryClaim().Should().BeTrue();  // in-flight = 1
        quota.TryClaim().Should().BeFalse(); // rejected

        quota.Release();                      // in-flight = 0
        quota.TryClaim().Should().BeTrue();  // now succeeds again
    }

    [Fact]
    public void SharedQuota_Release_on_unlimited_quota_does_not_underflow()
    {
        var quota = new SharedReceiveMaximumQuota(); // unlimited
        // Release without a prior Claim must not throw or underflow
        quota.Release();
        quota.Release();
        // Unlimited quota still accepts claims
        quota.TryClaim().Should().BeTrue();
    }

    [Fact]
    public void SharedQuota_IsLimited_reflects_configured_maximum()
    {
        var quota = new SharedReceiveMaximumQuota();
        quota.IsLimited.Should().BeFalse();

        quota.SetMaximum(5);
        quota.IsLimited.Should().BeTrue();

        quota.SetMaximum(0);
        quota.IsLimited.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Actor-level tests: shared quota limits total QoS 1 + QoS 2 in-flight
    //
    // DESIGN NOTE: Tests send packets one at a time and wait for each to
    // appear in the channel before sending the next. This ensures the quota
    // is fully claimed before we send the "should be buffered" packet,
    // making the tests deterministic and independent of actor scheduling.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SharedQuota_limits_combined_Qos1_and_Qos2_to_receive_maximum()
    {
        // ReceiveMaximum = 2; send p1 (QoS1) then p2 (QoS2) to fill quota,
        // then p3 (QoS1) which must be buffered.
        // ACK p1 → p3 promoted from QoS 1 buffer.
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var (qos1, qos2, _) = CreateActors(channel);

        qos1.Tell(new PublishingProtocol.SetReceiveMaximum(2));
        qos2.Tell(new PublishingProtocol.SetReceiveMaximum(2));

        var p1 = MakeQos1(1);
        var p2 = MakeQos2(2);
        var p3 = MakeQos1(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Claim slot 1: send p1, wait for it in channel
        qos1.Tell(p1, probe);
        var c1 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c1).Value.Should().Be(1);

        // Claim slot 2: send p2, wait for it in channel (quota now full)
        qos2.Tell(p2, probe);
        var c2 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c2).Value.Should().Be(2);

        // p3 must be buffered: quota is full
        qos1.Tell(p3, probe);
        await Task.Delay(80, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("p3 should be buffered because quota (2) is full");

        // ACK p1 — slot freed; p3 should be promoted from QoS 1 actor's buffer
        qos1.Tell(p1.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var promoted = await channel.Reader.ReadAsync(cts.Token);
        IdOf(promoted).Value.Should().Be(3, "p3 should be promoted after p1's slot is freed");

        // Clean up
        qos1.Tell(p3.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        qos2.Tell(p2.ToPubRec(), probe);
        var pubRelInChannel = await channel.Reader.ReadAsync(cts.Token);
        pubRelInChannel.PacketType.Should().Be(MqttPacketType.PubRel);
        qos2.Tell(new PubCompPacket { PacketId = p2.PacketId }, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public async Task SharedQuota_cross_Qos_drain_when_Qos1_frees_slot_and_Qos2_has_buffer()
    {
        // ReceiveMaximum = 2.
        // Step 1: send p1 (QoS1) and p2 (QoS2) → quota full.
        // Step 2: send p3 (QoS2) → buffered in QoS 2 actor.
        // Step 3: ACK p1 → QoS 1 has no buffer → notifies QoS 2 → p3 promoted.
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var (qos1, qos2, _) = CreateActors(channel);

        qos1.Tell(new PublishingProtocol.SetReceiveMaximum(2));
        qos2.Tell(new PublishingProtocol.SetReceiveMaximum(2));

        var p1 = MakeQos1(1);
        var p2 = MakeQos2(2);
        var p3 = MakeQos2(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        qos1.Tell(p1, probe);
        var c1 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c1).Value.Should().Be(1);

        qos2.Tell(p2, probe);
        var c2 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c2).Value.Should().Be(2);

        // p3 must be buffered in QoS 2 actor (quota full)
        qos2.Tell(p3, probe);
        await Task.Delay(80, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("p3 must be buffered in QoS 2 actor");

        // ACK p1 — QoS 1 actor's own buffer is empty → notifies QoS 2 sibling → p3 promoted
        qos1.Tell(p1.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var promoted = await channel.Reader.ReadAsync(cts.Token);
        IdOf(promoted).Value.Should().Be(3, "QoS 2 p3 should be promoted via cross-QoS sibling notification");

        // Clean up
        qos2.Tell(p2.ToPubRec(), probe);
        var pubRel2 = await channel.Reader.ReadAsync(cts.Token);
        pubRel2.PacketType.Should().Be(MqttPacketType.PubRel);
        qos2.Tell(new PubCompPacket { PacketId = p2.PacketId }, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        qos2.Tell(p3.ToPubRec(), probe);
        var pubRel3 = await channel.Reader.ReadAsync(cts.Token);
        pubRel3.PacketType.Should().Be(MqttPacketType.PubRel);
        qos2.Tell(new PubCompPacket { PacketId = p3.PacketId }, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public async Task SharedQuota_cross_Qos_drain_when_Qos2_frees_slot_and_Qos1_has_buffer()
    {
        // ReceiveMaximum = 2.
        // Step 1: send p1 (QoS1) and p2 (QoS2) → quota full.
        // Step 2: send p3 (QoS1) → buffered in QoS 1 actor.
        // Step 3: Complete p2 (PubRec + PubComp) → QoS 2 has no buffer → notifies QoS 1 → p3 promoted.
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var (qos1, qos2, _) = CreateActors(channel);

        qos1.Tell(new PublishingProtocol.SetReceiveMaximum(2));
        qos2.Tell(new PublishingProtocol.SetReceiveMaximum(2));

        var p1 = MakeQos1(1);
        var p2 = MakeQos2(2);
        var p3 = MakeQos1(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        qos1.Tell(p1, probe);
        var c1 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c1).Value.Should().Be(1);

        qos2.Tell(p2, probe);
        var c2 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c2).Value.Should().Be(2);

        // p3 must be buffered in QoS 1 actor (quota full)
        qos1.Tell(p3, probe);
        await Task.Delay(80, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("p3 must be buffered in QoS 1 actor");

        // Complete the QoS 2 exchange for p2 — PubComp frees the slot.
        qos2.Tell(p2.ToPubRec(), probe);
        var pubRelInChannel = await channel.Reader.ReadAsync(cts.Token);
        pubRelInChannel.PacketType.Should().Be(MqttPacketType.PubRel);

        qos2.Tell(new PubCompPacket { PacketId = p2.PacketId }, probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        // QoS 2 actor had an empty buffer → notified QoS 1 sibling → p3 promoted
        var promoted = await channel.Reader.ReadAsync(cts.Token);
        IdOf(promoted).Value.Should().Be(3, "QoS 1 p3 should be promoted via cross-QoS sibling notification");

        // Clean up
        qos1.Tell(p1.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        qos1.Tell(p3.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);
    }

    [Fact]
    public async Task SharedQuota_five_interleaved_publishes_respect_receive_maximum_of_three()
    {
        // ReceiveMaximum = 3.
        // Send p1 (QoS1), p2 (QoS2), p3 (QoS1) sequentially waiting for each to claim a slot
        // so that exactly 3 are in-flight. Then send p4 (QoS2) and p5 (QoS1) which must be buffered.
        // ACK p1 → p4 or p5 promoted; ACK p3 → the other promoted.
        var probe = CreateTestProbe();
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var (qos1, qos2, _) = CreateActors(channel);

        const ushort max = 3;
        qos1.Tell(new PublishingProtocol.SetReceiveMaximum(max));
        qos2.Tell(new PublishingProtocol.SetReceiveMaximum(max));

        var p1 = MakeQos1(1);
        var p2 = MakeQos2(2);
        var p3 = MakeQos1(3);
        var p4 = MakeQos2(4);
        var p5 = MakeQos1(5);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Fill the quota: 3 slots
        qos1.Tell(p1, probe);
        var c1 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c1).Value.Should().Be(1);

        qos2.Tell(p2, probe);
        var c2 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c2).Value.Should().Be(2);

        qos1.Tell(p3, probe);
        var c3 = await channel.Reader.ReadAsync(cts.Token);
        IdOf(c3).Value.Should().Be(3);

        // Quota full; send p4 and p5 — both must be buffered
        qos2.Tell(p4, probe);
        qos1.Tell(p5, probe);

        await Task.Delay(100, cts.Token);
        channel.Reader.TryRead(out _).Should().BeFalse("p4 and p5 should be buffered; quota (3) is full");

        // ACK p1 → slot freed; one buffered packet promoted
        qos1.Tell(p1.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var promoted1 = await channel.Reader.ReadAsync(cts.Token);
        promoted1.Should().NotBeNull("a buffered packet should be promoted after p1's slot is freed");

        // ACK p3 → slot freed; the other buffered packet promoted
        qos1.Tell(p3.ToPubAck(), probe);
        await probe.ExpectMsgAsync<PublishingProtocol.PublishSuccess>(cancellationToken: cts.Token);

        var promoted2 = await channel.Reader.ReadAsync(cts.Token);
        promoted2.Should().NotBeNull("the second buffered packet should be promoted after p3's slot is freed");

        // Two promoted packets should cover p4 and p5
        var promotedIds = new[] { IdOf(promoted1).Value, IdOf(promoted2).Value };
        promotedIds.Should().BeEquivalentTo(new ushort[] { 4, 5 },
            "both p4 and p5 must eventually be promoted");
    }
}
