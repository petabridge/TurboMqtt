// -----------------------------------------------------------------------
// <copyright file="ActiveMqMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Testcontainers.ActiveMq;
using TurboMqtt.Client;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

public class ActiveMqMqtt311End2EndSpecs: TransportSpecBase, IAsyncLifetime
{
    protected override bool SkipOnWindows => true;

    public static readonly Config DebugLogging = 
        """
        akka.loglevel = DEBUG
        """;
    
    private readonly ArtemisContainer? _container;
    
    public ActiveMqMqtt311End2EndSpecs(ITestOutputHelper output, Config? config = null) : base(output, config)
    {
        if(!IsOnWindows.Value)
            _container = new ArtemisBuilder()
                .WithImage("apache/activemq-artemis:2.31.2")
                .WithUsername("test")
                .WithPassword("test")
                .WithPortBinding(21883, 1883)
                .Build();
    }

    public override async Task<IMqttClient> CreateClient()
        => await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

    private static MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883);

    public async Task InitializeAsync()
    {
        if(_container is not null)
            await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if(_container is not null)
            await _container.StopAsync();
    }
}