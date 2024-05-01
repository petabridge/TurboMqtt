// -----------------------------------------------------------------------
// <copyright file="ActiveMqMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using TurboMqtt.Client;
using TurboMqtt.Protocol;
using TurboMqtt.Tests.End2End;
using Xunit.Abstractions;

namespace TurboMqtt.Container.Tests.End2End;

[Collection(nameof(EmqxCollection))]
public class EmqxMqtt311End2EndSpecs: TransportSpecBase
{
    public static readonly Config DebugLogging = 
        """
        akka.loglevel = DEBUG
        """;

    private readonly EmqxFixture _fixture;
    
    public EmqxMqtt311End2EndSpecs(ITestOutputHelper output, EmqxFixture fixture, Config? config = null) : base(output:output, config:config)
    {
        _fixture = fixture;
    }

    public override async Task<IMqttClient> CreateClient()
        => await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

    private MqttClientTcpOptions DefaultTcpOptions => new("localhost", _fixture.MqttPort);

    public override MqttClientConnectOptions DefaultConnectOptions =>
        new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
        {
            UserName = "test",
            Password = "test",
            //PublishRetryInterval = TimeSpan.FromSeconds(1),
            KeepAliveSeconds = 60 // so it's not a relevant factor during testing
        };
}