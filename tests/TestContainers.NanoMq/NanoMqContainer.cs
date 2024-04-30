// -----------------------------------------------------------------------
// <copyright file="NanoMqContainer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TestContainers.NanoMq;

/// <inheritdoc cref="DockerContainer" />
[PublicAPI]
public class NanoMqContainer: DockerContainer
{
    private readonly NanoMqConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqContainer" /> class.
    /// </summary>
    /// <param name="configuration">The container configuration.</param>
    public NanoMqContainer(NanoMqConfiguration configuration)
        : base(configuration)
    {
        _configuration = configuration;
    }

    public string UserName => "admin";
    public string Password => "public";

    public Uri BrokerTcpAddress =>
        new UriBuilder("nmq-tcp", Hostname, GetMappedPublicPort(NanoMqBuilder.NanoMqTcpPort)).Uri;

    public Uri BrokerWebSocketAddress =>
        new UriBuilder("nmq-ws", Hostname, GetMappedPublicPort(NanoMqBuilder.NanoMqWebSocketPort))
        {
            Path = "mqtt"
        }.Uri;
}