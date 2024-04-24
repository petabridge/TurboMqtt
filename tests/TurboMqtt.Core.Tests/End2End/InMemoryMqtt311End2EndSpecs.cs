// -----------------------------------------------------------------------
// <copyright file="InMemoryMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Client;
using Xunit.Abstractions;

namespace TurboMqtt.Core.Tests.End2End;

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
}