// -----------------------------------------------------------------------
// <copyright file="MqttProducerService.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurboMqtt;
using TurboMqtt.Client;
using TurboMqtt.Protocol;

namespace TurboMqtt.Samples.BackpressureProducer;

internal enum TargetMessageSize
{
    Tiny,
    OneKb,
    EightKb
}

public sealed class MqttProducerService : BackgroundService
{
    private readonly ILogger<MqttProducerService> _logger;
    private readonly IMqttClientFactory _clientFactory;
    private readonly IOptionsSnapshot<MqttConfig> _config;
    private readonly IHostLifetime _lifetime;

    public MqttProducerService(IMqttClientFactory clientFactory,
        IOptionsSnapshot<MqttConfig> config,
        ILogger<MqttProducerService> logger, IHostLifetime lifetime)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
        _lifetime = lifetime;
    }

    // generate a roughly 1kb long JSON payload message as a static variable
    private static readonly byte[] OneKbIshPayload = Encoding.UTF8.GetBytes(
        "{\"id\":\"1234567890\",\"name\":\"John Doe\",\"age\":30,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"}");

    // generate a roughly 8kb long JSON payload message as a static variable
    private static readonly byte[] EightKbIshPayload = Encoding.UTF8.GetBytes(
        "{\"id\":\"1234567890\",\"name\":\"John Doe\",\"age\":30,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\",\"children\":[{\"id\":\"1234567890\",\"name\":\"Jane Doe\",\"age\":5,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jack Doe\",\"age\":10,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jill Doe\",\"age\":15,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jim Doe\",\"age\":20,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jenny Doe\",\"age\":25,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jerry Doe\",\"age\":30,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jasmine Doe\",\"age\":35,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"},{\"id\":\"1234567890\",\"name\":\"Jared Doe\",\"age\":40,\"address\":\"123 Elm St\",\"city\":\"Springfield\",\"state\":\"IL\",\"zip\":\"62701\"}]}");
    
    private static Memory<byte> CreatePayload(int i, TargetMessageSize size)
    {
        return size switch
        {
            TargetMessageSize.Tiny => Encoding.UTF8.GetBytes($"msg-{i}"),
            TargetMessageSize.OneKb => OneKbIshPayload,
            TargetMessageSize.EightKb => EightKbIshPayload,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var config = _config.Value;

            var tcpClientOptions = new MqttClientTcpOptions(config.Host, config.Port);
            var clientConnectOptions = new MqttClientConnectOptions(config.ClientId, MqttProtocolVersion.V3_1_1)
            {
                UserName = config.User,
                Password = config.Password
            };

            await using var client = await _clientFactory.CreateTcpClient(clientConnectOptions, tcpClientOptions);
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(connectCts.Token, stoppingToken);
            var connectResult = await client.ConnectAsync(linkedCts.Token);
            if (!connectResult.IsSuccess)
            {
                _logger.LogError("Failed to connect to MQTT broker at {0}:{1} - {2}", config.Host, config.Port,
                    connectResult.Reason);
                return;
            }

            _logger.LogInformation("Connected to MQTT broker at {0}:{1}", config.Host, config.Port);
            foreach (var i in Enumerable.Range(0, config.MessageCount))
            {
                MqttMessage msg;
                msg = new MqttMessage(config.Topic, CreatePayload(i, TargetMessageSize.EightKb));
                
                // if(i % 3 == 0)
                // {
                //     msg = new MqttMessage(config.Topic, CreatePayload(i, TargetMessageSize.OneKb));
                // }
                // else if(i % 5 == 0)
                // {
                //     msg = new MqttMessage(config.Topic, CreatePayload(i, TargetMessageSize.EightKb));
                // }
                // else
                // {
                //     msg = new MqttMessage(config.Topic, CreatePayload(i, TargetMessageSize.Tiny));
                // }

                await client.PublishAsync(msg, stoppingToken);
                if(i % 1000 == 0)
                {
                    _logger.LogInformation("Published {0} messages", i);
                }
            }

            _logger.LogInformation("Shutting down MQTT consumer service");
            await client.DisconnectAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in MQTT producer service");
        }
        finally
        {
            _ = _lifetime.StopAsync(default);
        }

        Environment.Exit(0);
    }
}