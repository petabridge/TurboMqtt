// -----------------------------------------------------------------------
// <copyright file="OpenTelemetrySupport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using TurboMqtt.Client;
using TurboMqtt.Protocol;

namespace TurboMqtt.Telemetry;

/// <summary>
/// INTERNAL API - defines all TurboMqtt OTEL metric and activity sources.
/// </summary>
internal static class OpenTelemetrySupport
{
    private static readonly string
        Version = typeof(IMqttClient).Assembly.GetName().Version?.ToString() ?? string.Empty;
    
    public static readonly ActivitySource ActivitySource = new("TurboMqtt", Version);
    
    public static readonly Meter Meter = new Meter("TurboMqtt", Version);
    
    public static TagList Mqtt311Tags { get; } = new TagList
    {
        { MqttVersionTag, "3.1.1" }
    };
    
    public static TagList Mqtt5Tags { get; } = new TagList
    {
        { MqttVersionTag, "5.0" }
    };
    
    public const string QoSLevelTag = "qos";
    public const string ClientIdTag = "client.id";
    public const string MqttVersionTag = "mqtt.version";
    public const string PacketTypeTag = "packet.type";
    
    public enum Direction
    {
        Inbound,
        Outbound
    }
    
    public static Counter<long> CreateMessagesCounter(string clientId, MqttProtocolVersion version, Direction direction)
    {
        var tags = version switch
        {
            MqttProtocolVersion.V3_1_1 => Mqtt311Tags,
            MqttProtocolVersion.V5_0 => Mqtt5Tags,
            _ => []
        };
        
        tags.Add(ClientIdTag, clientId);

        var operationName = direction == Direction.Inbound ? "recv_messages" : "sent_messages";
        var description = direction == Direction.Inbound
            ? "The number of MQTT messages received from a broker."
            : "The number of MQTT messages sent to a broker.";
        
        return Meter.CreateCounter<long>(operationName, "packets",
            description, tags);
    }
    
    public static Counter<long> CreateBitRateCounter(string clientId, MqttProtocolVersion version, Direction direction)
    {
        var tags = version switch
        {
            MqttProtocolVersion.V3_1_1 => Mqtt311Tags,
            MqttProtocolVersion.V5_0 => Mqtt5Tags,
            _ => []
        };
        
        tags.Add(ClientIdTag, clientId);

        var operationName = direction == Direction.Inbound ? "recv_bytes" : "sent_bytes";
        var description = direction == Direction.Inbound
            ? "The number of MQTT bytes received from a broker."
            : "The number of MQTT bytes sent to a broker.";
        
        return Meter.CreateCounter<long>(operationName, "bytes",
            description, tags);
    }
}