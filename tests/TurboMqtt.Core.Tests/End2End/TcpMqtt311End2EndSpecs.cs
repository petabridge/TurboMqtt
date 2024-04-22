// -----------------------------------------------------------------------
// <copyright file="TcpMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Hosting;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Core.Tests.End2End;

public class TcpMqtt311End2EndSpecs : TestKit
{
    public TcpMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output)
    {
        _server = new FakeMqttTcpServer(new MqttTcpServerOptions("localhost", 21883), MqttProtocolVersion.V3_1_1, Log);
        _server.Bind();
        ClientFactory = new MqttClientFactory(Sys);
    }
    
    private readonly FakeMqttTcpServer _server;
    public MqttClientFactory ClientFactory { get; }
    
    public MqttClientConnectOptions DefaultConnectOptions =>
        new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
        {
            UserName = "test",
            Password = "test",
        };
    
    public MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883);

    protected override void AfterAll()
    {
        // shut down our local TCP server
        _server.Shutdown();
        base.AfterAll();
    }
    
    [Fact]
    public async Task ShouldConnectAndDisconnect()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();
        await client.DisconnectAsync(cts.Token);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldHandleServerDisconnect()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();
        _server.Shutdown();
        await client.WhenTerminated.WaitAsync(cts.Token);
    }
}