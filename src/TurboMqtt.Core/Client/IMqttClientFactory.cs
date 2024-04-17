// -----------------------------------------------------------------------
// <copyright file="IMqttClientFactory.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Config;

namespace TurboMqtt.Core.Client;

public interface IMqttClientFactory
{
    /// <summary>
    /// Creates a TCP-based MQTT client.
    /// </summary>
    /// <param name="tcpOptions"></param>
    /// <returns></returns>
    IMqttClient CreateTcpClient(MqttClientTcpOptions tcpOptions);
}