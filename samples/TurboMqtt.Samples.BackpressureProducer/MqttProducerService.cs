// -----------------------------------------------------------------------
// <copyright file="MqttProducerService.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurboMqtt.Core;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Samples.BackpressureProducer;

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
                var msg = new MqttMessage(config.Topic, $"msg-{i}");

                await client.PublishAsync(msg, stoppingToken);
                //if(i % 1000 == 0)
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