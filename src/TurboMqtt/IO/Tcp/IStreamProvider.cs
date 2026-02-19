// -----------------------------------------------------------------------
// <copyright file="IStreamProvider.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Abstraction over the network stream creation process.
/// Implementations create a connected <see cref="Stream"/> from connection parameters,
/// enabling TCP, TLS, and test stream providers to be swapped transparently.
/// </summary>
internal interface IStreamProvider
{
    /// <summary>
    /// Creates a connected <see cref="Stream"/> to the specified host and port.
    /// </summary>
    /// <param name="host">The hostname to connect to.</param>
    /// <param name="port">The port number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected <see cref="Stream"/> ready for reading and writing.</returns>
    Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default);

    /// <summary>
    /// Closes and cleans up the underlying connection resources (socket, etc.).
    /// </summary>
    void Close();
}
