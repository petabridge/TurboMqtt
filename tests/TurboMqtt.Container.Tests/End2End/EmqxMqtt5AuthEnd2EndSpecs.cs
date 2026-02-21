// -----------------------------------------------------------------------
// <copyright file="EmqxMqtt5AuthEnd2EndSpecs.cs" company="Petabridge, LLC">
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
/// MQTT 5.0 end-to-end tests against an EMQX broker configured with
/// username/password authentication (anonymous access disabled).
/// </summary>
[Collection(nameof(EmqxAuthCollection))]
public class EmqxMqtt5AuthEnd2EndSpecs : TestKit
{
    private readonly EmqxAuthFixture _fixture;
    private readonly MqttClientFactory _clientFactory;

    public EmqxMqtt5AuthEnd2EndSpecs(ITestOutputHelper output, EmqxAuthFixture fixture)
        : base(output: output)
    {
        _fixture = fixture;
        _clientFactory = new MqttClientFactory(Sys);
    }

    private MqttClientTcpOptions TcpOptions =>
        new("localhost", _fixture.MqttPort);

    private MqttClientConnectOptions ValidConnectOptions(string clientId) =>
        new(clientId, MqttProtocolVersion.V5_0)
        {
            UserName = EmqxAuthFixture.ValidUserName,
            Password = EmqxAuthFixture.ValidPassword,
            KeepAliveSeconds = 60
        };

    /// <summary>
    /// Verifies that a MQTT 5.0 client connecting with the correct username and password
    /// receives a successful CONNACK from the broker.
    /// </summary>
    [Fact]
    public async Task ShouldConnectWithValidCredentials()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("v5-auth-connect-valid"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);

        connectResult.IsSuccess.Should().BeTrue(
            "MQTT 5.0 connecting with valid username/password should receive a successful CONNACK");

        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a MQTT 5.0 client connecting with the correct username but wrong
    /// password is rejected by the broker.
    /// EMQX sends CONNACK with Reason Code 0x86 (Bad User Name or Password) per MQTT 5.0 §3.2.2.2.
    /// </summary>
    [Fact]
    public async Task ShouldRejectConnectionWithInvalidPassword()
    {
        var invalidOptions = new MqttClientConnectOptions("v5-auth-connect-reject", MqttProtocolVersion.V5_0)
        {
            UserName = EmqxAuthFixture.ValidUserName,
            Password = "wrong-password",
            KeepAliveSeconds = 60
        };

        var client = await _clientFactory.CreateTcpClient(invalidOptions, TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);

        connectResult.IsSuccess.Should().BeFalse(
            "MQTT 5.0 connecting with the correct username but an incorrect password should be refused by the broker");
    }

    /// <summary>
    /// Verifies that MQTT 5.0 publish and subscribe at QoS 0 work over an authenticated connection.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeWithAuth_QoS0()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("v5-auth-pubsub-qos0"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-auth-topic-qos0", QualityOfService.AtMostOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("v5-auth-topic-qos0", "hello v5 auth qos0")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello v5 auth qos0");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies that MQTT 5.0 publish and subscribe at QoS 1 work over an authenticated connection.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeWithAuth_QoS1()
    {
        var client = await _clientFactory.CreateTcpClient(
            ValidConnectOptions("v5-auth-pubsub-qos1"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-auth-topic-qos1", QualityOfService.AtLeastOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("v5-auth-topic-qos1", "hello v5 auth qos1")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello v5 auth qos1");

        await client.DisconnectAsync(cts.Token);
    }
}
