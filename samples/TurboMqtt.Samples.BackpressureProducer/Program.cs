﻿using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TurboMqtt;
using TurboMqtt.Telemetry;
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
        optionsBuilder.BindConfiguration("MqttConfig");
        s.AddTurboMqttClientFactory();
        s.AddHostedService<MqttProducerService>();
        
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService("BackPressureProducer",
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
        
    });

var host = builder.Build();

await host.RunAsync();