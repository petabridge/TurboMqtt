// -----------------------------------------------------------------------
// <copyright file="Mqtt311RoundtripPropertyTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// FsCheck property tests that verify every MQTT 3.1.1 packet type roundtrips
/// correctly through <see cref="Mqtt311Encoder"/> → <see cref="Mqtt311Decoder"/>.
///
/// Implements Task 2.3: "Add roundtrip encode/decode property tests for all packet types".
/// </summary>
public class Mqtt311RoundtripPropertyTests
{
    private static (bool success, IReadOnlyList<MqttPacket> packets) EncodeAndDecode(MqttPacket packet)
    {
        var decoder = new Mqtt311Decoder();
        var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
        var buffer = new Memory<byte>(new byte[estimatedSize.TotalSize]);
        Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);
        var success = decoder.TryDecode(buffer, out var decoded);
        return (success, decoded);
    }

    [FsCheck.Xunit.Property]
    public Property ConnectPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.ConnectPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("CONNECT packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");

            var p = (ConnectPacket)orig;
            var d = (ConnectPacket)decoded[0];

            // Exclude Will.Message (ReadOnlyMemory<byte>), compared separately below.
            d.Should().BeEquivalentTo(p, options => options
                .Excluding(x => x.Will!.Message));

            if (p.Will != null)
                d.Will!.Message.ToArray().Should().BeEquivalentTo(p.Will.Message.ToArray(),
                    "Will payload bytes must match");

            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property ConnAckPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.ConnAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("CONNACK packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PublishPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PublishPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBLISH packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");

            var p = (PublishPacket)orig;
            var d = (PublishPacket)decoded[0];

            d.QualityOfService.Should().Be(p.QualityOfService);
            d.Duplicate.Should().Be(p.Duplicate);
            d.RetainRequested.Should().Be(p.RetainRequested);
            d.TopicName.Should().Be(p.TopicName);

            // PacketId is not encoded for QoS 0
            if (p.QualityOfService != QualityOfService.AtMostOnce)
                d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip for QoS 1/2");

            d.Payload.ToArray().Should().BeEquivalentTo(p.Payload.ToArray(),
                "Payload bytes must match");

            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PubAckPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBACK packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PubRecPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PubRecPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBREC packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PubRelPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PubRelPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBREL packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PubCompPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PubCompPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBCOMP packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property SubscribePacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.SubscribePacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("SUBSCRIBE packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");

            var p = (SubscribePacket)orig;
            var d = (SubscribePacket)decoded[0];

            d.PacketId.Should().Be(p.PacketId);
            // SubscriptionIdentifier is MQTT 5.0 only and not encoded in MQTT 3.1.1.
            // Topics comparison includes QoS (the only subscription option in MQTT 3.1.1).
            d.Topics.Should().BeEquivalentTo(p.Topics,
                options => options
                    .Excluding(t => t.Options.NoLocal)          // MQTT 5.0 only
                    .Excluding(t => t.Options.RetainAsPublished) // MQTT 5.0 only
                    .Excluding(t => t.Options.RetainHandling),  // MQTT 5.0 only
                "Topic subscriptions must roundtrip (QoS only in MQTT 3.1.1)");

            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property SubAckPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.SubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("SUBACK packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property UnsubscribePacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.UnsubscribePacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("UNSUBSCRIBE packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property UnsubAckPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.UnsubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("UNSUBACK packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].Should().BeEquivalentTo(orig);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PingReqPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PingReqPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PINGREQ packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].PacketType.Should().Be(MqttPacketType.PingReq);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property PingRespPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PingRespPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PINGRESP packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].PacketType.Should().Be(MqttPacketType.PingResp);
            return true;
        });
    }

    [FsCheck.Xunit.Property]
    public Property DisconnectPacketRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.DisconnectPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("DISCONNECT packet decoding should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded packet");
            decoded[0].PacketType.Should().Be(MqttPacketType.Disconnect);
            return true;
        });
    }

    /// <summary>
    /// Combined roundtrip property test using all 14 MQTT 3.1.1 packet types via
    /// <see cref="PacketGenerators.PacketArb"/>. Encodes a random packet, decodes it,
    /// and asserts the packet type is preserved.
    /// </summary>
    [FsCheck.Xunit.Property]
    public Property AllPacketTypesRoundtrip()
    {
        return Prop.ForAll(PacketGenerators.PacketArb(), packet =>
        {
            var (success, decoded) = EncodeAndDecode(packet);
            success.Should().BeTrue($"packet type {packet.PacketType} should decode successfully");
            decoded.Count.Should().Be(1, $"expected exactly 1 decoded packet for {packet.PacketType}");
            decoded[0].PacketType.Should().Be(packet.PacketType,
                $"packet type must be preserved after roundtrip");
            return true;
        })
        .Classify(true, "all packet types roundtrip successfully");
    }
}
