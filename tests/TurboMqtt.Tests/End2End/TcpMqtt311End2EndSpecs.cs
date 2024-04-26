// -----------------------------------------------------------------------
// <copyright file="TcpMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

public class TcpMqtt311End2EndSpecs : TransportSpecBase
{
    public static readonly Config DebugLogging = """
                                                 akka.loglevel = DEBUG
                                                 """;

    public TcpMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output, config: DebugLogging)
    {
        // create custom log source for our TCP server
        //var logSource = LogSource.Create("FakeMqttTcpServer", typeof(FakeMqttTcpServer));
        var logger = new BusLogging(Sys.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            Sys.Settings.LogFormatter);
        _server = new FakeMqttTcpServer(new MqttTcpServerOptions("localhost", 21883), MqttProtocolVersion.V3_1_1,
            logger, TimeSpan.Zero, new DefaultFakeServerHandleFactory());
        _server.Bind();
    }

    private readonly FakeMqttTcpServer _server;

    public override async Task<IMqttClient> CreateClient()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);
        return client;
    }

    public MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883);

    protected override void AfterAll()
    {
        // shut down our local TCP server
        _server.Shutdown();
        base.AfterAll();
    }

    [Fact]
    public async Task ShouldAutomaticallyReconnectandSubscribeAfterServerDisconnect()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // kick the client
        _server.TryKickClient(DefaultConnectOptions.ClientId).Should().BeTrue();

        // automatic reconnect should be happening behind the scenes - attempt to publish a message we will receive
        var mqttMessage = new MqttMessage(DefaultTopic, "hello, world!") { QoS = QualityOfService.AtLeastOnce };
        var pubResult = await client.PublishAsync(mqttMessage);
        pubResult.IsSuccess.Should()
            .BeTrue(
                $"Expected to be able to publish message {mqttMessage} after reconnect, but got {pubResult} instead.");

        // now we should receive the message
        (await client.ReceivedMessages.WaitToReadAsync()).Should().BeTrue();
        client.ReceivedMessages.TryRead(out var receivedMessage).Should().BeTrue();
        receivedMessage!.Topic.Should().Be(DefaultTopic);

        // shut down
        using var shutdownCts = new CancellationTokenSource(RemainingOrDefault);
        await client.DisconnectAsync(shutdownCts.Token);

        await client.WhenTerminated.WaitAsync(shutdownCts.Token);
    }

    [Fact]
    public async Task ShouldTerminateClientAfterMultipleFailedConnectionAttempts()
    {
        var updatedOptions = DefaultConnectOptions;
        updatedOptions.MaxReconnectAttempts = 1; // allow 1 reconnection attempt
        var client = await ClientFactory.CreateTcpClient(updatedOptions, DefaultTcpOptions);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // shutdown server
        _server.Shutdown();

        // wait for retry-->reconnect loop to fail twice

        await client.WhenTerminated.WaitAsync(cts.Token);
        client.WhenTerminated.IsCompleted.Should().BeTrue();
    }
    
    // test case where we attempt to connect to non-existent server. ConnectAsync should fail
    [Fact]
    public async Task ShouldFailToConnectToNonExistentServer()
    {
        var updatedTcpOptions = new MqttClientTcpOptions("localhost", 21884)
        {
            MaxReconnectAttempts = 0
        };
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, updatedTcpOptions);
        
        // we are going to do this, intentionally, without a CTS here - this operation MUST FAIL if we are unable to connect
        var connectResult = await client.ConnectAsync();
        connectResult.IsSuccess.Should().BeFalse();

        client.IsConnected.Should().BeFalse();
    }
}