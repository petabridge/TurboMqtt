// -----------------------------------------------------------------------
// <copyright file="Mqtt5DecoderErrorPathSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Implements Task 3.4 "Error path tests" for <see cref="Mqtt5Decoder"/>.
/// Covers malformed property lengths, unknown property identifiers, oversized packets,
/// partial-frame reassembly, and MQTT 5.0-specific error conditions.
/// </summary>
public class Mqtt5DecoderErrorPathSpecs
{
    // ── Helper ───────────────────────────────────────────────────────────────

    /// <summary>Encodes a packet with Mqtt5Encoder and returns the full byte buffer.</summary>
    private static ReadOnlyMemory<byte> Mqtt5Encode(MqttPacket packet)
    {
        var estimated = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
        Memory<byte> buffer = new byte[estimated.TotalSize];
        Mqtt5Encoder.EncodePacket(packet, ref buffer, estimated);
        return buffer;
    }

    // ── 1. Unknown property identifiers ──────────────────────────────────────

    /// <summary>
    /// A PUBLISH packet containing an unknown property identifier (0xFE) in its
    /// properties section must be rejected with <see cref="MqttDecoderException"/>.
    /// Per MQTT 5.0 §2.2.2.2, receiving an unknown Property Identifier is a Protocol Error.
    /// </summary>
    [Fact]
    public void Decoder_Publish_UnknownPropertyIdentifier_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();

        // Manually crafted MQTT 5.0 PUBLISH packet (QoS=0):
        //   Fixed header : 0x30 (PUBLISH, DUP=0, QoS=0, RETAIN=0)
        //   Remaining len: 0x05 = 5
        //   Topic        : { 0x00, 0x01, 0x61 } = length 1, "a"
        //   Props length : 0x01 (VBI = 1 byte of properties follow)
        //   Props        : { 0xFE } = unknown identifier 0xFE
        var bytes = new byte[] { 0x30, 0x05, 0x00, 0x01, 0x61, 0x01, 0xFE };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "property identifier 0xFE is not defined in MQTT 5.0 Table 2-4 and must be rejected");
    }

    /// <summary>
    /// An AUTH packet containing an unknown property identifier (0xFE) in its
    /// properties section must be rejected with <see cref="MqttDecoderException"/>.
    /// </summary>
    [Fact]
    public void Decoder_Auth_UnknownPropertyIdentifier_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();

        // Manually crafted MQTT 5.0 AUTH packet:
        //   Fixed header : 0xF0 (AUTH)
        //   Remaining len: 0x03 = 3
        //   Reason code  : 0x00 (Success)
        //   Props length : 0x01 (1 byte follows)
        //   Props        : { 0xFE } = unknown identifier
        var bytes = new byte[] { 0xF0, 0x03, 0x00, 0x01, 0xFE };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "property identifier 0xFE is not defined for AUTH packets and must be rejected");
    }

    /// <summary>
    /// A SUBSCRIBE packet containing an unknown property identifier in its
    /// properties section must be rejected with <see cref="MqttDecoderException"/>.
    /// </summary>
    [Fact]
    public void Decoder_Subscribe_UnknownPropertyIdentifier_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();

        // Manually crafted MQTT 5.0 SUBSCRIBE packet:
        //   Fixed header   : 0x82 (SUBSCRIBE)
        //   Remaining len  : 0x0A = 10
        //   Packet ID      : { 0x00, 0x01 }
        //   Props length   : 0x01 (1 byte of properties)
        //   Props          : { 0xFE } = unknown identifier
        //   Topic filter   : { 0x00, 0x03, 0x61, 0x2F, 0x62 } = "a/b"
        //   Sub options    : 0x01 (QoS 1)
        var bytes = new byte[]
        {
            0x82, 0x0A,                         // SUBSCRIBE, remaining=10
            0x00, 0x01,                          // Packet ID=1
            0x01, 0xFE,                          // Props length=1, unknown prop 0xFE
            0x00, 0x03, 0x61, 0x2F, 0x62,       // topic "a/b"
            0x01                                 // QoS=1
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "property identifier 0xFE is not defined for SUBSCRIBE packets and must be rejected");
    }

    // ── 2. Malformed property lengths (invalid VBI) ───────────────────────────

    /// <summary>
    /// A properties section VBI that spans more than 4 bytes is malformed per
    /// MQTT 5.0 §1.5.5. <see cref="Mqtt5Decoder"/> must throw <see cref="MqttDecoderException"/>
    /// (not silently discard or crash).
    /// </summary>
    [Fact]
    public void Decoder_Publish_MalformedPropertiesVbi_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();

        // Manually crafted MQTT 5.0 PUBLISH packet with a 5-byte VBI in the properties section:
        //   Fixed header : 0x30 (PUBLISH QoS=0)
        //   Remaining len: 0x08 = 8  (3 topic bytes + 5 malformed VBI bytes)
        //   Topic        : { 0x00, 0x01, 0x61 } = "a"
        //   Props VBI    : { 0x80, 0x80, 0x80, 0x80, 0x01 } = 5-byte VBI (invalid per spec)
        var bytes = new byte[]
        {
            0x30, 0x08,
            0x00, 0x01, 0x61,                    // topic "a"
            0x80, 0x80, 0x80, 0x80, 0x01         // malformed 5-byte VBI
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "a 5-byte VBI in the properties section is malformed and must be rejected");
    }

    /// <summary>
    /// A SUBSCRIBE packet whose properties section VBI spans more than 4 bytes
    /// must be rejected with <see cref="MqttDecoderException"/>.
    /// </summary>
    [Fact]
    public void Decoder_Subscribe_MalformedPropertiesVbi_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();

        // Manually crafted MQTT 5.0 SUBSCRIBE:
        //   Fixed header : 0x82
        //   Remaining len: 0x09 = 9  (2 packet ID + 5 malformed VBI + 2 more to satisfy remaining)
        //   But with only malformed VBI after packet ID the decoder throws before reading topics.
        //   Remaining = 2 (pkt ID) + 5 (malformed VBI) = 7 → 0x07
        var bytes = new byte[]
        {
            0x82, 0x07,
            0x00, 0x01,                          // Packet ID=1
            0x80, 0x80, 0x80, 0x80, 0x01         // malformed 5-byte VBI for props length
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "a 5-byte VBI in the SUBSCRIBE properties section is malformed and must be rejected");
    }

    // ── 3. Oversized packets ─────────────────────────────────────────────────

    /// <summary>
    /// A packet header claiming the maximum MQTT remaining length (268,435,455 bytes)
    /// but providing no payload must cause the decoder to buffer the partial frame
    /// and return false — it must not crash or throw.
    /// </summary>
    [Fact]
    public void Decoder_MaxRemainingLength_InsufficientData_ReturnsFalse()
    {
        var decoder = new Mqtt5Decoder();

        // PUBLISH fixed header + VBI encoding 268435455 (0xFF 0xFF 0xFF 0x7F).
        // This declares 268435455 bytes of body but provides none.
        var bytes = new byte[] { 0x30, 0xFF, 0xFF, 0xFF, 0x7F };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeFalse(
            "the declared body (268435455 bytes) has not arrived yet");
        packets.Should().BeEmpty();
    }

    // ── 4. Truncated (partial frame) delivery ─────────────────────────────────

    /// <summary>
    /// A MQTT 5.0 PUBLISH packet fragmented across two TCP segments must be
    /// correctly reassembled by <see cref="Mqtt5Decoder"/>.
    /// </summary>
    [Fact]
    public void Decoder_TruncatedMqtt5Publish_ReassemblesCorrectly()
    {
        var decoder = new Mqtt5Decoder();

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "v5/sensors/temp")
        {
            PacketId = 42,
            Payload = new ReadOnlyMemory<byte>(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE })
        };

        var encoded = Mqtt5Encode(packet);

        // Split: first delivery gets all but the last 3 payload bytes
        var frame1 = encoded[..^3];
        var frame2 = encoded[^3..];

        var result1 = decoder.TryDecode(frame1, out var packets1);
        result1.Should().BeFalse("the MQTT 5.0 packet is not yet complete");
        packets1.Should().BeEmpty();

        var result2 = decoder.TryDecode(frame2, out var packets2);
        result2.Should().BeTrue("the MQTT 5.0 packet is now complete");
        packets2.Count.Should().Be(1, "exactly one PUBLISH packet was sent");

        var decoded = (PublishPacket)packets2[0];
        decoded.TopicName.Should().Be(packet.TopicName);
        decoded.QualityOfService.Should().Be(packet.QualityOfService);
        decoded.PacketId.Should().Be(packet.PacketId);
        decoded.Payload.ToArray().Should().BeEquivalentTo(packet.Payload.ToArray(),
            "all payload bytes must be preserved after reassembly");
    }

    /// <summary>
    /// A MQTT 5.0 CONNECT packet fragmented across three TCP segments must be
    /// correctly reassembled by <see cref="Mqtt5Decoder"/>.
    /// </summary>
    [Fact]
    public void Decoder_TruncatedMqtt5Connect_ReassemblesCorrectly()
    {
        var decoder = new Mqtt5Decoder();

        var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
        {
            ClientId = "v5-device-001",
            KeepAliveSeconds = 30,
            Flags = new ConnectFlags { CleanSession = true }
        };

        var encoded = Mqtt5Encode(packet);

        // Split into 3 roughly equal fragments
        var split1 = encoded.Length / 3;
        var split2 = 2 * encoded.Length / 3;

        var frame1 = encoded[..split1];
        var frame2 = encoded[split1..split2];
        var frame3 = encoded[split2..];

        decoder.TryDecode(frame1, out var p1);
        decoder.TryDecode(frame2, out var p2);
        decoder.TryDecode(frame3, out var p3);

        var allPackets = p1.Concat(p2).Concat(p3).ToList();
        allPackets.Count.Should().Be(1, "all fragments of one CONNECT must reassemble");

        var decoded = (ConnectPacket)allPackets[0];
        decoded.ClientId.Should().Be(packet.ClientId);
        decoded.KeepAliveSeconds.Should().Be(packet.KeepAliveSeconds);
        decoded.ProtocolVersion.Should().Be(MqttProtocolVersion.V5_0);
    }

    /// <summary>
    /// A MQTT 5.0 AUTH packet fed one byte at a time must be correctly reassembled.
    /// </summary>
    [Fact]
    public void Decoder_TruncatedMqtt5Auth_ByteByByteReassembly()
    {
        var decoder = new Mqtt5Decoder();

        var packet = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication)
        {
            AuthenticationData = new ReadOnlyMemory<byte>(new byte[] { 0x01, 0x02, 0x03 })
        };

        var encoded = Mqtt5Encode(packet);

        var collectedPackets = new List<MqttPacket>();
        for (var i = 0; i < encoded.Length; i++)
        {
            var fragment = encoded.Slice(i, 1);
            if (decoder.TryDecode(fragment, out var partial))
                collectedPackets.AddRange(partial);
        }

        collectedPackets.Count.Should().Be(1, "all byte fragments of one AUTH must reassemble");
        collectedPackets[0].PacketType.Should().Be(MqttPacketType.Auth);
        var authDecoded = (AuthPacket)collectedPackets[0];
        authDecoded.AuthenticationMethod.Should().Be("SCRAM-SHA-256");
    }

    // ── 5. Large payload roundtrip ────────────────────────────────────────────

    /// <summary>
    /// Verifies that the MQTT 5.0 encoder and decoder correctly handle a large
    /// payload (1 MB) without corruption.
    /// </summary>
    [Fact]
    public void Decoder_LargePayload_1MB_RoundtripSucceeds()
    {
        var payloadSize = 1 * 1024 * 1024;
        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++)
            payload[i] = (byte)(i & 0xFF);

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "v5/bench/large")
        {
            PacketId = 1,
            Payload = new ReadOnlyMemory<byte>(payload)
        };

        var decoder = new Mqtt5Decoder();
        var encoded = Mqtt5Encode(packet);
        var result = decoder.TryDecode(encoded, out var decoded);

        result.Should().BeTrue("a 1 MB payload is within the MQTT maximum");
        decoded.Count.Should().Be(1);

        var p = (PublishPacket)decoded[0];
        p.TopicName.Should().Be(packet.TopicName);
        p.Payload.ToArray().Should().BeEquivalentTo(payload,
            "all 1 MB of payload bytes must be preserved");
    }

    // ── 6. Fixed header edge cases ────────────────────────────────────────────

    /// <summary>
    /// Packet type 0 is RESERVED in all MQTT versions. The MQTT 5.0 decoder
    /// (which inherits from <see cref="Mqtt311Decoder"/>) must also reject it.
    /// </summary>
    [Fact]
    public void Decoder_ReservedPacketType0_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt5Decoder();
        // Fixed header 0x00 (type=0, reserved), remaining length 0
        var bytes = new byte[] { 0x00, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "packet type 0 is reserved and must be rejected by the MQTT 5.0 decoder");
    }

    /// <summary>
    /// A buffer containing only the fixed header byte (1 byte) is insufficient
    /// even for the length field. The MQTT 5.0 decoder must buffer and return false.
    /// </summary>
    [Fact]
    public void Decoder_OnlyFixedHeaderByte_ReturnsFalse()
    {
        var decoder = new Mqtt5Decoder();
        var bytes = new byte[] { 0x30 }; // PUBLISH fixed header, no length byte
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeFalse("the packet header is incomplete — missing remaining length");
        packets.Should().BeEmpty();
    }

    // ── 7. DISCONNECT compact form ────────────────────────────────────────────

    /// <summary>
    /// The MQTT 5.0 compact DISCONNECT form (Remaining Length = 0) must decode
    /// to a <see cref="DisconnectPacket"/> with a null <see cref="DisconnectPacket.ReasonCode"/>.
    /// </summary>
    [Fact]
    public void Decoder_Disconnect_CompactForm_DecodesWithNullReasonCode()
    {
        var decoder = new Mqtt5Decoder();

        // Compact DISCONNECT: fixed header 0xE0 + remaining length 0x00
        var bytes = new byte[] { 0xE0, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeTrue("compact DISCONNECT is a valid MQTT 5.0 packet");
        packets.Count.Should().Be(1);
        var d = (DisconnectPacket)packets[0];
        d.ReasonCode.Should().BeNull(
            "compact form (Remaining Length=0) means NormalDisconnection with no properties; " +
            "ReasonCode is null by convention in DisconnectPacket.Instance");
    }
}
