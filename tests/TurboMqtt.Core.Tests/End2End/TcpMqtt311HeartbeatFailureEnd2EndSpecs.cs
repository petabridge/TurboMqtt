// -----------------------------------------------------------------------
// <copyright file="TcpMqtt311HeartbeatFailureEnd2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.IO.Tcp;
using TurboMqtt.Core.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Core.Tests.End2End;

public class TcpMqtt311HeartbeatFailureEnd2EndSpecs : TestKit
{
    public TcpMqtt311HeartbeatFailureEnd2EndSpecs(ITestOutputHelper output, Config? config = null) : base(output:output, config:config)
    {
        ClientFactory = new MqttClientFactory(Sys);
        var logger = new BusLogging(Sys.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            Sys.Settings.LogFormatter);
        
        _server = new FakeMqttTcpServer(new MqttTcpServerOptions("localhost", Port), MqttProtocolVersion.V3_1_1, logger, 
            TimeSpan.FromMinutes(1));
        _server.Bind();
    }

    private const string DefaultTopic = "topic";
    private const int Port = 21887;
    private readonly FakeMqttTcpServer _server;
    
    public MqttClientFactory ClientFactory { get; }

    private MqttClientConnectOptions DefaultConnectOptions =>
        new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
        {
            UserName = "testuser",
            Password = "testpassword",
            KeepAliveSeconds = 1, // can't make it any lower than 1 second without disabling it, curse you type system
            MaxReconnectAttempts = 1, // allow 1 reconnection attempt
            PublishRetryInterval = TimeSpan.FromMilliseconds(250)
        };

    public async Task<IMqttClient> CreateClient()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);
        return client;
    }
    
    public MqttClientTcpOptions DefaultTcpOptions => new("localhost", Port);

    protected override void AfterAll()
    {
        // shut down our local TCP server
        _server.Shutdown();
        base.AfterAll();
    }
    
    [Fact]
    public async Task ShouldAutomaticallyReconnectandSubscribeAfterHeartbeatFailure()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        // need a longer timeout for this test
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();
        
        // subscribe
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();
        
        await EventFilter.Error(contains:"No heartbeat received from broker in")
            .ExpectAsync(1, async () =>
            {
                // wait for the server to disconnect us
                await Task.Delay(1500, cts.Token);
            }, cts.Token);
        
        // we should automatically reconnect and resubscribe - publish a message to verify
        var pubResult = await client.PublishAsync(new MqttMessage(DefaultTopic, "foo"){ QoS = QualityOfService.AtLeastOnce }, cts.Token);
        pubResult.IsSuccess.Should().BeTrue();
        
        // now we should receive the message
        (await client.ReceivedMessages.WaitToReadAsync()).Should().BeTrue();
        client.ReceivedMessages.TryRead(out var receivedMessage).Should().BeTrue();
        receivedMessage!.Topic.Should().Be(DefaultTopic);
    }
}