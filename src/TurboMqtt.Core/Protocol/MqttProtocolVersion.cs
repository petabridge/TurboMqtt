// -----------------------------------------------------------------------
// <copyright file="MqttProtocolVersion.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// The version of the MQTT protocol being used.
/// </summary>
public enum MqttVersion
{
    V3_0 = 3,    // Assuming a hypothetical representation for MQTT 3.0
    V3_1_1 = 4,  // MQTT 3.1.1 is usually represented by the protocol level 4
    V5_0 = 5     // MQTT 5.0 is represented by the protocol level 5
}