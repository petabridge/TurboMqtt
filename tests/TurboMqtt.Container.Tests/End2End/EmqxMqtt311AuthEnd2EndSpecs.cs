// -----------------------------------------------------------------------
// <copyright file="EmqxMqtt311AuthEnd2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using TurboMqtt.Client;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Container.Tests.End2End;

/// <summary>
/// MQTT 3.1.1 end-to-end tests against an EMQX broker configured with
/// username/password authentication (anonymous access disabled).
/// </summary>
[Collection(nameof(EmqxAuthCollection))]
public class EmqxMqtt311AuthEnd2EndSpecs : TestKit
{
    private readonly EmqxAuthFixture _fixture;
    private readonly MqttClientFactory _clientFactory;

    public EmqxMqtt311AuthEnd2EndSpecs(ITestOutputHelper output, EmqxAuthFixture fixture)
        : base(output: output)
    {
        _fixture = fixture;
        _clientFactory = new MqttClientFactory(Sys);
    }

    private MqttClientTcpOptions TcpOptions =>
        new("localhost", _fixture.MqttPort);

    private MqttClientConnectOptions ValidConnectOptions(string clientId) =>
        new(clientId, MqttProtocolVersion.V3_1_1)
        {
            UserName = EmqxAuthFixture.ValidUserName,
            Password = EmqxAuthFixture.ValidPassword,
            KeepAliveSeconds = 60
        };

    /// <summary>
    /// Verifies that a client connecting with the correct username and password
    /// receives a successful CONNACK from the broker.
    /// </summary>
    [Fact]
    public async Task ShouldConnectWithValidCredentials()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("auth-connect-valid"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);

        connectResult.IsSuccess.Should().BeTrue(
            "connecting with valid username/password should receive a successful CONNACK");

        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a client connecting without any credentials (no username,
    /// no password) is rejected when the broker has anonymous access disabled
    /// (<c>EMQX_MQTT__ALLOW_ANONYMOUS=false</c>).
    /// EMQX sends CONNACK with return code 0x04 or 0x05 (MQTT 3.1.1 §3.2.2.3).
    /// </summary>
    [Fact]
    public async Task ShouldRejectConnectionWithNoCredentials()
    {
        var noCredentialsOptions = new MqttClientConnectOptions("auth-connect-no-creds", MqttProtocolVersion.V3_1_1)
        {
            KeepAliveSeconds = 60
            // No UserName, no Password
        };

        var client = await _clientFactory.CreateTcpClient(noCredentialsOptions, TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);

        connectResult.IsSuccess.Should().BeFalse(
            "connecting without credentials should be refused when the broker has anonymous access disabled");
    }

    /// <summary>
    /// Verifies that a client connecting with the correct username but wrong
    /// password is rejected. The built-in database authenticator returns a
    /// definitive DENY (not a no-match) when the user exists but the password
    /// is incorrect, so the connection is refused regardless of allow_anonymous.
    /// EMQX sends CONNACK with return code 0x04 or 0x05 (MQTT 3.1.1 §3.2.2.3).
    /// </summary>
    [Fact]
    public async Task ShouldRejectConnectionWithInvalidPassword()
    {
        var invalidOptions = new MqttClientConnectOptions("auth-connect-reject", MqttProtocolVersion.V3_1_1)
        {
            UserName = EmqxAuthFixture.ValidUserName,
            Password = "wrong-password",
            KeepAliveSeconds = 60
        };

        var client = await _clientFactory.CreateTcpClient(invalidOptions, TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);

        connectResult.IsSuccess.Should().BeFalse(
            "connecting with the correct username but an incorrect password should be refused by the broker");
    }

    /// <summary>
    /// Verifies that publish and subscribe at QoS 0 work over an authenticated connection.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeWithAuth_QoS0()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("auth-pubsub-qos0"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("auth-topic-qos0", QualityOfService.AtMostOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("auth-topic-qos0", "hello auth qos0")
        {
            QoS = QualityOfService.AtMostOnce,
            Retain = false
        };

        var publishResult = await client.PublishAsync(message, cts.Token);
        publishResult.IsSuccess.Should().BeTrue();

        var receivedMessages = new List<MqttMessage>();
        await foreach (var received in client.ReceivedMessages.ReadAllAsync(cts.Token))
        {
            receivedMessages.Add(received);
            if (receivedMessages.Count >= 1)
                break;
        }

        receivedMessages.Should().HaveCount(1);
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello auth qos0");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies that publish and subscribe at QoS 1 work over an authenticated connection.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeWithAuth_QoS1()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("auth-pubsub-qos1"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("auth-topic-qos1", QualityOfService.AtLeastOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("auth-topic-qos1", "hello auth qos1")
        {
            QoS = QualityOfService.AtLeastOnce,
            Retain = false
        };

        var publishResult = await client.PublishAsync(message, cts.Token);
        publishResult.IsSuccess.Should().BeTrue();

        var receivedMessages = new List<MqttMessage>();
        await foreach (var received in client.ReceivedMessages.ReadAllAsync(cts.Token))
        {
            receivedMessages.Add(received);
            if (receivedMessages.Count >= 1)
                break;
        }

        receivedMessages.Should().HaveCount(1);
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello auth qos1");

        await client.DisconnectAsync(cts.Token);
    }
}
