// -----------------------------------------------------------------------
// <copyright file="Mqtt5EstimatorAccuracyTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using FsCheck.Xunit;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Direct accuracy tests for <see cref="MqttPacketSizeEstimator"/> — verifies that
/// the estimated <see cref="PacketSize.TotalSize"/> is never less than the actual
/// bytes the encoder writes.
///
/// Each property test encodes into an oversized buffer (bypassing the estimator-based
/// guard), measures the actual bytes written, and asserts the estimate is an upper bound.
/// Run at 1000+ iterations to exercise VBI boundary crossings, large user-property strings,
/// and other property-combination edge cases.
///
/// Implements Task 4.5: Investigate and fix MqttPacketSizeEstimator underestimation edge cases.
/// </summary>
public class Mqtt5EstimatorAccuracyTests
{
    // A dummy PacketSize large enough to bypass the buffer-size guard in Mqtt5Encoder.EncodePacket.
    // ContentSize = 1_000_000 → TotalSize = 1_000_000 + 4 + 1 = 1_000_005; buffer is 1_500_000 bytes.
    private static readonly PacketSize OversizedGuard = new(1_000_000);
    private const int BufferBytes = 1_500_000;

    /// <summary>
    /// Encodes <paramref name="packet"/> into a large scratch buffer, bypassing the
    /// estimator-based guard, and returns the actual byte count written.
    /// </summary>
    private static int ActualEncodedBytes(MqttPacket packet)
    {
        var buf = new Memory<byte>(new byte[BufferBytes]);
        return Mqtt5Encoder.EncodePacket(packet, ref buf, OversizedGuard);
    }

    // ── CONNECT ─────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Connect_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5ConnectPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for CONNECT");
            return true;
        });
    }

    // ── CONNACK ─────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property ConnAck_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5ConnAckPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for CONNACK");
            return true;
        });
    }

    // ── PUBLISH ─────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Publish_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PublishPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for PUBLISH");
            return true;
        });
    }

    // ── PUBACK ──────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property PubAck_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubAckPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for PUBACK");
            return true;
        });
    }

    // ── PUBREC ──────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property PubRec_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubRecPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for PUBREC");
            return true;
        });
    }

    // ── PUBREL ──────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property PubRel_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubRelPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for PUBREL");
            return true;
        });
    }

    // ── PUBCOMP ─────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property PubComp_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PubCompPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for PUBCOMP");
            return true;
        });
    }

    // ── SUBSCRIBE ───────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Subscribe_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5SubscribePacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for SUBSCRIBE");
            return true;
        });
    }

    // ── SUBACK ──────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property SubAck_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5SubAckPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for SUBACK");
            return true;
        });
    }

    // ── UNSUBSCRIBE ─────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Unsubscribe_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5UnsubscribePacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for UNSUBSCRIBE");
            return true;
        });
    }

    // ── UNSUBACK ────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property UnsubAck_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5UnsubAckPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for UNSUBACK");
            return true;
        });
    }

    // ── DISCONNECT ───────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Disconnect_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5DisconnectPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for DISCONNECT");
            return true;
        });
    }

    // ── AUTH ────────────────────────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property Auth_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5AuthPacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for AUTH");
            return true;
        });
    }

    // ── All packet types combined ────────────────────────────────────────────

    [Property(MaxTest = 1000)]
    public Property AllPacketTypes_EstimatorNeverUnderestimates()
    {
        return Prop.ForAll(Mqtt5PacketGenerators.Mqtt5PacketArb(), packet =>
        {
            var est = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(packet);
            var actual = ActualEncodedBytes(packet);
            est.TotalSize.Should().BeGreaterThanOrEqualTo(actual,
                $"Estimator TotalSize={est.TotalSize} must be >= actual={actual} for {packet.PacketType}");
            return true;
        });
    }
}
