// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryConfig.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TurboMqtt.Core.Client;

namespace TurboMqtt.Core.Telemetry;

/// <summary>
/// Used to configure the OpenTelemetry SDK for TurboMqtt.
/// </summary>
public static class OpenTelemetryConfig
{
    /// <summary>
    /// Adds TurboMqtt metrics to the OpenTelemetry SDK.
    /// </summary>
    /// <remarks>
    /// You must configure <see cref="MqttClientConnectOptions.EnableOpenTelemetry"/> in order to enable this feature
    /// on <see cref="IMqttClient"/> instances.
    /// </remarks>
    public static MeterProviderBuilder AddTurboMqttMetrics(this MeterProviderBuilder builder)
    {
        return builder.AddMeter(OpenTelemetrySupport.Meter.Name);
    }

    /// <summary>
    /// Adds TurboMqtt tracing to the OpenTelemetry SDK.
    /// </summary>
    /// <remarks>
    /// **Only works with MQTT 5.0 protocol**
    ///
    /// You must configure <see cref="MqttClientConnectOptions.EnableOpenTelemetry"/> in order to enable this feature
    /// on <see cref="IMqttClient"/> instances.
    /// </remarks>
    public static TracerProviderBuilder AddTurboMqttTracing(this TracerProviderBuilder builder)
    {
        return builder.AddSource(OpenTelemetrySupport.ActivitySource.Name);
    }
}