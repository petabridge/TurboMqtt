// -----------------------------------------------------------------------
// <copyright file="EmqxMqtt5End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using TurboMqtt.Client;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Container.Tests.End2End;

/// <summary>
/// MQTT 5.0 end-to-end tests against an EMQX broker.
/// Covers connect/disconnect, QoS 0/1/2 pub-sub, User Properties, and server-initiated DISCONNECT.
/// </summary>
[Collection(nameof(EmqxCollection))]
public class EmqxMqtt5End2EndSpecs : TestKit
{
    private readonly EmqxFixture _fixture;
    private readonly MqttClientFactory _clientFactory;

    public EmqxMqtt5End2EndSpecs(ITestOutputHelper output, EmqxFixture fixture)
        : base(output: output)
    {
        _fixture = fixture;
        _clientFactory = new MqttClientFactory(Sys);
    }

    private MqttClientTcpOptions TcpOptions => new("localhost", _fixture.MqttPort);

    private MqttClientConnectOptions ConnectOptions(string clientId) =>
        new(clientId, MqttProtocolVersion.V5_0)
        {
            UserName = "test",
            Password = "test",
            KeepAliveSeconds = 60
        };

    /// <summary>
    /// Verifies that a basic MQTT 5.0 connect and disconnect cycle completes successfully.
    /// </summary>
    [Fact]
    public async Task ShouldConnectAndDisconnect()
    {
        await using var client = await _clientFactory.CreateTcpClient(ConnectOptions("v5-connect-disc"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue("MQTT 5.0 CONNECT should succeed against EMQX");

        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies publish and subscribe at QoS 0 over MQTT 5.0.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeAtQoS0()
    {
        await using var client = await _clientFactory.CreateTcpClient(ConnectOptions("v5-pubsub-qos0"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-topic-qos0", QualityOfService.AtMostOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("v5-topic-qos0", "hello mqtt5 qos0")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello mqtt5 qos0");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies publish and subscribe at QoS 1 over MQTT 5.0.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeAtQoS1()
    {
        await using var client = await _clientFactory.CreateTcpClient(ConnectOptions("v5-pubsub-qos1"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-topic-qos1", QualityOfService.AtLeastOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("v5-topic-qos1", "hello mqtt5 qos1")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello mqtt5 qos1");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies publish and subscribe at QoS 2 over MQTT 5.0.
    /// </summary>
    [Fact]
    public async Task ShouldPublishAndSubscribeAtQoS2()
    {
        await using var client = await _clientFactory.CreateTcpClient(ConnectOptions("v5-pubsub-qos2"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-topic-qos2", QualityOfService.ExactlyOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("v5-topic-qos2", "hello mqtt5 qos2")
        {
            QoS = QualityOfService.ExactlyOnce,
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello mqtt5 qos2");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies that MQTT 5.0 User Properties on the CONNECT packet are accepted by the broker
    /// (the connection succeeds and the broker does not reject properties it doesn't use).
    /// </summary>
    [Fact]
    public async Task ShouldConnectWithUserPropertiesOnConnect()
    {
        var connectOptions = new MqttClientConnectOptions("v5-connect-userprops", MqttProtocolVersion.V5_0)
        {
            UserName = "test",
            Password = "test",
            KeepAliveSeconds = 60,
            UserProperties = new List<KeyValuePair<string, string>>
            {
                new("app-version", "1.0.0"),
                new("environment", "integration-test")
            }
        };

        await using var client = await _clientFactory.CreateTcpClient(connectOptions, TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue(
            "broker should accept a CONNECT with User Properties and return a successful CONNACK");

        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that User Properties attached to a PUBLISH packet are forwarded to the subscriber.
    /// EMQX 5.x forwards MQTT 5.0 User Properties from publisher to subscriber unchanged.
    /// </summary>
    [Fact]
    public async Task ShouldPublishWithUserPropertiesAndReceiveOnSubscriber()
    {
        await using var client = await _clientFactory.CreateTcpClient(ConnectOptions("v5-userprops-pubsub"), TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("v5-userprops-topic", QualityOfService.AtLeastOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var userProperties = new List<KeyValuePair<string, string>>
        {
            new("trace-id", "abc-123"),
            new("source", "integration-test")
        };

        var message = new MqttMessage("v5-userprops-topic", "payload-with-user-props")
        {
            QoS = QualityOfService.AtLeastOnce,
            Retain = false,
            UserProperties = userProperties
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("payload-with-user-props");

        var receivedProps = receivedMessages[0].UserProperties;
        receivedProps.Should().NotBeNull("broker should forward User Properties from publisher to subscriber");
        receivedProps!.Should().Contain(p => p.Key == "trace-id" && p.Value == "abc-123",
            "broker should forward trace-id User Property from publisher to subscriber");
        receivedProps.Should().Contain(p => p.Key == "source" && p.Value == "integration-test",
            "broker should forward source User Property from publisher to subscriber");

        await client.DisconnectAsync(cts.Token);
    }

    /// <summary>
    /// Verifies that a server-initiated DISCONNECT is handled correctly.
    /// EMQX sends DISCONNECT (Reason Code 0x8E Session Taken Over) to the first client
    /// when a second client connects with the same client ID (session takeover).
    /// The first client should terminate without crashing.
    /// </summary>
    [Fact]
    public async Task ShouldHandleServerInitiatedDisconnect()
    {
        const string sharedClientId = "v5-server-disc-test";

        // Client A connects first with MaxReconnectAttempts=0 to prevent reconnection after
        // the broker disconnects it via session takeover.
        var connectOptionsA = new MqttClientConnectOptions(sharedClientId, MqttProtocolVersion.V5_0)
        {
            UserName = "test",
            Password = "test",
            KeepAliveSeconds = 60,
            MaxReconnectAttempts = 0
        };
        await using var clientA = await _clientFactory.CreateTcpClient(connectOptionsA, TcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResultA = await clientA.ConnectAsync(cts.Token);
        connectResultA.IsSuccess.Should().BeTrue("client A should connect successfully");

        // Client B must use a separate ActorSystem+factory because ClientManagerActor enforces
        // uniqueness of client IDs within a single factory instance.
        // A separate factory still sends CONNECT with the same broker-level MQTT client ID,
        // which causes EMQX to trigger session takeover and send DISCONNECT to client A.
        var system2 = ActorSystem.Create("turbo-test-session-takeover");
        var factory2 = new MqttClientFactory(system2);
        try
        {
            var connectOptionsB = new MqttClientConnectOptions(sharedClientId, MqttProtocolVersion.V5_0)
            {
                UserName = "test",
                Password = "test",
                KeepAliveSeconds = 60,
                MaxReconnectAttempts = 0
            };
            await using var clientB = await factory2.CreateTcpClient(connectOptionsB, TcpOptions);
            var connectResultB = await clientB.ConnectAsync(cts.Token);
            connectResultB.IsSuccess.Should().BeTrue("client B should connect successfully (triggering session takeover)");

            // Client A should receive a server-initiated DISCONNECT and terminate.
            // WhenTerminated should complete within a reasonable timeout.
            var terminated = await clientA.WhenTerminated
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ContinueWith(t => t.IsCompletedSuccessfully);

            terminated.Should().BeTrue("client A should terminate after server-initiated DISCONNECT (session takeover)");
            clientA.IsConnected.Should().BeFalse("client A should no longer be connected after server disconnect");

            await clientB.DisconnectAsync(cts.Token);
        }
        finally
        {
            await system2.Terminate();
        }
    }
}
