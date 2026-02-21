// -----------------------------------------------------------------------
// <copyright file="MqttClientBrokerLimitSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Protocol.Pub;
using TurboMqtt.Streams;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.Client;

/// <summary>
/// Unit tests verifying that <see cref="MqttClient.PublishAsync"/> enforces
/// broker-advertised limits applied after a CONNACK (MQTT 5.0 §3.2.2).
///
/// Covers Task 5.2 (GitHub #369).
/// </summary>
public sealed class MqttClientBrokerLimitSpecs : TestKit
{
    public MqttClientBrokerLimitSpecs(ITestOutputHelper output) : base(output: output) { }

    // -----------------------------------------------------------------------
    // Minimal IMqttTransport for unit tests.
    // ConnectAsync() immediately returns true and sets status to Connected.
    // -----------------------------------------------------------------------

    private sealed class StubTransport : IMqttTransport
    {
        private readonly Channel<(IMemoryOwner<byte>, int)> _reads =
            Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        private readonly Channel<(IMemoryOwner<byte>, int)> _writes =
            Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        private readonly TaskCompletionSource<DisconnectReasonCode> _terminated = new();

        public ConnectionStatus Status { get; private set; } = ConnectionStatus.NotStarted;
        public ILoggingAdapter Log { get; init; } = null!;
        public Task<DisconnectReasonCode> WhenTerminated => _terminated.Task;
        public Task WaitForPendingWrites => Task.CompletedTask;
        public int MaxFrameSize => 1 << 20;
        public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer => _writes.Writer;
        public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader => _reads.Reader;

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            Status = ConnectionStatus.Connected;
            return Task.FromResult(true);
        }

        public Task<bool> CloseAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task AbortAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="MqttClient"/> wired to a <see cref="StubTransport"/>,
    /// drives a full <see cref="MqttClient.ConnectAsync"/> with a CONNACK that carries
    /// the supplied broker limits, and returns the ready client.
    /// </summary>
    private async Task<MqttClient> CreateClientWithBrokerLimits(ConnAckPacket connAck)
    {
        var transport = new StubTransport { Log = Sys.Log };
        var ownerProbe = CreateTestProbe();
        var clientAckProbe = CreateTestProbe();
        var qos1Probe = CreateTestProbe();
        var qos2Probe = CreateTestProbe();
        var hbProbe = CreateTestProbe();

        var outboundChannel = Channel.CreateUnbounded<MqttPacket>();
        var inboundChannel = Channel.CreateUnbounded<MqttMessage>();
        var options = new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1);
        var tcs = new TaskCompletionSource<DisconnectReasonCode>();
        tcs.SetResult(DisconnectReasonCode.NormalDisconnection);

        var requiredActors = new MqttRequiredActors(
            Qos2Actor: qos2Probe.Ref,
            Qos1Actor: qos1Probe.Ref,
            ClientAck: clientAckProbe.Ref,
            HeartBeatActor: hbProbe.Ref);

        var client = new MqttClient(
            transport,
            ownerProbe.Ref,
            requiredActors,
            inboundChannel.Reader,
            outboundChannel.Writer,
            Sys.Log,
            options,
            tcs.Task);

        // Answer the connect ask from a background task so ConnectAsync can complete.
        _ = Task.Run(async () =>
        {
            await clientAckProbe.ExpectMsgAsync<ConnectPacket>(TimeSpan.FromSeconds(10));
            clientAckProbe.Reply(new AckProtocol.ConnectSuccess(connAck));
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await client.ConnectAsync(cts.Token);
        result.IsSuccess.Should().BeTrue("connect should succeed to apply broker limits");

        return client;
    }

    /// <summary>
    /// Creates an <see cref="MqttClient"/> with default broker limits (no CONNACK applied).
    /// Suitable for the "publish within all limits" test that uses QoS 0.
    /// </summary>
    private MqttClient CreateClientWithDefaults()
    {
        var transport = new StubTransport { Log = Sys.Log };
        var ownerProbe = CreateTestProbe();
        var clientAckProbe = CreateTestProbe();
        var qos1Probe = CreateTestProbe();
        var qos2Probe = CreateTestProbe();
        var hbProbe = CreateTestProbe();

        var outboundChannel = Channel.CreateUnbounded<MqttPacket>();
        var inboundChannel = Channel.CreateUnbounded<MqttMessage>();
        var options = new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1);
        var tcs = new TaskCompletionSource<DisconnectReasonCode>();
        tcs.SetResult(DisconnectReasonCode.NormalDisconnection);

        var requiredActors = new MqttRequiredActors(
            Qos2Actor: qos2Probe.Ref,
            Qos1Actor: qos1Probe.Ref,
            ClientAck: clientAckProbe.Ref,
            HeartBeatActor: hbProbe.Ref);

        return new MqttClient(
            transport,
            ownerProbe.Ref,
            requiredActors,
            inboundChannel.Reader,
            outboundChannel.Writer,
            Sys.Log,
            options,
            tcs.Task);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_returns_failure_when_retain_requested_but_broker_disallows_retain()
    {
        var connAck = new ConnAckPacket
        {
            ReasonCode = ConnAckReasonCode.Success,
            RetainAvailable = false
        };
        await using var client = await CreateClientWithBrokerLimits(connAck);

        var message = new MqttMessage("topic", "payload")
        {
            QoS = QualityOfService.AtMostOnce,
            Retain = true
        };

        var result = await client.PublishAsync(message);

        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Contain("RetainAvailable=false");
    }

    [Fact]
    public async Task PublishAsync_returns_failure_when_QoS_exceeds_broker_maximum()
    {
        var connAck = new ConnAckPacket
        {
            ReasonCode = ConnAckReasonCode.Success,
            MaximumQoS = QualityOfService.AtLeastOnce  // broker only supports QoS 0 and 1
        };
        await using var client = await CreateClientWithBrokerLimits(connAck);

        var message = new MqttMessage("topic", "payload")
        {
            QoS = QualityOfService.ExactlyOnce,  // QoS 2 exceeds the broker's maximum
            Retain = false
        };

        var result = await client.PublishAsync(message);

        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Contain("AtLeastOnce");
    }

    [Fact]
    public async Task PublishAsync_returns_failure_when_payload_exceeds_broker_maximum_packet_size()
    {
        // 10 bytes is smaller than even the smallest MQTT PUBLISH frame (fixed header + topic)
        var connAck = new ConnAckPacket
        {
            ReasonCode = ConnAckReasonCode.Success,
            MaximumPacketSize = 10u
        };
        await using var client = await CreateClientWithBrokerLimits(connAck);

        // A 50-byte payload will produce a packet well over 10 bytes
        var message = new MqttMessage("topic", new byte[50])
        {
            QoS = QualityOfService.AtMostOnce,
            Retain = false
        };

        var result = await client.PublishAsync(message);

        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Contain("exceeds broker maximum");
    }

    [Fact]
    public async Task PublishAsync_succeeds_when_message_within_all_broker_limits()
    {
        // Default broker limits: retain=true, maxQoS=ExactlyOnce, maxPacketSize=uint.MaxValue
        await using var client = CreateClientWithDefaults();

        // QoS 0, no retain, small payload — passes all default limit checks
        var message = new MqttMessage("topic", "hello")
        {
            QoS = QualityOfService.AtMostOnce,
            Retain = false
        };

        var result = await client.PublishAsync(message);

        result.IsSuccess.Should().BeTrue();
    }
}
