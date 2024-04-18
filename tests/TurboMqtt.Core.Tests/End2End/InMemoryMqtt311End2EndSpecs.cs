// -----------------------------------------------------------------------
// <copyright file="InMemoryMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.TestKit.Xunit2;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Core.Tests.End2End;

public class InMemoryMqtt311End2EndSpecs : TestKit
{
    // enable debug logging
    public static readonly string Config = """
                                                   akka.loglevel = DEBUG
                                           """;

    
    public InMemoryMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output, config:Config)
    {
        ClientFactory = new MqttClientFactory(Sys);
    }

    public MqttClientConnectOptions DefaultConnectOptions =>
        new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
        {
            Username = "test",
            Password = "test",
            PublishRetryInterval = TimeSpan.FromSeconds(1),
        };
    
    public MqttClientFactory ClientFactory { get; }
    
    [Fact]
    public async Task ShouldConnectAndDisconnect()
    {
        var client = await ClientFactory.CreateInMemoryClient(DefaultConnectOptions);
        
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue(); 
        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }
    
    public static readonly TheoryData<MqttMessage[]> PublishMessages = new()
    {
        new MqttMessage[]
        {
            new MqttMessage("topic", "hello world")
            {
                QoS = QualityOfService.AtMostOnce,
                Retain = false,
            }
        },
        new MqttMessage[]
        {
            new MqttMessage("topic", "hello world 1")
            {
                QoS = QualityOfService.AtMostOnce,
                Retain = false,
            },
            new MqttMessage("topic", "hello world 2")
            {
                QoS = QualityOfService.AtMostOnce,
                Retain = false,
            },
            new MqttMessage("topic", "hello world 3")
            {
                QoS = QualityOfService.AtMostOnce,
                Retain = false,
            }
        },
        new MqttMessage[]
        {
            new MqttMessage("topic", "hello world 1")
            {
                QoS = QualityOfService.AtLeastOnce,
                Retain = false,
            },
        },
        new MqttMessage[]
        {
            new MqttMessage("topic", "hello world 1")
            {
                QoS = QualityOfService.ExactlyOnce,
                Retain = false,
            },
        }
    };
    
    [Theory]
    [MemberData(nameof(PublishMessages))]
    public async Task ShouldPublishMessages(MqttMessage[] messages)
    {
        var client = await ClientFactory.CreateInMemoryClient(DefaultConnectOptions);
        
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        var allReceivedMessage = client.ReceiveMessagesAsync(cts.Token).ToListAsync(cts.Token);
        
        foreach (var message in messages)
        {
            var publishResult = await client.PublishAsync(message, cts.Token);
            publishResult.IsSuccess.Should().BeTrue();
        }
        
        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }
}