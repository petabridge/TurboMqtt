using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboMqtt.Core;
using TurboMqtt.Samples.BackpressureProducer;

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
        optionsBuilder.BindConfiguration("MqttConfig")
            .ValidateOnStart();
        s.AddTurboMqttClientFactory();
        s.AddHostedService<MqttProducerService>();
    });

var host = builder.Build();

await host.RunAsync();