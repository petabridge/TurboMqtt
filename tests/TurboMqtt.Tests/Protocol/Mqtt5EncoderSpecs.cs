// -----------------------------------------------------------------------
// <copyright file="Mqtt5EncoderSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Unit tests for <see cref="Mqtt5Encoder"/>.
/// Each test verifies that the encoder produces the exact byte sequence
/// mandated by the OASIS MQTT 5.0 specification, hand-computed below.
/// </summary>
public class Mqtt5EncoderSpecs
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Allocate a 4096-byte buffer and encode, returning only the written bytes.</summary>
    private static (byte[] bytes, int written) RunEncode(Func<Memory<byte>, int> encode)
    {
        var raw = new byte[4096];
        var mem = new Memory<byte>(raw);
        var written = encode(mem);
        return (raw[..written], written);
    }

    // ── PINGREQ / PINGRESP ───────────────────────────────────────────────────

    public class PingPackets
    {
        [Fact]
        public void PingReq_should_produce_two_byte_frame()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = PingReqPacket.Instance;
                return Mqtt5Encoder.EncodePingPacket(packet, ref mem);
            });

            written.Should().Be(2);
            // Fixed header 0xC0 (type=12, no flags), Remaining Length 0x00
            bytes.Should().BeEquivalentTo(new byte[] { 0xC0, 0x00 });
        }

        [Fact]
        public void PingResp_should_produce_two_byte_frame()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = PingRespPacket.Instance;
                return Mqtt5Encoder.EncodePingPacket(packet, ref mem);
            });

            written.Should().Be(2);
            // Fixed header 0xD0 (type=13), Remaining Length 0x00
            bytes.Should().BeEquivalentTo(new byte[] { 0xD0, 0x00 });
        }
    }

    // ── DISCONNECT ───────────────────────────────────────────────────────────

    public class DisconnectPackets
    {
        [Fact]
        public void NormalDisconnect_compact_form_should_be_two_bytes()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new DisconnectPacket(); // defaults: ReasonCode = null → NormalDisconnection
                return Mqtt5Encoder.EncodeDisconnectPacket(packet, ref mem);
            });

            written.Should().Be(2);
            // Fixed header 0xE0 (type=14), Remaining Length 0x00
            bytes.Should().BeEquivalentTo(new byte[] { 0xE0, 0x00 });
        }

        [Fact]
        public void Disconnect_with_non_normal_reason_should_encode_reason_code_and_empty_props()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new DisconnectPacket { ReasonCode = DisconnectReasonCode.UnspecifiedError };
                return Mqtt5Encoder.EncodeDisconnectPacket(packet, ref mem);
            });

            written.Should().Be(4);
            // Fixed header 0xE0, Remaining Length 0x02, Reason 0x80, Props Length 0x00
            bytes.Should().BeEquivalentTo(new byte[] { 0xE0, 0x02, 0x80, 0x00 });
        }

        [Fact]
        public void Disconnect_with_session_expiry_interval_should_encode_property()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new DisconnectPacket
                {
                    ReasonCode = DisconnectReasonCode.NormalDisconnection,
                    SessionExpiryInterval = 300u
                };
                return Mqtt5Encoder.EncodeDisconnectPacket(packet, ref mem);
            });

            // Content = 1 (reason) + 1 (props VBI) + 5 (SEI prop) = 7
            // [0xE0, 0x07, 0x00, 0x05, 0x11, 0x00, 0x00, 0x01, 0x2C]
            // 300 decimal = 0x0000012C
            written.Should().Be(9);
            bytes[0].Should().Be(0xE0); // fixed header
            bytes[1].Should().Be(0x07); // remaining length = 7
            bytes[2].Should().Be(0x00); // NormalDisconnection
            bytes[3].Should().Be(0x05); // Properties length = 5
            bytes[4].Should().Be(0x11); // Session Expiry Interval id
            bytes[5..9].Should().BeEquivalentTo(new byte[] { 0x00, 0x00, 0x01, 0x2C }); // 300 big-endian
        }
    }

    // ── CONNACK ──────────────────────────────────────────────────────────────

    public class ConnAckPackets
    {
        [Fact]
        public void Minimal_ConnAck_success_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnAckPacket
                {
                    SessionPresent = false,
                    ReasonCode = ConnAckReasonCode.Success
                };
                return Mqtt5Encoder.EncodeConnAckPacket(packet, ref mem);
            });

            // Fixed 0x20, Remaining 0x03, SessionPresent 0x00, ReasonCode 0x00, Props 0x00
            written.Should().Be(5);
            bytes.Should().BeEquivalentTo(new byte[] { 0x20, 0x03, 0x00, 0x00, 0x00 });
        }

        [Fact]
        public void ConnAck_with_session_present_and_success()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnAckPacket
                {
                    SessionPresent = true,
                    ReasonCode = ConnAckReasonCode.Success
                };
                return Mqtt5Encoder.EncodeConnAckPacket(packet, ref mem);
            });

            written.Should().Be(5);
            bytes[2].Should().Be(0x01); // SessionPresent = true
        }

        [Fact]
        public void ConnAck_with_TopicAliasMaximum_property()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnAckPacket
                {
                    ReasonCode = ConnAckReasonCode.Success,
                    TopicAliasMaximum = 10
                };
                return Mqtt5Encoder.EncodeConnAckPacket(packet, ref mem);
            });

            // Props = 0x22 0x00 0x0A (3 bytes)
            // Content = 2 + VBI(3)=1 + 3 = 6
            written.Should().Be(8); // 1 fixed + 1 remaining + 6 content
            bytes[1].Should().Be(0x06); // remaining length = 6
            bytes[4].Should().Be(0x03); // properties length = 3
            bytes[5].Should().Be(0x22); // TopicAliasMaximum id
            bytes[6].Should().Be(0x00);
            bytes[7].Should().Be(0x0A); // 10
        }

        [Fact]
        public void ConnAck_with_AssignedClientIdentifier()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnAckPacket
                {
                    ReasonCode = ConnAckReasonCode.Success,
                    AssignedClientIdentifier = "abc"
                };
                return Mqtt5Encoder.EncodeConnAckPacket(packet, ref mem);
            });

            // Props: 0x12 0x00 0x03 'a' 'b' 'c' = 6 bytes
            // Content = 2 + 1 (VBI) + 6 = 9
            written.Should().Be(11);
            bytes[4].Should().Be(0x06); // props length
            bytes[5].Should().Be(0x12); // AssignedClientIdentifier id
            bytes[6].Should().Be(0x00);
            bytes[7].Should().Be(0x03); // string length = 3
            bytes[8].Should().Be((byte)'a');
            bytes[9].Should().Be((byte)'b');
            bytes[10].Should().Be((byte)'c');
        }
    }

    // ── PUBLISH ──────────────────────────────────────────────────────────────

    public class PublishPackets
    {
        [Fact]
        public void Publish_QoS0_no_properties_minimal_encoding()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PublishPacket(QualityOfService.AtMostOnce, false, false, "a/b")
                {
                    Payload = new byte[] { 0x01, 0x02 }
                };
                return Mqtt5Encoder.EncodePublishPacket(packet, ref mem);
            });

            // Fixed 0x30, Remaining 0x08
            // Topic: 0x00 0x03 'a' '/' 'b'
            // Props length: 0x00
            // Payload: 0x01 0x02
            written.Should().Be(10);
            bytes.Should().BeEquivalentTo(new byte[]
            {
                0x30, 0x08,
                0x00, 0x03, 0x61, 0x2F, 0x62, // "a/b"
                0x00,       // properties length
                0x01, 0x02  // payload
            });
        }

        [Fact]
        public void Publish_QoS1_with_packet_id_encodes_packet_id_before_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "t")
                {
                    PacketId = 7,
                    Payload = new byte[] { 0xAB }
                };
                return Mqtt5Encoder.EncodePublishPacket(packet, ref mem);
            });

            // Fixed 0x32 (type=3, QoS=1 → 0x30|0x02)
            // Content = 2+1 (topic) + 2 (packet id) + 1 (props VBI) + 0 (props) + 1 (payload) = 7
            written.Should().Be(9);
            bytes[0].Should().Be(0x32); // PUBLISH, QoS 1
            bytes[1].Should().Be(0x07); // remaining length 7
            bytes[2].Should().Be(0x00);
            bytes[3].Should().Be(0x01); // topic "t" length = 1
            bytes[4].Should().Be((byte)'t');
            bytes[5].Should().Be(0x00);
            bytes[6].Should().Be(0x07); // packet ID = 7
            bytes[7].Should().Be(0x00); // properties length = 0
            bytes[8].Should().Be(0xAB); // payload
        }

        [Fact]
        public void Publish_with_MessageExpiryInterval_encodes_property()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PublishPacket(QualityOfService.AtMostOnce, false, false, "x")
                {
                    MessageExpiryInterval = 60u
                };
                return Mqtt5Encoder.EncodePublishPacket(packet, ref mem);
            });

            // Props: 0x02 0x00 0x00 0x00 0x3C (5 bytes for MessageExpiryInterval=60)
            // Content = 2+1 (topic "x") + 1 (props VBI) + 5 (props) + 0 (payload) = 9
            written.Should().Be(11);
            bytes[0].Should().Be(0x30);
            bytes[1].Should().Be(0x09); // remaining length 9
            // bytes[2-4] = topic: 0x00, 0x01, 'x'
            bytes[5].Should().Be(0x05); // properties length = 5
            bytes[6].Should().Be(0x02); // MessageExpiryInterval id
            bytes[7..11].Should().BeEquivalentTo(new byte[] { 0x00, 0x00, 0x00, 0x3C }); // 60
        }

        [Fact]
        public void Publish_with_TopicAlias_encodes_property()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PublishPacket(QualityOfService.AtMostOnce, false, false, "a/b")
                {
                    TopicAlias = 3
                };
                return Mqtt5Encoder.EncodePublishPacket(packet, ref mem);
            });

            // Props: 0x23 0x00 0x03 (3 bytes for TopicAlias=3)
            var propsStart = 7; // after fixed(1) + remaining(1) + topic(2+3)
            bytes[propsStart].Should().Be(0x03); // properties length = 3
            bytes[propsStart + 1].Should().Be(0x23); // TopicAlias id
            bytes[propsStart + 2].Should().Be(0x00);
            bytes[propsStart + 3].Should().Be(0x03);
        }

        [Fact]
        public void Publish_with_UserProperties_encodes_string_pairs()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PublishPacket(QualityOfService.AtMostOnce, false, false, "t")
                {
                    UserProperties = new List<KeyValuePair<string, string>> { new("k", "v") }
                };
                return Mqtt5Encoder.EncodePublishPacket(packet, ref mem);
            });

            // User property: 0x26 + 0x00 0x01 'k' + 0x00 0x01 'v' = 7 bytes
            // Content = 2+1 (topic "t") + 1 (props VBI) + 7 (props) = 11
            written.Should().Be(13);
            // bytes[2-4] = topic: 0x00, 0x01, 't'
            bytes[5].Should().Be(0x07); // props length = 7
            bytes[6].Should().Be(0x26); // UserProperty id
        }
    }

    // ── ACK PACKETS ──────────────────────────────────────────────────────────

    public class AckPackets
    {
        [Fact]
        public void PubAck_compact_form_for_success_with_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubAckPacket { PacketId = 1, ReasonCode = MqttPubAckReasonCode.Success };
                return Mqtt5Encoder.EncodePubAckPacket(packet, ref mem);
            });

            written.Should().Be(4);
            bytes.Should().BeEquivalentTo(new byte[] { 0x40, 0x02, 0x00, 0x01 });
        }

        [Fact]
        public void PubAck_full_form_for_non_success_reason_code()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubAckPacket
                {
                    PacketId = 1,
                    ReasonCode = MqttPubAckReasonCode.NoMatchingSubscribers,
                    ReasonString = "test"
                };
                return Mqtt5Encoder.EncodePubAckPacket(packet, ref mem);
            });

            // Props: 0x1F + 0x00 0x04 + 't' 'e' 's' 't' = 7 bytes
            // Content = 2 (pkt id) + 1 (reason) + 1 (props VBI) + 7 (props) = 11
            written.Should().Be(13);
            bytes[0].Should().Be(0x40);
            bytes[1].Should().Be(0x0B); // remaining = 11
            bytes[2].Should().Be(0x00);
            bytes[3].Should().Be(0x01); // packet ID = 1
            bytes[4].Should().Be(0x10); // NoMatchingSubscribers
            bytes[5].Should().Be(0x07); // props length = 7
            bytes[6].Should().Be(0x1F); // ReasonString id
        }

        [Fact]
        public void PubAck_success_with_reason_string_uses_full_form()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubAckPacket
                {
                    PacketId = 2,
                    ReasonCode = MqttPubAckReasonCode.Success,
                    ReasonString = "ok"
                };
                return Mqtt5Encoder.EncodePubAckPacket(packet, ref mem);
            });

            // Full form even for Success because ReasonString present
            bytes[4].Should().Be(0x00); // Success reason code
        }

        [Fact]
        public void PubRec_compact_form_for_success_with_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubRecPacket { PacketId = 2 };
                return Mqtt5Encoder.EncodePubRecPacket(packet, ref mem);
            });

            written.Should().Be(4);
            bytes.Should().BeEquivalentTo(new byte[] { 0x50, 0x02, 0x00, 0x02 });
        }

        [Fact]
        public void PubRel_compact_form_for_success_with_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubRelPacket { PacketId = 3 };
                return Mqtt5Encoder.EncodePubRelPacket(packet, ref mem);
            });

            written.Should().Be(4);
            // PubRel has QoS=AtLeastOnce → fixed header 0x62
            bytes.Should().BeEquivalentTo(new byte[] { 0x62, 0x02, 0x00, 0x03 });
        }

        [Fact]
        public void PubComp_compact_form_for_success_with_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubCompPacket { PacketId = 4 };
                return Mqtt5Encoder.EncodePubCompPacket(packet, ref mem);
            });

            written.Should().Be(4);
            bytes.Should().BeEquivalentTo(new byte[] { 0x70, 0x02, 0x00, 0x04 });
        }

        [Fact]
        public void PubRec_full_form_with_explicit_failure_reason_code()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubRecPacket
                {
                    PacketId = 5,
                    ReasonCode = PubRecReasonCode.PacketIdentifierInUse
                };
                return Mqtt5Encoder.EncodePubRecPacket(packet, ref mem);
            });

            // Props size = 0, Content = 2 + 1 + 1 + 0 = 4
            written.Should().Be(6);
            bytes[0].Should().Be(0x50);
            bytes[4].Should().Be(0x91); // PacketIdentifierInUse
            bytes[5].Should().Be(0x00); // props length
        }

        [Fact]
        public void PubRel_full_form_with_failure_reason_code()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new PubRelPacket
                {
                    PacketId = 5,
                    ReasonCode = PubRelReasonCode.PacketIdentifierNotFound
                };
                return Mqtt5Encoder.EncodePubRelPacket(packet, ref mem);
            });

            written.Should().Be(6);
            bytes[0].Should().Be(0x62); // PUBREL
            bytes[4].Should().Be(0x92); // PacketIdentifierNotFound
        }
    }

    // ── SUBSCRIBE ────────────────────────────────────────────────────────────

    public class SubscribePackets
    {
        [Fact]
        public void Subscribe_single_topic_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new SubscribePacket
                {
                    PacketId = 5,
                    Topics = new[] { new TopicSubscription("test") { Options = new SubscriptionOptions { QoS = QualityOfService.AtLeastOnce } } }
                };
                return Mqtt5Encoder.EncodeSubscribePacket(packet, ref mem);
            });

            // Content = 2 (pkt id) + 1 (props VBI) + 0 (props) + 2+4+1 (topic) = 10
            written.Should().Be(12);
            bytes.Should().BeEquivalentTo(new byte[]
            {
                0x82, 0x0A, // fixed (Subscribe+QoS1), remaining 10
                0x00, 0x05, // packet ID = 5
                0x00,       // properties length = 0
                0x00, 0x04, 0x74, 0x65, 0x73, 0x74, // "test"
                0x01        // options: QoS 1
            });
        }

        [Fact]
        public void Subscribe_with_subscription_identifier_encodes_vbi_property()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new SubscribePacket
                {
                    PacketId = 1,
                    SubscriptionIdentifier = 100u,
                    Topics = new[] { new TopicSubscription("t") }
                };
                return Mqtt5Encoder.EncodeSubscribePacket(packet, ref mem);
            });

            // SubId property: 0x0B + VBI(100)=0x64 = 2 bytes
            // Content = 2 + VBI(2)=1 + 2 + (2+1+1) = 9
            written.Should().Be(11);
            bytes[4].Should().Be(0x02); // properties length = 2
            bytes[5].Should().Be(0x0B); // SubscriptionIdentifier id
            bytes[6].Should().Be(0x64); // 100 as VBI
        }
    }

    // ── SUBACK ───────────────────────────────────────────────────────────────

    public class SubAckPackets
    {
        [Fact]
        public void SubAck_single_topic_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new SubAckPacket
                {
                    PacketId = 5,
                    ReasonCodes = new[] { MqttSubscribeReasonCode.GrantedQoS1 }
                };
                return Mqtt5Encoder.EncodeSubAckPacket(packet, ref mem);
            });

            written.Should().Be(6);
            bytes.Should().BeEquivalentTo(new byte[]
            {
                0x90, 0x04, // fixed, remaining 4
                0x00, 0x05, // packet ID = 5
                0x00,       // properties length
                0x01        // GrantedQoS1
            });
        }
    }

    // ── UNSUBSCRIBE / UNSUBACK ───────────────────────────────────────────────

    public class UnsubscribePackets
    {
        [Fact]
        public void Unsubscribe_single_topic_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new UnsubscribePacket
                {
                    PacketId = 3,
                    Topics = new[] { "a/b" }
                };
                return Mqtt5Encoder.EncodeUnsubscribePacket(packet, ref mem);
            });

            written.Should().Be(10);
            bytes.Should().BeEquivalentTo(new byte[]
            {
                0xA2, 0x08, // fixed (Unsub+QoS1), remaining 8
                0x00, 0x03, // packet ID = 3
                0x00,       // properties length = 0
                0x00, 0x03, 0x61, 0x2F, 0x62 // "a/b"
            });
        }
    }

    public class UnsubAckPackets
    {
        [Fact]
        public void UnsubAck_single_topic_no_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new UnsubAckPacket
                {
                    PacketId = 3,
                    ReasonCodes = new[] { MqttUnsubscribeReasonCode.Success }
                };
                return Mqtt5Encoder.EncodeUnsubAckPacket(packet, ref mem);
            });

            written.Should().Be(6);
            bytes.Should().BeEquivalentTo(new byte[]
            {
                0xB0, 0x04, // fixed, remaining 4
                0x00, 0x03, // packet ID = 3
                0x00,       // properties length
                0x00        // Success
            });
        }
    }

    // ── AUTH ─────────────────────────────────────────────────────────────────

    public class AuthPackets
    {
        [Fact]
        public void Auth_continue_authentication_with_method_only()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new AuthPacket("PLAIN", AuthReasonCode.ContinueAuthentication)
                {
                    ReasonString = null
                };
                return Mqtt5Encoder.EncodeAuthPacket(packet, ref mem);
            });

            // Method "PLAIN" = 5 bytes → prop = 1+2+5 = 8 bytes
            // Content = 1 (reason) + VBI(8)=1 + 8 = 10
            written.Should().Be(12);
            bytes[0].Should().Be(0xF0); // AUTH
            bytes[1].Should().Be(0x0A); // remaining = 10
            bytes[2].Should().Be(0x18); // ContinueAuthentication
            bytes[3].Should().Be(0x08); // properties length = 8
            bytes[4].Should().Be(0x15); // AuthenticationMethod id
            bytes[5].Should().Be(0x00);
            bytes[6].Should().Be(0x05); // "PLAIN" length = 5
            bytes[7].Should().Be((byte)'P');
            bytes[8].Should().Be((byte)'L');
            bytes[9].Should().Be((byte)'A');
            bytes[10].Should().Be((byte)'I');
            bytes[11].Should().Be((byte)'N');
        }

        [Fact]
        public void Auth_success_with_empty_method_and_no_data()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                // Empty string still gets encoded as 2-byte length + 0 bytes
                var packet = new AuthPacket("", AuthReasonCode.Success)
                {
                    ReasonString = null
                };
                return Mqtt5Encoder.EncodeAuthPacket(packet, ref mem);
            });

            // Method "" = 0 bytes → prop = 1+2+0 = 3 bytes
            // Content = 1 + VBI(3)=1 + 3 = 5
            written.Should().Be(7);
            bytes[3].Should().Be(0x03); // props length = 3
            bytes[4].Should().Be(0x15); // AuthenticationMethod id
            bytes[5].Should().Be(0x00);
            bytes[6].Should().Be(0x00); // empty string length
        }
    }

    // ── CONNECT ──────────────────────────────────────────────────────────────

    public class ConnectPackets
    {
        [Fact]
        public void Minimal_connect_produces_correct_fixed_header_and_protocol_level()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // Total = 1 (fixed) + 1 (remaining VBI) + content(39)
            // content: "MQTT"(6) + proto(1) + flags(1) + keepalive(2) + propsVBI(1) + props(17) + clientId(2+9) = 39
            // props(17): SEI(5)+MaxPktSz(5)+TopAlias(3)+RRI(2)+RPI(2)=17; ReceiveMaximum omitted when 0 (§3.1.2.11.3)
            written.Should().Be(41);
            bytes[0].Should().Be(0x10); // CONNECT fixed header
            bytes[1].Should().Be(0x27); // remaining length = 39
            // Protocol Name
            bytes[2].Should().Be(0x00);
            bytes[3].Should().Be(0x04);
            bytes[4].Should().Be((byte)'M');
            bytes[5].Should().Be((byte)'Q');
            bytes[6].Should().Be((byte)'T');
            bytes[7].Should().Be((byte)'T');
            // Protocol Level
            bytes[8].Should().Be(0x05); // MQTT 5.0!
            // Connect Flags
            bytes[9].Should().Be(0x00); // default: no flags
            // Keep Alive
            bytes[10].Should().Be(0x00);
            bytes[11].Should().Be(0x00);
            // Properties Length (17 always-present properties; ReceiveMaximum omitted when 0)
            bytes[12].Should().Be(0x11); // 17 decimal
        }

        [Fact]
        public void Connect_always_includes_five_mandatory_v5_properties()
        {
            var (bytes, _) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // Properties start at byte 13, length = 17 bytes (ReceiveMaximum omitted when 0: §3.1.2.11.3)
            // Layout: SEI(5)+MaxPktSz(5)+TopAlias(3)+RRI(2)+RPI(2) = 17
            var props = bytes[13..30];
            // 0x11 (SEI) at offset 0
            props[0].Should().Be(0x11);
            // 0x27 (MaxPacketSize) at offset 5
            props[5].Should().Be(0x27);
            // 0x22 (TopicAliasMaximum) at offset 10
            props[10].Should().Be(0x22);
            // 0x19 (RequestResponseInfo) at offset 13
            props[13].Should().Be(0x19);
            // 0x17 (RequestProblemInfo) at offset 15
            props[15].Should().Be(0x17);
        }

        [Fact]
        public void Connect_client_id_appears_in_payload_after_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
                // ClientId = "turbomqtt" (9 bytes)
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // ClientId starts at byte 30 (after fixed(1) + remaining(1) + var-header(10) + props-vbi(1) + props(17))
            bytes[30].Should().Be(0x00);
            bytes[31].Should().Be(0x09); // "turbomqtt" length = 9
            bytes[32].Should().Be((byte)'t');
        }

        [Fact]
        public void Connect_with_username_and_password_sets_flags_and_payload()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
                {
                    UserName = "user",
                    Password = "pass"
                };
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // Connect flags: UsernameFlag=0x80, PasswordFlag=0x40 → 0xC0
            bytes[9].Should().Be(0xC0);
            // After fixed(1) + remaining(1) + var-header(10) + props-vbi(1) + props(17) + clientId(2+9)
            // = offset 41 for username
            bytes[41].Should().Be(0x00);
            bytes[42].Should().Be(0x04); // "user" length = 4
        }

        [Fact]
        public void Connect_with_authentication_method_includes_auth_properties()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
                {
                    AuthenticationMethod = "ab"
                };
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // Additional auth method prop: 1+2+2 = 5 bytes added to base props (17)
            // Props length = 22 → 0x16
            bytes[12].Should().Be(0x16); // properties length = 22
            // Find auth method property (immediately after fixed 17 props)
            bytes[30].Should().Be(0x15); // AuthenticationMethod id
        }

        [Fact]
        public void Connect_with_will_encodes_will_properties_topic_and_payload()
        {
            var (bytes, written) = RunEncode(mem =>
            {
                var flags = new ConnectFlags { WillFlag = true, WillQoS = QualityOfService.AtMostOnce };
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
                {
                    Flags = flags,
                    Will = new MqttLastWill("will/topic", new byte[] { 0xDE, 0xAD })
                };
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            // After standard header+props+clientId = 41 bytes
            // fixed(1) + remaining(1) + var-header(10) + props-vbi(1) + props(17) + clientId(2+9) = 41
            // Will section starts at byte 41:
            // NonZeroUInt16 as a struct field is default-initialized to 0 (not 1) - only new NonZeroUInt16()
            // calls the parameterless constructor with Value=1. As a field, it's zeroed.
            // will.DelayInterval.Value == 0 → not written → willPropsSize = 0
            // Will props length VBI = 0x00
            bytes[9].Should().Be(0x04); // Connect Flags: WillFlag=0x04 | WillQoS=0 (0<<3)=0x04
            // Will starts after clientId
            var willStart = 41;
            bytes[willStart].Should().Be(0x00); // will props length = 0 (no will properties)
        }

        [Fact]
        public void Connect_with_clean_session_sets_correct_flag_bit()
        {
            var (bytes, _) = RunEncode(mem =>
            {
                var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
                {
                    Flags = new ConnectFlags { CleanSession = true }
                };
                return Mqtt5Encoder.EncodeConnectPacket(packet, ref mem);
            });

            bytes[9].Should().Be(0x02); // CleanSession = bit 1 = 0x02
        }
    }

    // ── EncodePacket dispatch ────────────────────────────────────────────────

    public class EncodePacketDispatch
    {
        [Fact]
        public void EncodePacket_dispatches_to_correct_encoder_for_each_type()
        {
            var packets = new MqttPacket[]
            {
                PingReqPacket.Instance,
                PingRespPacket.Instance,
                new DisconnectPacket(),
                new ConnAckPacket { ReasonCode = ConnAckReasonCode.Success },
                new PubAckPacket { PacketId = 1, ReasonCode = MqttPubAckReasonCode.Success },
                new PubRecPacket { PacketId = 1 },
                new PubRelPacket { PacketId = 1 },
                new PubCompPacket { PacketId = 1 },
                new AuthPacket("m", AuthReasonCode.Success),
            };

            foreach (var packet in packets)
            {
                var raw = new byte[512];
                var mem = new Memory<byte>(raw);
                // Use a large PacketSize so the buffer guard passes
                var size = new PacketSize(400);
                var act = () => Mqtt5Encoder.EncodePacket(packet, ref mem, size);
                act.Should().NotThrow($"EncodePacket should handle {packet.PacketType}");
            }
        }

        [Fact]
        public void EncodePacket_throws_when_buffer_is_too_small()
        {
            var raw = new byte[1]; // tiny buffer
            var mem = new Memory<byte>(raw);
            var size = new PacketSize(400); // estimated 400 byte content → TotalSize > 1

            var act = () => Mqtt5Encoder.EncodePacket(PingReqPacket.Instance, ref mem, size);
            act.Should().Throw<ArgumentException>();
        }
    }
}
