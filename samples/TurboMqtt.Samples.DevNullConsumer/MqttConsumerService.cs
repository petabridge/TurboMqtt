// -----------------------------------------------------------------------
// <copyright file="MqttConsumerService.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Samples.DevNullConsumer;

public sealed class MqttConsumerService : BackgroundService
{
    private readonly ILogger<MqttConsumerService> _logger;
    private readonly IMqttClientFactory _clientFactory;
    private readonly IOptionsSnapshot<MqttConfig> _config;
    private readonly IHostLifetime _lifetime;

    public MqttConsumerService(IMqttClientFactory clientFactory, IOptionsSnapshot<MqttConfig> config,
        ILogger<MqttConsumerService> logger, IHostLifetime lifetime)
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

            var client = await _clientFactory.CreateTcpClient(clientConnectOptions, tcpClientOptions);
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
            var subscribeResult = await client.SubscribeAsync(config.Topic, config.QoS, linkedCts.Token);
            if (!subscribeResult.IsSuccess)
            {
                _logger.LogError("Failed to subscribe to topic {0} - {1}", config.Topic, subscribeResult.Reason);
                return;
            }

            _logger.LogInformation("Subscribed to topic {0}", config.Topic);

            var received = 0;
            var receivedMessage = client.ReceivedMessages;
            while (await receivedMessage.WaitToReadAsync(stoppingToken))
            {
                while (receivedMessage.TryRead(out _))
                {
                    if (++received % 1000 == 0)
                    {
                        _logger.LogInformation("Received {0} messages", received);
                    }
                }
            }

            _logger.LogInformation("Shutting down MQTT consumer service");
            await client.DisconnectAsync(stoppingToken);
        }
        catch(OperationCanceledException){ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in MQTT consumer service");
        }
        finally
        {
            // shut the process down
            _ = _lifetime.StopAsync(default);
        }
        
        Environment.Exit(0);
    }
}