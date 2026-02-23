// -----------------------------------------------------------------------
// <copyright file="ClientStreamOwnerReconnectingSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.Client;

/// <summary>
/// A handle factory that stalls specific (1-indexed) connection numbers and uses
/// a real MQTT 3.1.1 handle for all others.
/// </summary>
internal sealed class ControlledHandleFactory : IFakeServerHandleFactory
{
    private int _count;
    private readonly HashSet<int> _stallingConnections;

    public ControlledHandleFactory(IEnumerable<int> stallingConnections)
    {
        _stallingConnections = new HashSet<int>(stallingConnections);
    }

    public IFakeServerHandle CreateServerHandle(
        Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction,
        ILoggingAdapter log,
        MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1,
        TimeSpan? heartbeatDelay = null)
    {
        var n = Interlocked.Increment(ref _count);
        if (_stallingConnections.Contains(n))
            return new StallingServerHandle(log);
        return new FakeMqtt311ServerHandle(pushMessage, closingAction, log, heartbeatDelay);
    }
}

/// <summary>
/// Isolated tests for the <see cref="ClientStreamOwner"/> Reconnecting state.
///
/// These tests use <see cref="FakeMqttTcpServer"/> to drive the actor through
/// each Reconnecting transition without Docker or a real broker.  EventFilter is
/// used to detect state transitions reliably, avoiding the timing race that comes
/// from polling <see cref="IMqttClient.IsConnected"/> during the brief window
/// between transport abort and transport swap.
///
/// Coverage:
/// <list type="bullet">
///   <item>ReconnectSuccess → returns to Running</item>
///   <item>ReconnectFailed with remaining attempts → retries (eventually succeeds)</item>
///   <item>ReconnectFailed with no remaining attempts → PoisonPill (client shuts down)</item>
///   <item>DoDisconnect while reconnecting → immediate shutdown</item>
/// </list>
/// </summary>
public sealed class ClientStreamOwnerReconnectingSpecs : TestKit
{
    // Ensure Info-level messages are forwarded to the EventStream so EventFilter
    // can intercept log messages from ClientStreamOwner.
    private static readonly string Config = "akka.loglevel = INFO";

    public ClientStreamOwnerReconnectingSpecs(ITestOutputHelper output)
        : base(output: output, config: Config) { }

    private ILoggingAdapter Logger =>
        new BusLogging(Sys.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            Sys.Settings.LogFormatter);

    private FakeMqttTcpServer CreateServer(IFakeServerHandleFactory factory)
    {
        var server = new FakeMqttTcpServer(
            new MqttTcpServerOptions("localhost", 0),
            MqttProtocolVersion.V3_1_1,
            Logger,
            TimeSpan.FromMinutes(1),
            factory);
        server.Bind();
        return server;
    }

    /// <summary>
    /// After a server-initiated disconnect triggers reconnect and the server
    /// accepts the second connection normally, the actor processes ReconnectSuccess
    /// and returns to the Running state; subsequent operations succeed.
    ///
    /// EventFilter on "Reconnect succeeded. Returning to Running state." gives a
    /// precise, race-free signal that the actor has left Reconnecting.
    /// </summary>
    [Fact]
    public async Task ReconnectSuccess_ReturnsToRunning()
    {
        var factory = new MqttClientFactory(Sys);
        var server = CreateServer(new DefaultFakeServerHandleFactory());
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var connectOptions = new MqttClientConnectOptions("reconnect-success-test", MqttProtocolVersion.V3_1_1)
            {
                MaxReconnectAttempts = 3,
                ReconnectTimeout = TimeSpan.FromSeconds(5),
                KeepAliveSeconds = 60
            };
            var tcpOptions = new MqttClientTcpOptions("localhost", server.BoundPort);
            await using var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

            // Phase 1: initial connect succeeds
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue("initial connection should succeed");

            // Phase 2: kick the client; EventFilter waits for the actor to process
            // ReconnectSuccess and log the transition back to Running state.
            await EventFilter.Info(contains: "Reconnect succeeded. Returning to Running state.")
                .ExpectAsync(1, async () =>
                {
                    var kicked = server.TryKickClient("reconnect-success-test");
                    kicked.Should().BeTrue("server must have the client registered");
                    await Task.CompletedTask;
                }, cancellationToken: cts.Token);

            // Phase 3: verify the actor is truly in Running state by performing a publish.
            // If the actor were still in Reconnecting, the publish would eventually
            // queue in the outbound channel but the QoS 0 result returns immediately
            // anyway — so just confirm IsConnected reflects the Running transport.
            client.IsConnected.Should().BeTrue("actor should be in Running state after ReconnectSuccess");
            var publishResult = await client.PublishAsync(
                new MqttMessage("test/topic", "hello") { QoS = QualityOfService.AtMostOnce },
                cts.Token);
            publishResult.IsSuccess.Should().BeTrue("client should be operational after reconnect");
        }
        finally
        {
            server.Shutdown();
        }
    }

    /// <summary>
    /// When ReconnectFailed arrives and remaining attempts > 0, the actor retries.
    /// The second attempt (connection #3) succeeds, producing a ReconnectSuccess.
    ///
    /// The elapsed-time guard (> stall timeout) proves that at least one reconnect
    /// attempt stalled before the eventual success — i.e. the retry path ran.
    /// </summary>
    [Fact]
    public async Task ReconnectFailed_WithRemainingAttempts_Retries()
    {
        var factory = new MqttClientFactory(Sys);
        // Connection 1 = real (initial), connection 2 = stalling (1st reconnect fails),
        // connection 3+ = real (2nd reconnect succeeds)
        var server = CreateServer(new ControlledHandleFactory([2]));
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var connectOptions = new MqttClientConnectOptions("reconnect-retry-test", MqttProtocolVersion.V3_1_1)
            {
                // 2 attempts: enters Reconnecting with 1 remaining.
                // 1st attempt stalls → ReconnectFailed → remaining=1 → retries.
                // 2nd attempt (conn 3) succeeds → ReconnectSuccess.
                // 3 s gives ample room for the retry to complete on slow Windows CI runners
                // (DNS + TCP connect + MQTT handshake overhead). Stall detection still fires
                // within 3 s, and the elapsed-time assertion (> 400 ms) remains valid.
                MaxReconnectAttempts = 2,
                ReconnectTimeout = TimeSpan.FromSeconds(3),
                KeepAliveSeconds = 60
            };
            var tcpOptions = new MqttClientTcpOptions("localhost", server.BoundPort);
            await using var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

            // Phase 1: initial connect succeeds (connection #1 = real handle)
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue("initial connection should succeed");

            // Phase 2: kick → 1st reconnect (conn #2) stalls; 500 ms later → ReconnectFailed.
            //           Remaining=1 > 0 → actor retries.
            //           2nd reconnect (conn #3) uses a real handle → ReconnectSuccess → Running.
            // EventFilter waits for the "Reconnect succeeded" info log (fired by ReconnectSuccess).
            var startTime = DateTimeOffset.UtcNow;
            await EventFilter.Info(contains: "Reconnect succeeded. Returning to Running state.")
                .ExpectAsync(1, async () =>
                {
                    var kicked = server.TryKickClient("reconnect-retry-test");
                    kicked.Should().BeTrue("server must have the client registered");
                    await Task.CompletedTask;
                }, cancellationToken: cts.Token);

            // Phase 3: the elapsed time must exceed the stall timeout (500 ms) because the
            // first reconnect attempt stalled before the eventual retry succeeded.
            var elapsed = DateTimeOffset.UtcNow - startTime;
            elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2),
                "at least one reconnect attempt must have stalled before the retry succeeded");

            // Phase 4: client is operational again
            client.IsConnected.Should().BeTrue("actor should be in Running state after retry succeeds");
        }
        finally
        {
            server.Shutdown();
        }
    }

    /// <summary>
    /// When ReconnectFailed arrives and no remaining attempts are left,
    /// the actor sends PoisonPill to itself and the client terminates.
    /// </summary>
    [Fact]
    public async Task ReconnectFailed_NoRemainingAttempts_ShutsDown()
    {
        var factory = new MqttClientFactory(Sys);
        // Connection 1 = real (initial); all subsequent connections stall
        var server = CreateServer(new FirstConnectOnlyHandleFactory());
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var connectOptions = new MqttClientConnectOptions("reconnect-exhausted-test", MqttProtocolVersion.V3_1_1)
            {
                // MaxReconnectAttempts=1 → enters Reconnecting with 0 remaining.
                // On ReconnectFailed: remaining == 0 → PoisonPill.
                MaxReconnectAttempts = 1,
                ReconnectTimeout = TimeSpan.FromMilliseconds(500),
                KeepAliveSeconds = 60
            };
            var tcpOptions = new MqttClientTcpOptions("localhost", server.BoundPort);
            await using var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

            // Phase 1: initial connect succeeds
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue("initial connection should succeed");

            // Phase 2: kick → reconnect stalls → ReconnectFailed → 0 remaining → PoisonPill.
            // PoisonPill triggers PostStop which aborts the transport → IsConnected = false.
            server.TryKickClient("reconnect-exhausted-test").Should().BeTrue();

            // Phase 3: client must terminate within the stall timeout (500 ms) plus overhead.
            // Asserting within 4 s provides clear headroom over the old 5 s hard-coded default.
            await AwaitAssertAsync(
                () => client.IsConnected.Should().BeFalse("client should be shut down after exhausting reconnect attempts"),
                duration: TimeSpan.FromSeconds(4),
                interval: TimeSpan.FromMilliseconds(100),
                cancellationToken: cts.Token);
        }
        finally
        {
            server.Shutdown();
        }
    }

    /// <summary>
    /// When DoDisconnect arrives while the actor is in Reconnecting state,
    /// the actor immediately responds with DisconnectComplete and sends
    /// PoisonPill to itself — bypassing the reconnect timeout.
    ///
    /// Strategy:
    ///   1. Nested EventFilters detect first "Stream terminated. Entering Reconnecting
    ///      state." (inner) and then "Transport swapped: new connection is now active."
    ///      (outer). The outer filter fires immediately after SwapTransport(), guaranteeing
    ///      IsConnected = true when it resolves — no polling required.
    ///   2. Call DisconnectAsync — the Reconnecting DoDisconnect handler fires
    ///      immediately, PoisonPill is sent, client terminates before the 30 s CTS.
    /// </summary>
    [Fact]
    public async Task DoDisconnect_WhileReconnecting_ShutsDownImmediately()
    {
        var factory = new MqttClientFactory(Sys);
        // Connection 1 = real; all subsequent connections stall
        var server = CreateServer(new FirstConnectOnlyHandleFactory());
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var connectOptions = new MqttClientConnectOptions("reconnect-disconnect-test", MqttProtocolVersion.V3_1_1)
            {
                // Long reconnect timeout so the stall doesn't time out on its own.
                MaxReconnectAttempts = 5,
                ReconnectTimeout = TimeSpan.FromSeconds(30),
                KeepAliveSeconds = 60
            };
            var tcpOptions = new MqttClientTcpOptions("localhost", server.BoundPort);
            await using var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

            // Phase 1: initial connect succeeds
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue("initial connection should succeed");

            // Phase 2+3: kick the client; wait deterministically for the new transport
            // to be swapped in.
            //
            // The outer EventFilter waits for "Transport swapped: new connection is now
            // active." — this fires immediately after SwapTransport() returns, guaranteeing
            // IsConnected = true when the filter resolves.
            //
            // The inner EventFilter waits for "Stream terminated. Entering Reconnecting
            // state." to confirm the actor entered Reconnecting before the kick returns.
            //
            // Using nested EventFilters instead of AwaitAssertAsync polling eliminates the
            // 20 ms polling race that caused intermittent failures under parallel test load.
            await EventFilter.Info(contains: "Transport swapped: new connection is now active.")
                .ExpectAsync(1, async () =>
                {
                    await EventFilter.Info(contains: "Stream terminated. Entering Reconnecting state.")
                        .ExpectAsync(1, async () =>
                        {
                            var kicked = server.TryKickClient("reconnect-disconnect-test");
                            kicked.Should().BeTrue("server must have the client registered");
                            await Task.CompletedTask;
                        }, cancellationToken: cts.Token);
                }, cancellationToken: cts.Token);

            // Phase 4: call DisconnectAsync wrapped in an EventFilter that waits for the
            // transport to be fully disposed.
            //
            // DisconnectAsync returns after WhenTerminated fires (ClientStreamOwner terminated),
            // but AbortAsync() is fire-and-forget in PostStop — the TcpTransportActor processes
            // the PoisonPill asynchronously AFTER WhenTerminated.  The EventFilter on
            // "Disposing of TCP transport stream." guarantees that State.Status has been set to
            // a non-Connected value (Disconnected) before we assert, eliminating the race window.
            //
            // Note: the first "Disposing" (for transport $a, kicked in Phase 2+3) fires BEFORE
            // this EventFilter is set up, so count=1 captures only transport $b's disposal.
            await EventFilter.Info(contains: "Disposing of TCP transport stream.")
                .ExpectAsync(1, async () =>
                {
                    using var disconnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.DisconnectAsync(disconnectCts.Token);
                }, cancellationToken: cts.Token);

            // Phase 5: transport $b is now disposed (Status = Disconnected); IsConnected is
            // deterministically false — no polling required.
            client.IsConnected.Should().BeFalse("client should be shut down after DoDisconnect during reconnect");
        }
        finally
        {
            server.Shutdown();
        }
    }
}
