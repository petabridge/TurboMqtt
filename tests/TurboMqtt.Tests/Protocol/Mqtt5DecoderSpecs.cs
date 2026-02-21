// -----------------------------------------------------------------------
// <copyright file="Mqtt5DecoderSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Unit tests for <see cref="Mqtt5Decoder"/>.
///
/// Strategy: for each packet type, encode using <see cref="Mqtt5Encoder"/> then decode
/// with <see cref="Mqtt5Decoder"/> and assert field equality.  For simple compact forms
/// (PUBACK, DISCONNECT) we also verify against hand-computed byte sequences.
/// </summary>
public class Mqtt5DecoderSpecs
{
    // ── Shared helpers ─────────────────────────────────────────────────────

    /// <summary>Encodes a single packet into a fresh 8192-byte buffer and returns the exact bytes written.</summary>
    private static byte[] Encode(Func<Memory<byte>, int> encode)
    {
        var raw = new byte[8192];
        var mem = new Memory<byte>(raw);
        var written = encode(mem);
        return raw[..written];
    }

    /// <summary>Wraps <paramref name="bytes"/> in a <see cref="ReadOnlyMemory{T}"/> and decodes.</summary>
    private static bool Decode(byte[] bytes, out IReadOnlyList<MqttPacket> packets)
    {
        var decoder = new Mqtt5Decoder();
        var ok = decoder.TryDecode(bytes, out var immutablePackets);
        packets = immutablePackets;
        return ok;
    }

    /// <summary>Encode + decode roundtrip, returns the single decoded packet.</summary>
    private static T Roundtrip<T>(Func<Memory<byte>, int> encode) where T : MqttPacket
    {
        var bytes = Encode(encode);
        var ok = Decode(bytes, out var packets);
        ok.Should().BeTrue("roundtrip should succeed");
        packets.Should().HaveCount(1);
        return packets[0].Should().BeOfType<T>().Subject;
    }

    // ── PINGREQ / PINGRESP ─────────────────────────────────────────────────

    public class PingPackets
    {
        [Fact]
        public void PingReq_roundtrip()
        {
            var decoded = Roundtrip<PingReqPacket>(m => Mqtt5Encoder.EncodePingPacket(PingReqPacket.Instance, ref m));
            decoded.Should().BeSameAs(PingReqPacket.Instance);
        }

        [Fact]
        public void PingResp_roundtrip()
        {
            var decoded = Roundtrip<PingRespPacket>(m => Mqtt5Encoder.EncodePingPacket(PingRespPacket.Instance, ref m));
            decoded.Should().BeSameAs(PingRespPacket.Instance);
        }
    }

    // ── DISCONNECT ─────────────────────────────────────────────────────────

    public class DisconnectPackets
    {
        [Fact]
        public void Compact_disconnect_decodes_to_normal_disconnection()
        {
            // Hand-crafted: 0xE0 0x00 (compact form, Remaining Length = 0)
            var bytes = new byte[] { 0xE0, 0x00 };
            Decode(bytes, out var packets).Should().BeTrue();
            var p = packets[0].Should().BeOfType<DisconnectPacket>().Subject;
            p.ReasonCode.Should().BeNull();
        }

        [Fact]
        public void Disconnect_with_reason_code_roundtrip()
        {
            var packet = new DisconnectPacket { ReasonCode = DisconnectReasonCode.ServerBusy };
            var decoded = Roundtrip<DisconnectPacket>(m => Mqtt5Encoder.EncodeDisconnectPacket(packet, ref m));
            decoded.ReasonCode.Should().Be(DisconnectReasonCode.ServerBusy);
        }

        [Fact]
        public void Disconnect_with_session_expiry_and_server_reference_roundtrip()
        {
            var packet = new DisconnectPacket
            {
                ReasonCode = DisconnectReasonCode.NormalDisconnection,
                SessionExpiryInterval = 120u,
                ServerReference = "mqtt.example.com"
            };
            var decoded = Roundtrip<DisconnectPacket>(m => Mqtt5Encoder.EncodeDisconnectPacket(packet, ref m));
            decoded.ReasonCode.Should().Be(DisconnectReasonCode.NormalDisconnection);
            decoded.SessionExpiryInterval.Should().Be(120u);
            decoded.ServerReference.Should().Be("mqtt.example.com");
        }
    }

    // ── CONNACK ────────────────────────────────────────────────────────────

    public class ConnAckPackets
    {
        [Fact]
        public void Minimal_connack_no_properties()
        {
            var packet = new ConnAckPacket { SessionPresent = false, ReasonCode = ConnAckReasonCode.Success };
            var decoded = Roundtrip<ConnAckPacket>(m => Mqtt5Encoder.EncodeConnAckPacket(packet, ref m));
            decoded.SessionPresent.Should().BeFalse();
            decoded.ReasonCode.Should().Be(ConnAckReasonCode.Success);
        }

        [Fact]
        public void ConnAck_with_session_present_and_bad_credentials()
        {
            var packet = new ConnAckPacket
            {
                SessionPresent = true,
                ReasonCode = ConnAckReasonCode.BadUsernameOrPassword
            };
            var decoded = Roundtrip<ConnAckPacket>(m => Mqtt5Encoder.EncodeConnAckPacket(packet, ref m));
            decoded.SessionPresent.Should().BeTrue();
            decoded.ReasonCode.Should().Be(ConnAckReasonCode.BadUsernameOrPassword);
        }

        [Fact]
        public void ConnAck_with_all_standard_broker_properties()
        {
            var packet = new ConnAckPacket
            {
                SessionPresent = false,
                ReasonCode = ConnAckReasonCode.Success,
                SessionExpiryInterval = 3600u,
                ReceiveMaximum = 100,
                MaximumQoS = QualityOfService.AtLeastOnce,
                RetainAvailable = true,
                MaximumPacketSize = 65536u,
                AssignedClientIdentifier = "auto-id-42",
                TopicAliasMaximum = 10,
                ReasonString = "Connected",
                WildcardSubscriptionAvailable = true,
                SubscriptionIdentifiersAvailable = false,
                SharedSubscriptionAvailable = true,
                ServerKeepAlive = 60,
                ResponseInformation = "response-topic-prefix",
                ServerReference = "broker2.example.com"
            };
            var decoded = Roundtrip<ConnAckPacket>(m => Mqtt5Encoder.EncodeConnAckPacket(packet, ref m));
            decoded.SessionPresent.Should().BeFalse();
            decoded.ReasonCode.Should().Be(ConnAckReasonCode.Success);
            decoded.SessionExpiryInterval.Should().Be(3600u);
            decoded.ReceiveMaximum.Should().Be(100);
            decoded.MaximumQoS.Should().Be(QualityOfService.AtLeastOnce);
            decoded.RetainAvailable.Should().BeTrue();
            decoded.MaximumPacketSize.Should().Be(65536u);
            decoded.AssignedClientIdentifier.Should().Be("auto-id-42");
            decoded.TopicAliasMaximum.Should().Be(10);
            decoded.ReasonString.Should().Be("Connected");
            decoded.WildcardSubscriptionAvailable.Should().BeTrue();
            decoded.SubscriptionIdentifiersAvailable.Should().BeFalse();
            decoded.SharedSubscriptionAvailable.Should().BeTrue();
            decoded.ServerKeepAlive.Should().Be(60);
            decoded.ResponseInformation.Should().Be("response-topic-prefix");
            decoded.ServerReference.Should().Be("broker2.example.com");
        }

        [Fact]
        public void ConnAck_with_user_properties()
        {
            var packet = new ConnAckPacket
            {
                ReasonCode = ConnAckReasonCode.Success,
                UserProperties = new List<KeyValuePair<string, string>> { new("x-region", "us-east-1") }
            };
            var decoded = Roundtrip<ConnAckPacket>(m => Mqtt5Encoder.EncodeConnAckPacket(packet, ref m));
            decoded.UserProperties.Should().NotBeNull();
            decoded.UserProperties!.First(p => p.Key == "x-region").Value.Should().Be("us-east-1");
        }
    }

    // ── PUBLISH ────────────────────────────────────────────────────────────

    public class PublishPackets
    {
        [Fact]
        public void Publish_qos0_no_properties()
        {
            var packet = new PublishPacket(QualityOfService.AtMostOnce, false, false, "test/topic")
            {
                Payload = new byte[] { 1, 2, 3 }
            };
            var decoded = Roundtrip<PublishPacket>(m =>
            {
                var size = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
                return Mqtt5Encoder.EncodePublishPacket(packet, ref m);
            });
            decoded.TopicName.Should().Be("test/topic");
            decoded.QualityOfService.Should().Be(QualityOfService.AtMostOnce);
            decoded.Payload.ToArray().Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        }

        [Fact]
        public void Publish_qos1_with_v5_properties()
        {
            var packet = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "sensor/temp")
            {
                PacketId = 42,
                Payload = new byte[] { 0xFF, 0x00 },
                PayloadFormatIndicator = PayloadFormatIndicator.Utf8Encoded,
                MessageExpiryInterval = 300u,
                TopicAlias = 5,
                ResponseTopic = "sensor/temp/response",
                CorrelationData = new byte[] { 0x01, 0x02 },
                ContentType = "application/json",
                UserProperties = new List<KeyValuePair<string, string>> { new("src", "sensor-1") }
            };
            var decoded = Roundtrip<PublishPacket>(m => Mqtt5Encoder.EncodePublishPacket(packet, ref m));
            decoded.TopicName.Should().Be("sensor/temp");
            decoded.QualityOfService.Should().Be(QualityOfService.AtLeastOnce);
            decoded.PacketId.Value.Should().Be(42);
            decoded.PayloadFormatIndicator.Should().Be(PayloadFormatIndicator.Utf8Encoded);
            decoded.MessageExpiryInterval.Should().Be(300u);
            decoded.TopicAlias.Should().Be(5);
            decoded.ResponseTopic.Should().Be("sensor/temp/response");
            decoded.CorrelationData!.Value.ToArray().Should().BeEquivalentTo(new byte[] { 0x01, 0x02 });
            decoded.ContentType.Should().Be("application/json");
            decoded.UserProperties!.First(p => p.Key == "src").Value.Should().Be("sensor-1");
        }

        [Fact]
        public void Publish_qos2_with_subscription_identifiers()
        {
            var packet = new PublishPacket(QualityOfService.ExactlyOnce, false, true, "alerts/critical")
            {
                PacketId = 99,
                Payload = new byte[] { 0xAA },
                SubscriptionIdentifiers = new List<uint> { 1u, 42u }
            };
            var decoded = Roundtrip<PublishPacket>(m => Mqtt5Encoder.EncodePublishPacket(packet, ref m));
            decoded.QualityOfService.Should().Be(QualityOfService.ExactlyOnce);
            decoded.RetainRequested.Should().BeTrue();
            decoded.PacketId.Value.Should().Be(99);
            decoded.SubscriptionIdentifiers.Should().BeEquivalentTo(new[] { 1u, 42u });
        }
    }

    // ── PUBACK ─────────────────────────────────────────────────────────────

    public class PubAckPackets
    {
        [Fact]
        public void PubAck_compact_form_hand_computed()
        {
            // Compact: 0x40 0x02 <packetId MSB> <packetId LSB>
            var bytes = new byte[] { 0x40, 0x02, 0x00, 0x07 };
            Decode(bytes, out var packets).Should().BeTrue();
            var p = packets[0].Should().BeOfType<PubAckPacket>().Subject;
            p.PacketId.Value.Should().Be(7);
            p.ReasonCode.Should().Be(MqttPubAckReasonCode.Success);
        }

        [Fact]
        public void PubAck_full_form_with_reason_and_properties_roundtrip()
        {
            var packet = new PubAckPacket
            {
                PacketId = 123,
                ReasonCode = MqttPubAckReasonCode.NotAuthorized,
                ReasonString = "No permission",
                UserProperties = new List<KeyValuePair<string, string>> { new("detail", "acl-deny") }
            };
            var decoded = Roundtrip<PubAckPacket>(m => Mqtt5Encoder.EncodePubAckPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(123);
            decoded.ReasonCode.Should().Be(MqttPubAckReasonCode.NotAuthorized);
            decoded.ReasonString.Should().Be("No permission");
            decoded.UserProperties!.First(p => p.Key == "detail").Value.Should().Be("acl-deny");
        }
    }

    // ── PUBREC ─────────────────────────────────────────────────────────────

    public class PubRecPackets
    {
        [Fact]
        public void PubRec_compact_form()
        {
            var bytes = new byte[] { 0x50, 0x02, 0x00, 0x05 }; // PubRec, RL=2, pid=5
            Decode(bytes, out var packets).Should().BeTrue();
            var p = packets[0].Should().BeOfType<PubRecPacket>().Subject;
            p.PacketId.Value.Should().Be(5);
            p.ReasonCode.Should().Be(PubRecReasonCode.Success);
        }

        [Fact]
        public void PubRec_with_reason_code_roundtrip()
        {
            var packet = new PubRecPacket
            {
                PacketId = 55,
                ReasonCode = PubRecReasonCode.PacketIdentifierInUse,
                ReasonString = "already in use"
            };
            var decoded = Roundtrip<PubRecPacket>(m => Mqtt5Encoder.EncodePubRecPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(55);
            decoded.ReasonCode.Should().Be(PubRecReasonCode.PacketIdentifierInUse);
            decoded.ReasonString.Should().Be("already in use");
        }
    }

    // ── PUBREL ─────────────────────────────────────────────────────────────

    public class PubRelPackets
    {
        [Fact]
        public void PubRel_compact_form()
        {
            // PubRel fixed header has flags=0x02: 0x62
            var bytes = new byte[] { 0x62, 0x02, 0x00, 0x0A }; // RL=2, pid=10
            Decode(bytes, out var packets).Should().BeTrue();
            var p = packets[0].Should().BeOfType<PubRelPacket>().Subject;
            p.PacketId.Value.Should().Be(10);
            p.ReasonCode.Should().Be(PubRelReasonCode.Success);
        }

        [Fact]
        public void PubRel_with_reason_code_roundtrip()
        {
            var packet = new PubRelPacket
            {
                PacketId = 77,
                ReasonCode = PubRelReasonCode.PacketIdentifierNotFound
            };
            var decoded = Roundtrip<PubRelPacket>(m => Mqtt5Encoder.EncodePubRelPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(77);
            decoded.ReasonCode.Should().Be(PubRelReasonCode.PacketIdentifierNotFound);
        }
    }

    // ── PUBCOMP ────────────────────────────────────────────────────────────

    public class PubCompPackets
    {
        [Fact]
        public void PubComp_compact_form()
        {
            var bytes = new byte[] { 0x70, 0x02, 0x00, 0x0B }; // RL=2, pid=11
            Decode(bytes, out var packets).Should().BeTrue();
            var p = packets[0].Should().BeOfType<PubCompPacket>().Subject;
            p.PacketId.Value.Should().Be(11);
            p.ReasonCode.Should().Be(PubCompReasonCode.Success);
        }

        [Fact]
        public void PubComp_success_roundtrip()
        {
            var packet = new PubCompPacket { PacketId = 88, ReasonCode = PubCompReasonCode.Success };
            var decoded = Roundtrip<PubCompPacket>(m => Mqtt5Encoder.EncodePubCompPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(88);
            decoded.ReasonCode.Should().Be(PubCompReasonCode.Success);
        }
    }

    // ── SUBSCRIBE ──────────────────────────────────────────────────────────

    public class SubscribePackets
    {
        [Fact]
        public void Subscribe_single_topic_no_properties()
        {
            var packet = new SubscribePacket
            {
                PacketId = 1,
                Topics = new[]
                {
                    new TopicSubscription("home/#")
                    {
                        Options = new SubscriptionOptions { QoS = QualityOfService.AtLeastOnce }
                    }
                }
            };
            var decoded = Roundtrip<SubscribePacket>(m => Mqtt5Encoder.EncodeSubscribePacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(1);
            decoded.Topics.Should().HaveCount(1);
            decoded.Topics[0].Topic.Should().Be("home/#");
            decoded.Topics[0].Options.QoS.Should().Be(QualityOfService.AtLeastOnce);
        }

        [Fact]
        public void Subscribe_with_subscription_identifier()
        {
            var packet = new SubscribePacket
            {
                PacketId = 7,
                SubscriptionIdentifier = 42u,
                Topics = new[]
                {
                    new TopicSubscription("devices/+/status")
                    {
                        Options = new SubscriptionOptions
                        {
                            QoS = QualityOfService.ExactlyOnce,
                            NoLocal = true,
                            RetainAsPublished = true,
                            RetainHandling = RetainHandlingOption.SendAtSubscribeIfNew
                        }
                    }
                }
            };
            var decoded = Roundtrip<SubscribePacket>(m => Mqtt5Encoder.EncodeSubscribePacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(7);
            decoded.SubscriptionIdentifier.Should().Be(42u);
            decoded.Topics[0].Options.QoS.Should().Be(QualityOfService.ExactlyOnce);
            decoded.Topics[0].Options.NoLocal.Should().BeTrue();
            decoded.Topics[0].Options.RetainAsPublished.Should().BeTrue();
            decoded.Topics[0].Options.RetainHandling.Should().Be(RetainHandlingOption.SendAtSubscribeIfNew);
        }
    }

    // ── SUBACK ─────────────────────────────────────────────────────────────

    public class SubAckPackets
    {
        [Fact]
        public void SubAck_single_granted_qos1()
        {
            var packet = new SubAckPacket
            {
                PacketId = 1,
                ReasonCodes = new[] { MqttSubscribeReasonCode.GrantedQoS1 }
            };
            var decoded = Roundtrip<SubAckPacket>(m => Mqtt5Encoder.EncodeSubAckPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(1);
            decoded.ReasonCodes.Should().BeEquivalentTo(new[] { MqttSubscribeReasonCode.GrantedQoS1 });
        }

        [Fact]
        public void SubAck_with_reason_string_and_user_properties()
        {
            var packet = new SubAckPacket
            {
                PacketId = 3,
                ReasonString = "partial failure",
                ReasonCodes = new[]
                {
                    MqttSubscribeReasonCode.GrantedQoS0,
                    MqttSubscribeReasonCode.NotAuthorized
                },
                UserProperties = new List<KeyValuePair<string, string>> { new("x-info", "test") }
            };
            var decoded = Roundtrip<SubAckPacket>(m => Mqtt5Encoder.EncodeSubAckPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(3);
            decoded.ReasonString.Should().Be("partial failure");
            decoded.ReasonCodes.Should().BeEquivalentTo(new[]
            {
                MqttSubscribeReasonCode.GrantedQoS0,
                MqttSubscribeReasonCode.NotAuthorized
            });
            decoded.UserProperties!.First(p => p.Key == "x-info").Value.Should().Be("test");
        }
    }

    // ── UNSUBSCRIBE ────────────────────────────────────────────────────────

    public class UnsubscribePackets
    {
        [Fact]
        public void Unsubscribe_single_topic_roundtrip()
        {
            var packet = new UnsubscribePacket
            {
                PacketId = 2,
                Topics = new[] { "home/#" }
            };
            var decoded = Roundtrip<UnsubscribePacket>(m => Mqtt5Encoder.EncodeUnsubscribePacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(2);
            decoded.Topics.Should().BeEquivalentTo(new[] { "home/#" });
        }

        [Fact]
        public void Unsubscribe_with_user_properties_roundtrip()
        {
            var packet = new UnsubscribePacket
            {
                PacketId = 9,
                Topics = new[] { "topic/a", "topic/b" },
                UserProperties = new List<KeyValuePair<string, string>> { new("reason", "cleanup") }
            };
            var decoded = Roundtrip<UnsubscribePacket>(m => Mqtt5Encoder.EncodeUnsubscribePacket(packet, ref m));
            decoded.Topics.Should().BeEquivalentTo(new[] { "topic/a", "topic/b" });
            decoded.UserProperties!.First(p => p.Key == "reason").Value.Should().Be("cleanup");
        }
    }

    // ── UNSUBACK ───────────────────────────────────────────────────────────

    public class UnsubAckPackets
    {
        [Fact]
        public void UnsubAck_with_reason_codes_roundtrip()
        {
            var packet = new UnsubAckPacket
            {
                PacketId = 9,
                ReasonCodes = new[]
                {
                    MqttUnsubscribeReasonCode.Success,
                    MqttUnsubscribeReasonCode.NoSubscriptionExisted
                },
                ReasonString = "done"
            };
            var decoded = Roundtrip<UnsubAckPacket>(m => Mqtt5Encoder.EncodeUnsubAckPacket(packet, ref m));
            decoded.PacketId.Value.Should().Be(9);
            decoded.ReasonCodes.Should().BeEquivalentTo(new[]
            {
                MqttUnsubscribeReasonCode.Success,
                MqttUnsubscribeReasonCode.NoSubscriptionExisted
            });
            decoded.ReasonString.Should().Be("done");
        }
    }

    // ── AUTH ───────────────────────────────────────────────────────────────

    public class AuthPackets
    {
        [Fact]
        public void Auth_continue_authentication_with_method_and_data()
        {
            var packet = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication)
            {
                AuthenticationData = new byte[] { 0x01, 0x02, 0x03 }
            };
            var decoded = Roundtrip<AuthPacket>(m => Mqtt5Encoder.EncodeAuthPacket(packet, ref m));
            decoded.ReasonCode.Should().Be(AuthReasonCode.ContinueAuthentication);
            decoded.AuthenticationMethod.Should().Be("SCRAM-SHA-256");
            decoded.AuthenticationData.ToArray().Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03 });
        }

        [Fact]
        public void Auth_reauthenticate_with_user_properties()
        {
            var packet = new AuthPacket("PLAIN", AuthReasonCode.ReAuthenticate)
            {
                UserProperties = new List<KeyValuePair<string, string>> { new("session", "abc123") }
            };
            var decoded = Roundtrip<AuthPacket>(m => Mqtt5Encoder.EncodeAuthPacket(packet, ref m));
            decoded.ReasonCode.Should().Be(AuthReasonCode.ReAuthenticate);
            decoded.AuthenticationMethod.Should().Be("PLAIN");
            decoded.UserProperties!.First(p => p.Key == "session").Value.Should().Be("abc123");
        }
    }

    // ── Multiple packets in a single buffer ────────────────────────────────

    public class MultiPacketDecoding
    {
        [Fact]
        public void Two_ping_packets_in_one_buffer()
        {
            var req = Encode(m => Mqtt5Encoder.EncodePingPacket(PingReqPacket.Instance, ref m));
            var resp = Encode(m => Mqtt5Encoder.EncodePingPacket(PingRespPacket.Instance, ref m));
            var combined = req.Concat(resp).ToArray();

            Decode(combined, out var packets).Should().BeTrue();
            packets.Should().HaveCount(2);
            packets[0].Should().BeOfType<PingReqPacket>();
            packets[1].Should().BeOfType<PingRespPacket>();
        }

        [Fact]
        public void Partial_frame_accumulates_in_remainder()
        {
            var req = Encode(m => Mqtt5Encoder.EncodePingPacket(PingReqPacket.Instance, ref m));
            var decoder = new Mqtt5Decoder();

            // Feed only the first byte — should return false (incomplete packet)
            var first = decoder.TryDecode(new ReadOnlyMemory<byte>(req[..1]), out var partial);
            first.Should().BeFalse();
            partial.Should().BeEmpty();

            // Feed the remainder — should now decode successfully
            var second = decoder.TryDecode(new ReadOnlyMemory<byte>(req[1..]), out var complete);
            second.Should().BeTrue();
            complete.Should().HaveCount(1);
            complete[0].Should().BeOfType<PingReqPacket>();
        }
    }
}
