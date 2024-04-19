// -----------------------------------------------------------------------
// <copyright file="MqttConfig.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core;

namespace TurboMqtt.Samples.BackpressureProducer;

public class MqttConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "dev-null-producer";
    public string Topic { get; set; } = "dev/null";
    public string User { get; set; } = "dev-null-consumer";
    public string Password { get; set; } = "dev-null-consumer";
    public QualityOfService QoS { get; set; } = 0;
    
    public int MessageCount { get; set; } = 1_000_000;
}