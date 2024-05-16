# TurboMqtt

[TurboMqtt](https://github.com/petabridge/TurboMqtt) is a high-speed Message Queue Telemetry Transport (MQTT) client designed to support large-scale IOT workloads, handling over 100k msg/s from any MQTT 3.1.1+ broker.

![TurboMqtt logo](https://raw.githubusercontent.com/petabridge/TurboMqtt/dev/docs/logo.png)

TurboMqtt is written on top of [Akka.NET](https://getakka.net/) and Akka.Streams, which is the secret to its efficient use of resources and high throughput.

## Key Features

* MQTT 3.1.1 support;
* Extremely high performance - hundreds of thousands of messages per second;
* Extremely resource-efficient - pools memory and leverages asynchronous I/O best practices;
* Extremely robust fault tolerance - this is one of [Akka.NET's great strengths](https://petabridge.com/blog/akkadotnet-actors-restart/) and we've leveraged it in TurboMqtt;
* Supports all MQTT quality of service levels, with automatic publishing retries for QoS 1 and 2;
* Full [OpenTelemetry](https://opentelemetry.io/) support;
* Automatic retry-reconnect in broker disconnect scenarios;
* Full support for IAsyncEnumerable and backpressure on the receiver side;
* Automatically de-duplicates packets on the receiver side; and
* Automatically acks QoS 1 and QoS 2 packets.

Simple interface that works at very high rates of speed with minimal resource utilization.

## Documentation

1. [QuickStart](https://github.com/petabridge/TurboMqtt/tree/dev?tab=readme-ov-file#quickstart)
2. [Performance](https://github.com/petabridge/TurboMqtt/blob/dev/docs/Performance.md)
3. [OpenTelemetry Support](https://github.com/petabridge/TurboMqtt/blob/dev/docs/Telemetry.md)
4. [MQTT 3.1.1 Roadmap](https://github.com/petabridge/TurboMqtt/issues/66)
5. [MQTT 5.0 Roadmap](https://github.com/petabridge/TurboMqtt/issues/67)
6. [MQTT over Quic Roadmap](https://github.com/petabridge/TurboMqtt/issues/68)

## QuickStart

To get started with TurboMqtt:

```
dotnet add package TurboMqtt
```

And from there, you can call `AddTurboMqttClientFactory` on your `IServiceCollection`:

```csharp
var builder = new HostBuilder();

builder
    .ConfigureAppConfiguration(configBuilder =>
    {
        configBuilder
            .AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .ConfigureServices(s =>
    {
        s.AddTurboMqttClientFactory();

        // HostedService is going to use TurboMqtt
        s.AddHostedService<MqttProducerService>();
        
    });

var host = builder.Build();

await host.RunAsync();
```

And inject `IMqttClientFactory` into your ASP.NET Controllers, SignalR Hubs, gRPC services, `IHostedService`s, etc and create `IMqttClient` instances:

```csharp
var tcpClientOptions = new MqttClientTcpOptions(config.Host, config.Port);
var clientConnectOptions = new MqttClientConnectOptions(config.ClientId, MqttProtocolVersion.V3_1_1)
{
    UserName = config.User,
    Password = config.Password
};

await using IMqttClient client = await _clientFactory.CreateTcpClient(clientConnectOptions, tcpClientOptions);

// connect to the broker
var connectResult = await client.ConnectAsync(linkedCts.Token);
            
if (!connectResult.IsSuccess)
{
    _logger.LogError("Failed to connect to MQTT broker at {0}:{1} - {2}", config.Host, config.Port,
        connectResult.Reason);
    return;
}
```

### Publishing Messages

Publishing messages with TurboMqtt is easy:

```csharp
foreach (var i in Enumerable.Range(0, config.MessageCount))
{
    var msg = new MqttMessage(config.Topic, CreatePayload(i, TargetMessageSize.EightKb))
                {
                    QoS = QualityOfService.AtLeastOnce
                };

    IPublishResult publishResult = await client.PublishAsync(msg, stoppingToken);
    if(i % 1000 == 0)
    {
        _logger.LogInformation("Published {0} messages", i);
    }
}
```

The `IPublishResult.IsSuccess` property will return `true` when:

1. `QualityOfService.AtMostOnce` (QoS 0) - as soon as the message has queued for delivery;
2. `QualityOfService.AtLeastOnce` (QoS 1) - after we've received a `PubAck` from the broker, confirming receipt; and
3. `QualityOfService.ExactlyOnce` (QoS 2) - after we've completed the full MQTT QoS 2 exchange and received the final `PubComp` acknowledgement from the broker.

**TurboMqtt will automatically retry delivery of messages in the event of overdue ACKs from the broker**.

### Receiving Messages

TurboMqtt is backpressure-aware and thus exposes the stream of received `MqttMessage`s to consumers via [`System.Threading.Channel<MqttMessage>`](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels):

```csharp
ISubscribeResult subscribeResult = await client.SubscribeAsync(config.Topic, config.QoS, linkedCts.Token);
if (!subscribeResult.IsSuccess)
{
    _logger.LogError("Failed to subscribe to topic {0} - {1}", config.Topic, subscribeResult.Reason);
    return;
}

_logger.LogInformation("Subscribed to topic {0}", config.Topic);

var received = 0;
ChannelRead<MqttMessage> receivedMessages = client.ReceivedMessages;
while (await receivedMessages.WaitToReadAsync(stoppingToken))
{
    while (receivedMessages.TryRead(out MqttMessage m))
    {    
    	_logger.LogInformation("Received message [{0}] for topic [{1}]", m.Payload,  m.Topic);
    }
}
```

If we've subscribed using `QualityOfService.AtMostOnce` or `QualityOfService.ExactlyOnce`, TurboMqtt has already fully ACKed the message for you by the time you receive it and we use a per-topic de-duplication buffer to detect and remove duplicates.
 
## Licensing

TurboMqtt is available under the Apache 2.0 license.

## Support

To get support with TurboMqtt, either fill out the help form on Sdkbin or [file an issue on the TurboMqtt repository](https://github.com/petabridge/TurboMqtt/issues).

TurboMqtt developed and maintained by [Petabridge](https://petabridge.com/), the company behind Akka.NET.