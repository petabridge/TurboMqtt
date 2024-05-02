// -----------------------------------------------------------------------
// <copyright file="InMemoryMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Client;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

public class InMemoryMqtt311End2EndSpecs : TransportSpecBase
{
    // enable debug logging
    public static readonly string Config = """
                                                   akka.loglevel = DEBUG
                                           """;


    public InMemoryMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output, config: Config)
    {

    }

    public override Task<IMqttClient> CreateClient()
    {
        var client = ClientFactory.CreateInMemoryClient(DefaultConnectOptions);
        return client;
    }
    
    // create a spec where we keep recreating the same client with the same clientId each time we disconnect - it should reconnect successfully
    [Fact]
    public async Task ShouldAutomaticallyReconnectandSubscribeAfterServerDisconnect()
    {
        await RunClientLifeCycle();
        await RunClientLifeCycle();
        await RunClientLifeCycle();

        async Task RunClientLifeCycle()
        {
            var client = await ClientFactory.CreateInMemoryClient(DefaultConnectOptions);

            using var cts = new CancellationTokenSource(RemainingOrDefault);
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue();

            // subscribe
            var subscribeResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtMostOnce, cts.Token);
            subscribeResult.IsSuccess.Should().BeTrue();

            // disconnect
            await client.DisconnectAsync(cts.Token);
            await client.WhenTerminated;
        }
    }
}