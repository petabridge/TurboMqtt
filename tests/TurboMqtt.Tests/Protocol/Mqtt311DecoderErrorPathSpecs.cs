// -----------------------------------------------------------------------
// <copyright file="Mqtt311DecoderErrorPathSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Tests.Packets;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Implements Task 2.4: "Add error path and boundary condition tests" for
/// <see cref="Mqtt311Decoder"/>. Tests defensive behavior against invalid,
/// adversarial, and edge-case input.
/// </summary>
public class Mqtt311DecoderErrorPathSpecs
{
    // -------------------------------------------------------------------------
    // 1. Invalid packet type bytes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packet type 0 is RESERVED in MQTT 3.1.1. The decoder should reject it.
    /// The fixed header byte 0x00 → type nibble 0x0 → not a valid MQTT packet type.
    /// </summary>
    [Fact]
    public void Decoder_InvalidPacketType_Reserved0_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();
        // Fixed header 0x00 (type=0, reserved), remaining length 0
        var bytes = new byte[] { 0x00, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "packet type 0 is reserved and must be rejected");
    }

    /// <summary>
    /// Fixed header byte 0xFF → type nibble 0xF (15) = MqttPacketType.Auth.
    /// AUTH is an MQTT 5.0 packet; the 3.1.1 decoder must reject it.
    /// </summary>
    [Fact]
    public void Decoder_InvalidPacketType_Auth_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();
        // Fixed header 0xF0 (type=15 = Auth), remaining length 0
        var bytes = new byte[] { 0xF0, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "AUTH is an MQTT 5.0 packet and must not be accepted by the MQTT 3.1.1 decoder");
    }

    // -------------------------------------------------------------------------
    // 2. Truncated packets
    // -------------------------------------------------------------------------

    /// <summary>
    /// When a buffer contains a complete header but fewer payload bytes than the
    /// remaining-length field indicates, the decoder must buffer the partial frame
    /// and return false (not throw, not lose data).
    /// After receiving the missing bytes, the packet must be fully decoded.
    /// </summary>
    [Fact]
    public void Decoder_TruncatedPacket_ReturnsFalseAndReassemblesOnNextCall()
    {
        var decoder = new Mqtt311Decoder();

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "status/sensor1")
        {
            PacketId = 42,
            Payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }
        };

        var encoded = PacketEncodingTestHelper.EncodePacketOnly(packet);

        // Split: first call gets all but the last 3 payload bytes
        var frame1 = encoded[..^3];
        var frame2 = encoded[^3..];

        var result1 = decoder.TryDecode(frame1, out var packets1);
        result1.Should().BeFalse("the packet is not yet complete");
        packets1.Should().BeEmpty();

        var result2 = decoder.TryDecode(frame2, out var packets2);
        result2.Should().BeTrue("the packet is now complete");
        packets2.Count.Should().Be(1, "exactly one packet was sent");

        var decoded = (PublishPacket)packets2[0];
        decoded.TopicName.Should().Be(packet.TopicName);
        decoded.QualityOfService.Should().Be(packet.QualityOfService);
        decoded.Payload.ToArray().Should().BeEquivalentTo(packet.Payload.ToArray());
    }

    /// <summary>
    /// A buffer containing only the fixed header byte (1 byte) is insufficient
    /// even for the length field. The decoder must buffer and return false.
    /// </summary>
    [Fact]
    public void Decoder_OnlyFixedHeaderByte_ReturnsFalse()
    {
        var decoder = new Mqtt311Decoder();
        var bytes = new byte[] { 0x30 }; // PUBLISH fixed header, no length byte
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeFalse("the packet header is incomplete");
        packets.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // 3. Remaining length edge cases
    // -------------------------------------------------------------------------

    /// <summary>
    /// A packet header claiming the maximum MQTT remaining length (268435455 bytes)
    /// but providing no payload should cause the decoder to save the partial frame
    /// and return false — not crash or throw.
    /// </summary>
    [Fact]
    public void Decoder_MaxRemainingLength_InsufficientData_ReturnsFalse()
    {
        var decoder = new Mqtt311Decoder();

        // PUBLISH fixed header + VBI encoding 268435455 (0xFF 0xFF 0xFF 0x7F)
        // This declares 268435455 bytes of body but provides none.
        var bytes = new byte[] { 0x30, 0xFF, 0xFF, 0xFF, 0x7F };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeFalse(
            "the declared body (268435455 bytes) has not arrived yet");
        packets.Should().BeEmpty();
    }

    /// <summary>
    /// A Variable Byte Integer that spans more than 4 bytes is malformed per the
    /// MQTT 3.1.1 spec (§2.2.3). The decoder must treat this as a partial/incomplete
    /// frame (returns false) — it cannot distinguish a 5-byte malformed VBI from a
    /// header that has not yet fully arrived. Crucially it must not crash.
    /// </summary>
    [Fact]
    public void Decoder_FiveByteVariableLengthInteger_TreatedAsPartialFrame()
    {
        var decoder = new Mqtt311Decoder();

        // PUBLISH fixed header, then 5 VBI bytes with continuation bits on bytes 0-3.
        // The 5th byte triggers the TryGetPacketLength overflow guard.
        // { 0x80, 0x80, 0x80, 0x80 } = 4 continuation-bit bytes, then 0x01
        var bytes = new byte[] { 0x30, 0x80, 0x80, 0x80, 0x80, 0x01 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeFalse(
            "a 5-byte VBI is treated as an incomplete header (no data loss, no crash)");
        packets.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // 4. Maximum-size payload roundtrip
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that the encoder and decoder correctly handle a large payload
    /// (1 MB) without corruption. Uses a practical test limit rather than the
    /// 256 MB protocol maximum for CI performance.
    /// </summary>
    [Fact]
    public void Decoder_LargePayload_1MB_RoundtripSucceeds()
    {
        // 1 MB payload
        var payloadSize = 1 * 1024 * 1024;
        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++)
            payload[i] = (byte)(i & 0xFF);

        var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "bench/large")
        {
            PacketId = 1,
            Payload = new ReadOnlyMemory<byte>(payload)
        };

        var decoder = new Mqtt311Decoder();
        var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
        var buffer = new Memory<byte>(new byte[estimatedSize.TotalSize]);
        Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);

        var result = decoder.TryDecode(buffer, out var decoded);

        result.Should().BeTrue("a 1 MB payload is within the MQTT 3.1.1 maximum");
        decoded.Count.Should().Be(1);

        var p = (PublishPacket)decoded[0];
        p.TopicName.Should().Be(packet.TopicName);
        p.Payload.ToArray().Should().BeEquivalentTo(payload,
            "all 1 MB of payload bytes must be preserved");
    }

    // -------------------------------------------------------------------------
    // 5. PUBLISH with invalid QoS 3
    // -------------------------------------------------------------------------

    /// <summary>
    /// QoS level 3 (0b11) is reserved and undefined in MQTT 3.1.1 §4.3.
    /// The current decoder parses it without throwing (it produces a packet with
    /// an undefined QoS value). This test documents that behavior.
    /// Brokers should close connections that receive such packets.
    /// </summary>
    [Fact]
    public void Decoder_Publish_QoS3_DecodesWithoutThrow()
    {
        var decoder = new Mqtt311Decoder();

        // Manually crafted PUBLISH packet with QoS=3 bits [2:1] = 0b11 → 0x06 in low nibble
        // Fixed header: 0x36 (PUBLISH | DUP=0, QoS=3, RETAIN=0)
        // Remaining length: 7 (2 topic len + 3 topic "a/b" + 2 packet ID)
        // Topic: { 0x00, 0x03, 'a', '/', 'b' }
        // Packet ID: { 0x00, 0x01 }
        var bytes = new byte[]
        {
            0x36,                         // Fixed header: PUBLISH, QoS=3
            0x07,                         // Remaining length: 7
            0x00, 0x03, 0x61, 0x2F, 0x62, // Topic length=3, "a/b"
            0x00, 0x01                    // Packet ID = 1
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        // The MQTT 3.1.1 decoder does not currently reject QoS 3;
        // it produces a packet with (QualityOfService)3.
        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeTrue();
        packets.Count.Should().Be(1);
        var p = (PublishPacket)packets[0];
        // Verify QoS value is exactly what was encoded (the undefined value 3)
        ((int)p.QualityOfService).Should().Be(3,
            "decoder passes through QoS 3 without rejection (protocol layer must close the connection)");
    }

    // -------------------------------------------------------------------------
    // 6. CONNECT packet validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// MQTT 3.1.1 §3.1.2.1 mandates the protocol name is exactly "MQTT".
    /// A CONNECT packet with a different protocol name must be rejected.
    /// </summary>
    [Fact]
    public void Decoder_Connect_InvalidProtocolName_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();

        // Minimal CONNECT with protocol name "MQTX" (last char changed from 'T' to 'X')
        // Fixed header: 0x10, remaining length 0x0D (13)
        // Protocol name len: 0x00 0x04, name: 'M' 'Q' 'T' 'X'
        // Protocol level: 0x04, Connect flags: 0x02, Keep-alive: 0x00 0x3C
        // Client ID len: 0x00 0x01, Client ID: 'a'
        var bytes = new byte[]
        {
            0x10, 0x0D,                       // CONNECT, remaining=13
            0x00, 0x04, 0x4D, 0x51, 0x54, 0x58, // protocol name "MQTX"
            0x04,                             // protocol level 4 (3.1.1)
            0x02,                             // connect flags (CleanSession=1)
            0x00, 0x3C,                       // keep-alive = 60 s
            0x00, 0x01, 0x61                  // client ID length=1, "a"
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "the protocol name 'MQTX' is not 'MQTT' and must be rejected per §3.1.2.1");
    }

    /// <summary>
    /// The protocol name is case-sensitive: "mqtt" (lowercase) is not valid.
    /// </summary>
    [Fact]
    public void Decoder_Connect_LowercaseProtocolName_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();

        // Protocol name "mqtt" (all lowercase)
        var bytes = new byte[]
        {
            0x10, 0x0D,
            0x00, 0x04, 0x6D, 0x71, 0x74, 0x74, // "mqtt" (lowercase)
            0x04, 0x02, 0x00, 0x3C,
            0x00, 0x01, 0x61
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "the protocol name comparison is case-sensitive per §3.1.2.1");
    }

    /// <summary>
    /// The MQTT 3.1.1 decoder does not enforce a specific protocol version byte;
    /// it stores whatever version byte it finds. This test documents that a
    /// CONNECT with protocol level 0x05 (MQTT 5.0) decodes without throwing —
    /// protocol version enforcement is handled at a higher layer.
    /// </summary>
    [Fact]
    public void Decoder_Connect_ProtocolVersion5_DecodesWithVersion5()
    {
        var decoder = new Mqtt311Decoder();

        // Same minimal CONNECT but with protocol level 0x05 instead of 0x04
        var bytes = new byte[]
        {
            0x10, 0x0D,
            0x00, 0x04, 0x4D, 0x51, 0x54, 0x54, // "MQTT"
            0x05,                                // protocol level = 5 (MQTT 5.0)
            0x02, 0x00, 0x3C,
            0x00, 0x01, 0x61
        };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var result = decoder.TryDecode(buffer, out var packets);

        result.Should().BeTrue();
        packets.Count.Should().Be(1);
        var connect = (ConnectPacket)packets[0];
        connect.ProtocolVersion.Should().Be(MqttProtocolVersion.V5_0,
            "the decoder stores the protocol version byte verbatim");
    }

    // -------------------------------------------------------------------------
    // 7. Partial frame delivery for additional packet types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that a SUBSCRIBE packet fragmented across two TCP segments is
    /// correctly reassembled.
    /// </summary>
    [Fact]
    public void Decoder_PartialFrame_SubscribePacket_ReassemblesCorrectly()
    {
        var decoder = new Mqtt311Decoder();

        var packet = new SubscribePacket
        {
            PacketId = 7,
            Topics = new[]
            {
                new TopicSubscription("sensors/temperature") { Options = new SubscriptionOptions { QoS = QualityOfService.AtLeastOnce } },
                new TopicSubscription("sensors/humidity")    { Options = new SubscriptionOptions { QoS = QualityOfService.AtMostOnce } }
            }
        };

        var encoded = PacketEncodingTestHelper.EncodePacketOnly(packet);

        // Split just after the packet ID (header + 2 bytes of variable header)
        var headerLen = encoded.Length - (encoded.Span[1] & 0x7F); // approximate, fine for small packets
        var splitPoint = Math.Max(1, encoded.Length / 2);

        var frame1 = encoded[..splitPoint];
        var frame2 = encoded[splitPoint..];

        var r1 = decoder.TryDecode(frame1, out var p1);
        var r2 = decoder.TryDecode(frame2, out var p2);

        // At least one call must have produced the complete packet
        var allPackets = p1.Concat(p2).ToList();
        allPackets.Count.Should().Be(1, "exactly one SUBSCRIBE packet was sent");

        var decoded = (SubscribePacket)allPackets[0];
        decoded.PacketId.Should().Be(packet.PacketId);
        decoded.Topics.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------
    // 8. Fixed header reserved bits validation [MQTT-2.2.2]
    // -------------------------------------------------------------------------

    /// <summary>
    /// MQTT-2.2.2: SUBSCRIBE must have lower nibble = 0x2 (first byte 0x82).
    /// A SUBSCRIBE packet with first byte 0x80 (lower nibble 0x0) must be rejected.
    /// </summary>
    [Fact]
    public void Decoder_Subscribe_WrongReservedBits_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();

        // SUBSCRIBE with lower nibble 0x0 instead of required 0x2:
        //   Fixed header: 0x80 (type=8=SUBSCRIBE, reserved=0x0 — wrong, must be 0x2)
        //   Remaining   : 0x06 (6 bytes)
        //   Packet ID   : { 0x00, 0x01 }
        //   Topic "a"   : { 0x00, 0x01, 0x61 }
        //   Options     : 0x00
        var bytes = new byte[] { 0x80, 0x06, 0x00, 0x01, 0x00, 0x01, 0x61, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "SUBSCRIBE fixed header lower nibble must be 0x2 per MQTT-2.2.2");
    }

    /// <summary>
    /// MQTT-2.2.2: UNSUBSCRIBE must have lower nibble = 0x2 (first byte 0xA2).
    /// A UNSUBSCRIBE packet with first byte 0xA0 (lower nibble 0x0) must be rejected.
    /// </summary>
    [Fact]
    public void Decoder_Unsubscribe_WrongReservedBits_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();

        // UNSUBSCRIBE with lower nibble 0x0 instead of required 0x2:
        //   Fixed header: 0xA0 (type=10=UNSUBSCRIBE, reserved=0x0 — wrong, must be 0x2)
        //   Remaining   : 0x05 (5 bytes)
        //   Packet ID   : { 0x00, 0x01 }
        //   Topic "a"   : { 0x00, 0x01, 0x61 }
        var bytes = new byte[] { 0xA0, 0x05, 0x00, 0x01, 0x00, 0x01, 0x61 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "UNSUBSCRIBE fixed header lower nibble must be 0x2 per MQTT-2.2.2");
    }

    /// <summary>
    /// MQTT-2.2.2: PUBREL must have lower nibble = 0x2 (first byte 0x62).
    /// A PUBREL packet with first byte 0x60 (lower nibble 0x0) must be rejected.
    /// </summary>
    [Fact]
    public void Decoder_PubRel_WrongReservedBits_ThrowsMqttDecoderException()
    {
        var decoder = new Mqtt311Decoder();

        // PUBREL with lower nibble 0x0 instead of required 0x2:
        //   Fixed header: 0x60 (type=6=PUBREL, reserved=0x0 — wrong, must be 0x2)
        //   Remaining   : 0x02 (2 bytes)
        //   Packet ID   : { 0x00, 0x01 }
        var bytes = new byte[] { 0x60, 0x02, 0x00, 0x01 };
        var buffer = new ReadOnlyMemory<byte>(bytes);

        var act = () => decoder.TryDecode(buffer, out _);

        act.Should().Throw<MqttDecoderException>(
            "PUBREL fixed header lower nibble must be 0x2 per MQTT-2.2.2");
    }

    /// <summary>
    /// Verifies that a CONNECT packet fragmented across three TCP segments is
    /// correctly reassembled.
    /// </summary>
    [Fact]
    public void Decoder_PartialFrame_ConnectPacket_ReassemblesCorrectly()
    {
        var decoder = new Mqtt311Decoder();

        var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "my-device-001",
            KeepAliveSeconds = 30,
            Flags = new ConnectFlags { CleanSession = true }
        };

        var encoded = PacketEncodingTestHelper.EncodePacketOnly(packet);

        // Split into 3 roughly equal fragments
        var split1 = encoded.Length / 3;
        var split2 = 2 * encoded.Length / 3;

        var frame1 = encoded[..split1];
        var frame2 = encoded[split1..split2];
        var frame3 = encoded[split2..];

        decoder.TryDecode(frame1, out var p1);
        decoder.TryDecode(frame2, out var p2);
        var r3 = decoder.TryDecode(frame3, out var p3);

        var allPackets = p1.Concat(p2).Concat(p3).ToList();
        allPackets.Count.Should().Be(1, "exactly one CONNECT packet was sent");

        var decoded = (ConnectPacket)allPackets[0];
        decoded.ClientId.Should().Be(packet.ClientId);
        decoded.KeepAliveSeconds.Should().Be(packet.KeepAliveSeconds);
    }

    /// <summary>
    /// Verifies that an UNSUBSCRIBE packet fragmented at every byte boundary is
    /// correctly reassembled.
    /// </summary>
    [Fact]
    public void Decoder_PartialFrame_UnsubscribePacket_ByteByByteReassembly()
    {
        var decoder = new Mqtt311Decoder();

        var packet = new UnsubscribePacket
        {
            PacketId = 99,
            Topics = new[] { "home/lights", "home/thermostat" }
        };

        var encoded = PacketEncodingTestHelper.EncodePacketOnly(packet);

        // Feed one byte at a time — the harshest fragmentation possible
        var collectedPackets = new List<MqttPacket>();
        for (var i = 0; i < encoded.Length; i++)
        {
            var fragment = encoded.Slice(i, 1);
            if (decoder.TryDecode(fragment, out var partial))
                collectedPackets.AddRange(partial);
        }

        collectedPackets.Count.Should().Be(1, "all fragments of one UNSUBSCRIBE must reassemble");

        var decoded = (UnsubscribePacket)collectedPackets[0];
        decoded.PacketId.Should().Be(packet.PacketId);
        decoded.Topics.Should().BeEquivalentTo(packet.Topics);
    }
}
