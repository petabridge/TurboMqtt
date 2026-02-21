// -----------------------------------------------------------------------
// <copyright file="Mqtt5RoundtripPropertyTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// FsCheck property tests verifying that every MQTT 5.0 packet type roundtrips
/// correctly through <see cref="Mqtt5Encoder"/> → <see cref="Mqtt5Decoder"/>.
///
/// Implements Task 3.4: "Add FsCheck generators and roundtrip property tests for MQTT 5.0".
/// </summary>
public class Mqtt5RoundtripPropertyTests
{
    // ── Shared encode/decode helpers ─────────────────────────────────────────

    private static (bool success, IReadOnlyList<MqttPacket> packets) EncodeAndDecode(MqttPacket packet)
    {
        var decoder = new Mqtt5Decoder();
        var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
        var buffer = new Memory<byte>(new byte[estimatedSize.TotalSize]);
        Mqtt5Encoder.EncodePacket(packet, ref buffer, estimatedSize);
        var ro = new ReadOnlyMemory<byte>(buffer.ToArray());
        var success = decoder.TryDecode(ro, out var decoded);
        return (success, decoded);
    }

    /// <summary>
    /// Asserts <paramref name="decoded"/> memory bytes match <paramref name="original"/> bytes.
    /// When <paramref name="original"/> is null or empty, asserts <paramref name="decoded"/> is also null or empty.
    /// </summary>
    private static void AssertMemoryEqual(ReadOnlyMemory<byte>? decoded, ReadOnlyMemory<byte>? original, string fieldName)
    {
        if (!original.HasValue || original.Value.IsEmpty)
        {
            // Encoder skips null/empty binary data; decoder leaves the field unset (null or empty).
            var isEmpty = !decoded.HasValue || decoded.Value.IsEmpty;
            isEmpty.Should().BeTrue($"{fieldName} should be null/empty when not encoded");
        }
        else
        {
            decoded.HasValue.Should().BeTrue($"{fieldName} should be present after roundtrip");
            decoded!.Value.ToArray().Should().BeEquivalentTo(
                original.Value.ToArray(),
                $"{fieldName} bytes must match");
        }
    }

    // ── CONNECT ─────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5ConnectPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5ConnectPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("CONNECT encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded CONNECT packet");

            var p = (ConnectPacket)orig;
            var d = (ConnectPacket)decoded[0];

            // Compare all fields except memory-typed ones (compared below).
            d.Should().BeEquivalentTo(p, options => options
                .Excluding(x => x.Will!.Message)
                .Excluding(x => x.AuthenticationData));

            // Will payload: compare byte content
            if (p.Will != null)
                d.Will!.Message.ToArray().Should().BeEquivalentTo(
                    p.Will.Message.ToArray(),
                    "Will payload bytes must match");

            // AuthenticationData: compare byte content
            AssertMemoryEqual(d.AuthenticationData, p.AuthenticationData, "ConnectPacket.AuthenticationData");

            return true;
        });
    }

    // ── CONNACK ─────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5ConnAckPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5ConnAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("CONNACK encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded CONNACK packet");

            var p = (ConnAckPacket)orig;
            var d = (ConnAckPacket)decoded[0];

            d.Should().BeEquivalentTo(p, options => options
                .Excluding(x => x.AuthenticationData));

            AssertMemoryEqual(d.AuthenticationData, p.AuthenticationData, "ConnAckPacket.AuthenticationData");

            return true;
        });
    }

    // ── PUBLISH ─────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PublishPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PublishPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBLISH encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded PUBLISH packet");

            var p = (PublishPacket)orig;
            var d = (PublishPacket)decoded[0];

            // Compare structural fields; exclude memory fields, payload, and PacketId (checked separately).
            // PacketId is not encoded for QoS 0 — exclude it from structural equivalence to avoid false failures.
            d.Should().BeEquivalentTo(p, options => options
                .Excluding(x => x.Payload)
                .Excluding(x => x.CorrelationData)
                .Excluding(x => x.PacketId));

            // PacketId is only encoded for QoS 1 and 2
            if (p.QualityOfService != QualityOfService.AtMostOnce)
                d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip for QoS 1/2");

            // Payload bytes
            d.Payload.ToArray().Should().BeEquivalentTo(p.Payload.ToArray(),
                "Payload bytes must match");

            // CorrelationData: nullable binary data
            AssertMemoryEqual(d.CorrelationData, p.CorrelationData, "PublishPacket.CorrelationData");

            return true;
        });
    }

    // ── PUBACK ──────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PubAckPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBACK encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded PUBACK packet");

            var p = (PubAckPacket)orig;
            var d = (PubAckPacket)decoded[0];

            d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip");
            d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip");

            // Compact form: Success + no props → ReasonString = null, UserProperties = null.
            // Non-compact: ReasonString and UserProperties roundtrip as-is.
            var isCompact = p.ReasonCode == MqttPubAckReasonCode.Success
                && string.IsNullOrEmpty(p.ReasonString)
                && (p.UserProperties == null || p.UserProperties.Count == 0);

            if (!isCompact)
            {
                d.ReasonString.Should().Be(p.ReasonString, "ReasonString must roundtrip in full form");
                if (p.UserProperties != null && p.UserProperties.Count > 0)
                    d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");
            }

            return true;
        });
    }

    // ── PUBREC ──────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PubRecPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubRecPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBREC encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded PUBREC packet");

            var p = (PubRecPacket)orig;
            var d = (PubRecPacket)decoded[0];

            d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip");

            // Compact form: null/Success + no props → encoder omits reason code, decoder returns null.
            var resolvedCode = p.ReasonCode ?? PubRecReasonCode.Success;
            var hasProps = !string.IsNullOrEmpty(p.ReasonString)
                || (p.UserProperties != null && p.UserProperties.Count > 0);
            var isCompact = resolvedCode == PubRecReasonCode.Success && !hasProps;

            if (!isCompact)
            {
                d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip in non-compact form");
                d.ReasonString.Should().Be(p.ReasonString, "ReasonString must roundtrip in non-compact form");
                if (p.UserProperties != null && p.UserProperties.Count > 0)
                    d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");
            }

            return true;
        });
    }

    // ── PUBREL ──────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PubRelPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubRelPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBREL encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded PUBREL packet");

            var p = (PubRelPacket)orig;
            var d = (PubRelPacket)decoded[0];

            d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip");

            // Compact form: null/Success + no props → encoder omits reason code, decoder returns null.
            var resolvedCode = p.ReasonCode ?? PubRelReasonCode.Success;
            var hasProps = !string.IsNullOrEmpty(p.ReasonString)
                || (p.UserProperties != null && p.UserProperties.Count > 0);
            var isCompact = resolvedCode == PubRelReasonCode.Success && !hasProps;

            if (!isCompact)
            {
                d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip in non-compact form");
                d.ReasonString.Should().Be(p.ReasonString, "ReasonString must roundtrip in non-compact form");
                if (p.UserProperties != null && p.UserProperties.Count > 0)
                    d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");
            }

            return true;
        });
    }

    // ── PUBCOMP ─────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PubCompPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubCompPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PUBCOMP encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded PUBCOMP packet");

            var p = (PubCompPacket)orig;
            var d = (PubCompPacket)decoded[0];

            d.PacketId.Should().Be(p.PacketId, "PacketId must roundtrip");

            // Compact form: null/Success + no props → encoder omits reason code, decoder returns null.
            var resolvedCode = p.ReasonCode ?? PubCompReasonCode.Success;
            var hasProps = !string.IsNullOrEmpty(p.ReasonString)
                || (p.UserProperties != null && p.UserProperties.Count > 0);
            var isCompact = resolvedCode == PubCompReasonCode.Success && !hasProps;

            if (!isCompact)
            {
                d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip in non-compact form");
                d.ReasonString.Should().Be(p.ReasonString, "ReasonString must roundtrip in non-compact form");
                if (p.UserProperties != null && p.UserProperties.Count > 0)
                    d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");
            }

            return true;
        });
    }

    // ── SUBSCRIBE ───────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5SubscribePacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5SubscribePacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("SUBSCRIBE encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded SUBSCRIBE packet");
            decoded[0].Should().BeEquivalentTo(orig,
                "SUBSCRIBE packet including V5 subscription options must roundtrip");
            return true;
        });
    }

    // ── SUBACK ──────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5SubAckPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5SubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("SUBACK encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded SUBACK packet");
            decoded[0].Should().BeEquivalentTo(orig, "all SUBACK fields must roundtrip");
            return true;
        });
    }

    // ── UNSUBSCRIBE ─────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5UnsubscribePacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5UnsubscribePacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("UNSUBSCRIBE encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded UNSUBSCRIBE packet");
            decoded[0].Should().BeEquivalentTo(orig, "all UNSUBSCRIBE fields must roundtrip");
            return true;
        });
    }

    // ── UNSUBACK ────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5UnsubAckPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5UnsubAckPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("UNSUBACK encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded UNSUBACK packet");
            decoded[0].Should().BeEquivalentTo(orig, "all UNSUBACK fields must roundtrip");
            return true;
        });
    }

    // ── PINGREQ / PINGRESP ───────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PingReqPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PingReqPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PINGREQ encode/decode should succeed");
            decoded.Count.Should().Be(1);
            decoded[0].PacketType.Should().Be(MqttPacketType.PingReq);
            return true;
        });
    }

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5PingRespPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PingRespPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("PINGRESP encode/decode should succeed");
            decoded.Count.Should().Be(1);
            decoded[0].PacketType.Should().Be(MqttPacketType.PingResp);
            return true;
        });
    }

    // ── DISCONNECT ───────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5DisconnectPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5DisconnectPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("DISCONNECT encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded DISCONNECT packet");

            var p = (DisconnectPacket)orig;
            var d = (DisconnectPacket)decoded[0];

            // Compact form: null ReasonCode, no properties → decoded ReasonCode = null.
            // Non-compact: all fields roundtrip.
            d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip");
            d.SessionExpiryInterval.Should().Be(p.SessionExpiryInterval, "SessionExpiryInterval must roundtrip");
            d.ServerReference.Should().Be(p.ServerReference, "ServerReference must roundtrip");

            if (p.UserProperties != null && p.UserProperties.Count > 0)
                d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");

            return true;
        });
    }

    // ── AUTH ────────────────────────────────────────────────────────────────

    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property Mqtt5AuthPacketRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5AuthPacketArb(), orig =>
        {
            var (success, decoded) = EncodeAndDecode(orig);
            success.Should().BeTrue("AUTH encode/decode should succeed");
            decoded.Count.Should().Be(1, "expected exactly one decoded AUTH packet");

            var p = (AuthPacket)orig;
            var d = (AuthPacket)decoded[0];

            d.AuthenticationMethod.Should().Be(p.AuthenticationMethod, "AuthenticationMethod must roundtrip");
            d.ReasonCode.Should().Be(p.ReasonCode, "ReasonCode must roundtrip");
            d.ReasonString.Should().Be(p.ReasonString, "ReasonString must roundtrip");

            // AuthenticationData: non-nullable ReadOnlyMemory<byte>
            d.AuthenticationData.ToArray().Should().BeEquivalentTo(
                p.AuthenticationData.ToArray(),
                "AuthenticationData bytes must match");

            if (p.UserProperties != null && p.UserProperties.Count > 0)
                d.UserProperties.Should().BeEquivalentTo(p.UserProperties, "UserProperties must roundtrip");

            return true;
        });
    }

    // ── Combined: all 15 packet types ───────────────────────────────────────

    /// <summary>
    /// Combined roundtrip property test using all 15 MQTT 5.0 packet types.
    /// Encodes a random packet, decodes it, and asserts the packet type is preserved.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 1000)]
    public Property AllMqtt5PacketTypesRoundtrip()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PacketArb(), packet =>
        {
            var (success, decoded) = EncodeAndDecode(packet);
            success.Should().BeTrue($"MQTT 5.0 packet type {packet.PacketType} should encode/decode successfully");
            decoded.Count.Should().Be(1, $"expected exactly 1 decoded packet for {packet.PacketType}");
            decoded[0].PacketType.Should().Be(packet.PacketType,
                "packet type must be preserved after roundtrip");
            return true;
        })
        .Classify(true, "all 15 MQTT 5.0 packet types roundtrip successfully");
    }
}
