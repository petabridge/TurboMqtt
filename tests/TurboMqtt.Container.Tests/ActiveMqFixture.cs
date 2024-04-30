// -----------------------------------------------------------------------
// <copyright file="ActiveMqFixture.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Testcontainers.ActiveMq;

namespace TurboMqtt.Container.Tests;

[CollectionDefinition(nameof(ActiveMqCollection))]
public class ActiveMqCollection : ICollectionFixture<ActiveMqFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

public class ActiveMqFixture: IAsyncLifetime
{
    public const ushort MqttTcpPort = 1883;
    
    public readonly ArtemisContainer Container;

    public ActiveMqFixture()
    {
        Container = new ArtemisBuilder()
            .WithImage("apache/activemq-artemis:2.31.2")
            .WithUsername("test")
            .WithPassword("test")
            .WithPortBinding(MqttTcpPort, true)
            .Build();
    }

    public int MqttPort => Container.GetMappedPublicPort(MqttTcpPort);

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.StopAsync();
        await Container.DisposeAsync();
    }
}