// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryConfig.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using TurboMqtt.Core.Client;

namespace TurboMqtt.Core.Telemetry;

internal static class OpenTelemetryConfig
{
    private static readonly string
        Version = typeof(IMqttClient).Assembly.GetName().Version?.ToString() ?? string.Empty;
    
    public static readonly ActivitySource ActivitySource = new("TurboMqtt", Version);
    
    public static readonly Meter Meter = new Meter("TurboMqtt", Version);
}