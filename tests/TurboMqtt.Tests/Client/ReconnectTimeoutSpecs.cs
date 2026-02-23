// -----------------------------------------------------------------------
// <copyright file="ReconnectTimeoutSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.Client;

/// <summary>
/// A fake server handle that accepts the TCP connection but never responds to MQTT CONNECT.
/// Used to simulate a stalled reconnect (broker accepts TCP but doesn't answer CONNACK).
/// </summary>
internal sealed class StallingServerHandle : IFakeServerHandle
{
    private readonly TaskCompletionSource<string> _whenClientId = new();
    private readonly TaskCompletionSource _whenTerminated = new();

    public StallingServerHandle(ILoggingAdapter log) => Log = log;

    public Task<string> WhenClientIdAssigned => _whenClientId.Task;
    public Task WhenTerminated => _whenTerminated.Task;
    public MqttProtocolVersion ProtocolVersion => MqttProtocolVersion.V3_1_1;
    public ILoggingAdapter Log { get; }

    // Silently discard all incoming MQTT bytes — never reply with CONNACK
    public void HandleBytes(in ReadOnlyMemory<byte> bytes) { }
    public void HandlePacket(MqttPacket packet) { }
    public bool TryPush(MqttPacket outboundPacket) => false;
    public void FlushPackets() { }

    // Let ProcessClientAsync exit cleanly
    public void DisconnectFromServer() => _whenTerminated.TrySetResult();
}

/// <summary>
/// Handle factory that serves a real MQTT 3.1.1 handle for the FIRST connection
/// and a <see cref="StallingServerHandle"/> for every subsequent connection.
/// This allows the initial connect to succeed while the reconnect stalls.
/// </summary>
internal sealed class FirstConnectOnlyHandleFactory : IFakeServerHandleFactory
{
    private int _count;

    public IFakeServerHandle CreateServerHandle(
        Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction,
        ILoggingAdapter log,
        MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1,
        TimeSpan? heartbeatDelay = null)
    {
        var n = Interlocked.Increment(ref _count);
        if (n == 1)
            return new FakeMqtt311ServerHandle(pushMessage, closingAction, log, heartbeatDelay);

        // All subsequent connections stall — they accept TCP but ignore MQTT CONNECT
        return new StallingServerHandle(log);
    }
}

/// <summary>
/// Verifies that <see cref="ClientStreamOwner.BeginReconnect"/> uses
/// <see cref="MqttClientConnectOptions.ReconnectTimeout"/> rather than
/// the old hard-coded 5-second value.
/// </summary>
public sealed class ReconnectTimeoutSpecs : TestKit
{
    public ReconnectTimeoutSpecs(ITestOutputHelper output) : base(output: output) { }

    [Fact]
    public void ReconnectTimeout_DefaultsToFiveSeconds()
    {
        var opts = new MqttClientConnectOptions("test", MqttProtocolVersion.V3_1_1);
        opts.ReconnectTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReconnectTimeout_IsConfigurable()
    {
        var opts = new MqttClientConnectOptions("test", MqttProtocolVersion.V3_1_1)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(42)
        };
        opts.ReconnectTimeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    /// <summary>
    /// Verifies that the reconnect CTS is built from the configured
    /// <see cref="MqttClientConnectOptions.ReconnectTimeout"/>:
    ///
    /// 1. Initial connection succeeds (first-connect-only factory).
    /// 2. The server kicks the client, triggering a reconnect.
    /// 3. The reconnect stalls (second connection is a <see cref="StallingServerHandle"/>).
    /// 4. With a 500 ms timeout, the reconnect CTS fires, ConnectAsync is cancelled,
    ///    and the actor exhausts its one retry → shuts down.
    /// 5. The whole sequence must complete in well under the old hard-coded 5 s default.
    /// </summary>
    [Fact]
    public async Task ReconnectCts_UsesConfiguredTimeout_WhenReconnectStalls()
    {
        var factory = new MqttClientFactory(Sys);
        var logger = new BusLogging(Sys.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            Sys.Settings.LogFormatter);

        // Bind on port 0 → OS assigns an ephemeral port
        var server = new FakeMqttTcpServer(
            new MqttTcpServerOptions("localhost", 0),
            MqttProtocolVersion.V3_1_1,
            logger,
            TimeSpan.FromMinutes(1),     // heartbeat not relevant for this test
            new FirstConnectOnlyHandleFactory());
        server.Bind();

        try
        {
            var port = server.BoundPort;
            var tcpOptions = new MqttClientTcpOptions("localhost", port);

            // Reconnect timeout long enough to survive Windows CI DNS overhead but short
            // enough to keep the test well under its 10 s outer CTS.
            var connectOptions = new MqttClientConnectOptions("reconnect-timeout-test", MqttProtocolVersion.V3_1_1)
            {
                ReconnectTimeout = TimeSpan.FromSeconds(3),
                MaxReconnectAttempts = 1,
                KeepAliveSeconds = 60 // disable heartbeat interference
            };

            await using var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

            // Phase 1: initial connect succeeds
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue("initial connection should succeed");

            // Phase 2: kick the client — the server closes the connection, triggering reconnect
            // Give the server a moment to register the client ID (ContinueWith callback may not have completed yet)
            await AwaitAssertAsync(
                () => server.TryKickClient("reconnect-timeout-test").Should().BeTrue("server must know the client ID"),
                duration: TimeSpan.FromSeconds(1),
                interval: TimeSpan.FromMilliseconds(10),
                cancellationToken: cts.Token);

            // Phase 3: reconnect stalls (StallingServerHandle never sends CONNACK).
            //           The 3 s CTS fires → ReconnectFailed → actor shuts down.
            //           Assert within 8 s — well under the old 5 s hard-coded value.
            await AwaitAssertAsync(
                () => client.IsConnected.Should().BeFalse("client should terminate after reconnect timeout"),
                duration: TimeSpan.FromSeconds(8),
                interval: TimeSpan.FromMilliseconds(100),
                cancellationToken: cts.Token);
        }
        finally
        {
            server.Shutdown();
        }
    }
}
