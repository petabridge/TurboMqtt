# TurboMqtt Telemetry

TurboMqtt supports [OpenTelemetry](https://opentelemetry.io/) - this page explains how to enable it.

## Subscribing to TurboMqtt `Meter` and `ActivitySource`

We provide some helpful extension methods to be used alongside the `OpenTelemetryBuilder` type:

* `AddTurboMqttMetrics()` - subscribes to all TurboMqtt metric sources.
* `AddTurboMqttTracing()` - subscribes to all TurboMqtt trace sources, which we currently do not support.

An end to end example of how to use these settings:

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
        // parse MqttConfig from appsettings.json
        var optionsBuilder = s.AddOptions<MqttConfig>();
        optionsBuilder.BindConfiguration("MqttConfig");
        s.AddTurboMqttClientFactory();

        var resourceBuilder = ResourceBuilder.CreateDefault().AddService("DevNullConsumer",
            "TurboMqtt.Examples",
            serviceInstanceId: Dns.GetHostName());

        s.AddOpenTelemetry()
            .WithMetrics(m =>
            {
                m
                    .SetResourceBuilder(resourceBuilder)
                    .AddTurboMqttMetrics()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri("http://localhost:4317"); // Replace with the appropriate endpoint
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc; // or HttpProtobuf
                    });
            })
            .WithTracing(t =>
            {
                t
                    .SetResourceBuilder(resourceBuilder)
                    .AddTurboMqttTracing()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri("http://localhost:4317"); // Replace with the appropriate endpoint
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc; // or HttpProtobuf
                    });
            });
        s.AddHostedService<MqttConsumerService>();
    });

var host = builder.Build();

await host.RunAsync();
```

## Collected Metrics

What metrics does TurboMqtt expose?

* `recv_messages` - by `clientId`, `PacketType`, `MqttProtocolVersion`
* `recv_bytes` - by `clientId`, `MqttProtocolVersion`
* `sent_messages` - by `clientId`, `PacketType`, `MqttProtocolVersion`
* `sent_bytes` - by `clientId`, `MqttProtocolVersion`

## Disabling TurboMqtt OpenTelemetry for Performance Reasons

If you want to disable the low-level emission of OpenTelemetry metrics and traces, we support that on the `MqttClientConnectOptions` class you have to use when creating an `IMqttClient`:

```csharp
var tcpClientOptions = new MqttClientTcpOptions(config.Host, config.Port);
var clientConnectOptions = new MqttClientConnectOptions(config.ClientId, MqttProtocolVersion.V3_1_1)
{
    UserName = config.User,
    Password = config.Password,
    KeepAliveSeconds = 5,
    EnableOpenTelemetry = false // disable telemetry
};

var client = await _clientFactory.CreateTcpClient(clientConnectOptions, tcpClientOptions);
```