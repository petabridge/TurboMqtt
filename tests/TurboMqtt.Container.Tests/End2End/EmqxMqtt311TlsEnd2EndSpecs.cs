// -----------------------------------------------------------------------
// <copyright file="EmqxMqtt311TlsEnd2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using TurboMqtt.Client;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Container.Tests.End2End;

[Collection(nameof(EmqxCollection))]
public class EmqxMqtt311TlsEnd2EndSpecs : TestKit
{
    private readonly EmqxFixture _fixture;
    private readonly MqttClientFactory _clientFactory;

    public EmqxMqtt311TlsEnd2EndSpecs(ITestOutputHelper output, EmqxFixture fixture)
        : base(output: output)
    {
        _fixture = fixture;
        _clientFactory = new MqttClientFactory(Sys);
    }

    private MqttClientConnectOptions DefaultConnectOptions =>
        new("test-tls-client", MqttProtocolVersion.V3_1_1)
        {
            UserName = "test",
            Password = "test",
            KeepAliveSeconds = 60
        };

    private MqttClientTcpOptions DefaultTcpOptions =>
        new("localhost", _fixture.MqttTlsPort);

    // EMQX ships with self-signed certs, so we need to accept them
    private static MqttClientTlsOptions DefaultTlsOptions => new()
    {
        ServerCertificateValidationCallback = AcceptAllCertificates
    };

    private static bool AcceptAllCertificates(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;

    [Fact]
    public async Task ShouldConnectAndDisconnectOverTls()
    {
        var client = await _clientFactory.CreateTlsTcpClient(
            DefaultConnectOptions, DefaultTcpOptions, DefaultTlsOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();
        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldPublishAndSubscribeOverTls_QoS0()
    {
        var client = await _clientFactory.CreateTlsTcpClient(
            DefaultConnectOptions, DefaultTcpOptions, DefaultTlsOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("tls-topic", QualityOfService.AtMostOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("tls-topic", "hello tls qos0")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello tls qos0");

        await client.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task ShouldPublishAndSubscribeOverTls_QoS1()
    {
        var client = await _clientFactory.CreateTlsTcpClient(
            DefaultConnectOptions, DefaultTcpOptions, DefaultTlsOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var subscribeResult = await client.SubscribeAsync("tls-topic-qos1", QualityOfService.AtLeastOnce, cts.Token);
        subscribeResult.IsSuccess.Should().BeTrue();

        var message = new MqttMessage("tls-topic-qos1", "hello tls qos1")
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
        Encoding.UTF8.GetString(receivedMessages[0].Payload.Span).Should().Be("hello tls qos1");

        await client.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task ShouldWorkWithCustomServerCertificateValidationCallback()
    {
        var callbackInvoked = false;

        var tlsOptions = new MqttClientTlsOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) =>
            {
                callbackInvoked = true;
                return true;
            }
        };

        var client = await _clientFactory.CreateTlsTcpClient(
            DefaultConnectOptions, DefaultTcpOptions, tlsOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        callbackInvoked.Should().BeTrue("the custom certificate validation callback should have been invoked");

        await client.DisconnectAsync(cts.Token);
    }
}
