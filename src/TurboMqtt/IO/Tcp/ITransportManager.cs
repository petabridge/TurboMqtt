// -----------------------------------------------------------------------
// <copyright file="ITransportManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Encapsulates all of the connection-specific details for a given transport and can be
/// used by the <see cref="ClientStreamOwner"/> to initially create and recreate connections.
/// </summary>
internal interface IMqttTransportManager
{
    Task<IMqttTransport> CreateTransportAsync(CancellationToken ct = default);
}