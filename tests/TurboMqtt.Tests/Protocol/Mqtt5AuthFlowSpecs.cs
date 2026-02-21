// -----------------------------------------------------------------------
// <copyright file="Mqtt5AuthFlowSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboMqtt.Client;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using Xunit.Abstractions;
using static TurboMqtt.Protocol.AckProtocol;

namespace TurboMqtt.Tests.Protocol;

// ── Helpers ─────────────────────────────────────────────────────────────────

/// <summary>
/// Simple synchronous auth handler for testing.
/// Returns a fixed response to every challenge.
/// </summary>
internal sealed class FakeAuthHandler(string method, byte[] initialData, byte[] challengeResponse)
    : IMqtt5AuthHandler
{
    public string AuthenticationMethod => method;
    public ReadOnlyMemory<byte> GetInitialAuthData() => initialData;

    public ValueTask<ReadOnlyMemory<byte>> HandleChallengeAsync(ReadOnlyMemory<byte> challengeData,
        CancellationToken ct)
        => ValueTask.FromResult<ReadOnlyMemory<byte>>(challengeResponse);
}

/// <summary>
/// Auth handler that always throws on challenge — simulates auth failures.
/// </summary>
internal sealed class ThrowingAuthHandler : IMqtt5AuthHandler
{
    public string AuthenticationMethod => "fail-method";
    public ReadOnlyMemory<byte> GetInitialAuthData() => ReadOnlyMemory<byte>.Empty;

    public ValueTask<ReadOnlyMemory<byte>> HandleChallengeAsync(ReadOnlyMemory<byte> challengeData,
        CancellationToken ct)
        => throw new InvalidOperationException("Auth handler failure (test)");
}

// ── Mqtt5AuthStateMachine unit tests ────────────────────────────────────────

public class Mqtt5AuthStateMachineSpecs
{
    private static FakeAuthHandler MakeHandler() =>
        new("SCRAM-SHA-256", [0x01, 0x02], [0x03, 0x04]);

    [Fact]
    public void Initial_state_is_AwaitingConnAck()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.AwaitingConnAck);
    }

    [Fact]
    public void GetInitialAuthData_returns_handler_data()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.GetInitialAuthData().ToArray().Should().Equal(0x01, 0x02);
    }

    [Fact]
    public async Task HandleChallengeAsync_transitions_to_InChallenge_and_returns_response()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        var challenge = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication)
        {
            AuthenticationData = new byte[] { 0xAA }
        };

        var response = await sm.HandleChallengeAsync(challenge, CancellationToken.None);

        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.InChallenge);
        response.ReasonCode.Should().Be(AuthReasonCode.ContinueAuthentication);
        response.AuthenticationMethod.Should().Be("SCRAM-SHA-256");
        response.AuthenticationData.ToArray().Should().Equal(0x03, 0x04);
    }

    [Fact]
    public async Task HandleChallengeAsync_can_be_called_multiple_times_in_InChallenge_state()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        var challenge = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication)
        {
            AuthenticationData = new byte[] { 0x10 }
        };

        // First challenge
        await sm.HandleChallengeAsync(challenge, CancellationToken.None);
        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.InChallenge);

        // Second challenge (multi-round auth)
        var response = await sm.HandleChallengeAsync(challenge, CancellationToken.None);
        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.InChallenge);
        response.Should().NotBeNull();
    }

    [Fact]
    public void Complete_transitions_to_Authenticated()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.Complete();
        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.Authenticated);
    }

    [Fact]
    public void Fail_transitions_to_Failed()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.Fail();
        sm.State.Should().Be(Mqtt5AuthStateMachine.AuthState.Failed);
    }

    [Fact]
    public async Task HandleChallengeAsync_throws_when_already_Authenticated()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.Complete();

        var challenge = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sm.HandleChallengeAsync(challenge, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task HandleChallengeAsync_throws_when_already_Failed()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.Fail();

        var challenge = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sm.HandleChallengeAsync(challenge, CancellationToken.None).AsTask());
    }

    [Fact]
    public void AuthenticationMethod_comes_from_handler()
    {
        var sm = new Mqtt5AuthStateMachine(MakeHandler());
        sm.AuthenticationMethod.Should().Be("SCRAM-SHA-256");
    }
}

// ── ClientAcksActor auth flow actor tests ───────────────────────────────────

/// <summary>
/// Tests MQTT 5.0 enhanced auth flows through <see cref="ClientAcksActor"/>.
/// </summary>
public class Mqtt5AuthFlowActorSpecs : TestKit
{
    public Mqtt5AuthFlowActorSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    private static ConnectPacket MakeConnectPacket() =>
        new(MqttProtocolVersion.V5_0) { ClientId = "auth-client" };

    private static ConnAckPacket SuccessConnAck =>
        new() { ReasonCode = ConnAckReasonCode.Success };

    private static ConnAckPacket FailureConnAck =>
        new() { ReasonCode = ConnAckReasonCode.NotAuthorized };

    private static AuthPacket ChallengeAuth =>
        new("SCRAM-SHA-256", AuthReasonCode.ContinueAuthentication)
        {
            AuthenticationData = new byte[] { 0xBB }
        };

    // ── Happy path: CONNECT → CONNACK (no challenge) ─────────────────────

    [Fact]
    public async Task ConnectWithAuthHandler_no_challenge_succeeds_on_CONNACK()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new FakeAuthHandler("SCRAM-SHA-256", [0x01], [0x02]);
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        actor.Tell(msg);
        actor.Tell(SuccessConnAck);

        var resp = await ExpectMsgAsync<ConnectSuccess>();
        resp.IsSuccess.Should().BeTrue();
    }

    // ── Happy path: CONNECT → AUTH(0x18) challenge → AUTH(0x18) response → CONNACK ─

    [Fact]
    public async Task ConnectWithAuthHandler_one_challenge_round_succeeds()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new FakeAuthHandler("SCRAM-SHA-256", [0x01], [0x03, 0x04]);
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        actor.Tell(msg);
        actor.Tell(ChallengeAuth);

        // The actor should have written an AUTH response to the outbound channel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responsePacket = await channel.Reader.ReadAsync(cts.Token);
        responsePacket.Should().BeOfType<AuthPacket>();
        var authResp = (AuthPacket)responsePacket;
        authResp.ReasonCode.Should().Be(AuthReasonCode.ContinueAuthentication);
        authResp.AuthenticationData.ToArray().Should().Equal(0x03, 0x04);

        // Now the broker sends CONNACK
        actor.Tell(SuccessConnAck);

        var resp = await ExpectMsgAsync<ConnectSuccess>();
        resp.IsSuccess.Should().BeTrue();
    }

    // ── Happy path: two-round challenge ────────────────────────────────────

    [Fact]
    public async Task ConnectWithAuthHandler_two_challenge_rounds_succeed()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new FakeAuthHandler("SCRAM-SHA-256", [0x01], [0x99]);
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        actor.Tell(msg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Round 1
        actor.Tell(ChallengeAuth);
        var r1 = await channel.Reader.ReadAsync(cts.Token);
        r1.Should().BeOfType<AuthPacket>();

        // Round 2
        actor.Tell(ChallengeAuth);
        var r2 = await channel.Reader.ReadAsync(cts.Token);
        r2.Should().BeOfType<AuthPacket>();

        // CONNACK
        actor.Tell(SuccessConnAck);
        var resp = await ExpectMsgAsync<ConnectSuccess>();
        resp.IsSuccess.Should().BeTrue();
    }

    // ── Failure path: CONNACK failure during auth ───────────────────────────

    [Fact]
    public async Task ConnectWithAuthHandler_CONNACK_failure_returns_ConnectFailure()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new FakeAuthHandler("SCRAM-SHA-256", [0x01], [0x02]);
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        actor.Tell(msg);
        actor.Tell(FailureConnAck);

        var resp = await ExpectMsgAsync<ConnectFailure>();
        resp.IsSuccess.Should().BeFalse();
    }

    // ── Failure path: challenge handler throws ──────────────────────────────

    [Fact]
    public async Task ConnectWithAuthHandler_challenge_handler_throws_returns_ConnectFailure()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new ThrowingAuthHandler();
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        actor.Tell(msg);
        actor.Tell(ChallengeAuth);

        // The actor should fail the pending connect
        var resp = await ExpectMsgAsync<ConnectFailure>(TimeSpan.FromSeconds(5));
        resp.IsSuccess.Should().BeFalse();
        resp.Reason.Should().Contain("Auth challenge failed");
    }

    // ── Regular CONNECT (no auth) still works after the changes ────────────

    [Fact]
    public async Task Regular_ConnectPacket_still_works_without_auth_handler()
    {
        var actor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1) { ClientId = "test" };
        actor.Tell(connectPacket);
        actor.Tell(SuccessConnAck);

        var resp = await ExpectMsgAsync<ConnectSuccess>();
        resp.IsSuccess.Should().BeTrue();
    }

    // ── AUTH(0x19) incoming is treated as a challenge continuation ──────────

    [Fact]
    public async Task AUTH_ReAuthenticate_incoming_is_treated_as_challenge()
    {
        var channel = Channel.CreateUnbounded<MqttPacket>();
        var actor = Sys.ActorOf(Props.Create(() =>
            new ClientAcksActor(TimeSpan.FromMinutes(1), channel.Writer)));

        var handler = new FakeAuthHandler("SCRAM-SHA-256", [0x01], [0x05]);
        var msg = new ClientAcksActor.ConnectWithAuthHandler(MakeConnectPacket(), handler);

        var reAuthPacket = new AuthPacket("SCRAM-SHA-256", AuthReasonCode.ReAuthenticate)
        {
            AuthenticationData = new byte[] { 0xCC }
        };

        actor.Tell(msg);
        actor.Tell(reAuthPacket);

        // Should write an AUTH response (treating it as a challenge)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responsePacket = await channel.Reader.ReadAsync(cts.Token);
        responsePacket.Should().BeOfType<AuthPacket>();

        actor.Tell(SuccessConnAck);
        var resp = await ExpectMsgAsync<ConnectSuccess>();
        resp.IsSuccess.Should().BeTrue();
    }
}
