// -----------------------------------------------------------------------
// <copyright file="QualityOfService.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core;

/// <summary>
/// QoS value - corresponds to the MQTT specification.
/// </summary>
public enum QualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}