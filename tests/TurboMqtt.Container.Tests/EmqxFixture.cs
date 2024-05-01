// -----------------------------------------------------------------------
// <copyright file="ActiveMqFixture.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TestContainers.Emqx;

namespace TurboMqtt.Container.Tests;

[CollectionDefinition(nameof(EmqxCollection))]
public class EmqxCollection : ICollectionFixture<EmqxFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

public class EmqxFixture: IAsyncLifetime
{
    public readonly EmqxContainer Container;

    public EmqxFixture()
    {
        Container = new EmqxBuilder()
            .WithEnvironment("EMQX_SESSION__UPGRADE_QOS", "true")
            .Build();
    }

    public int MqttPort => Container.BrokerTcpPort;

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