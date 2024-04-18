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
    public InMemoryMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output)
    {
        ClientFactory = new MqttClientFactory(Sys);
    }

    public MqttClientConnectOptions DefaultConnectOptions =>
        new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
        {
            Username = "test",
            Password = "test",
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
}