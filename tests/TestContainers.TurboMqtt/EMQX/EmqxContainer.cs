// -----------------------------------------------------------------------
// <copyright file="EmqxContainer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TestContainers.Emqx;

/// <inheritdoc cref="DockerContainer" />
[PublicAPI]
public class EmqxContainer: DockerContainer
{
    private readonly EmqxConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmqxContainer" /> class.
    /// </summary>
    /// <param name="configuration">The container configuration.</param>
    public EmqxContainer(EmqxConfiguration configuration)
        : base(configuration)
    {
        _configuration = configuration;
    }

    public int BrokerTcpPort => GetMappedPublicPort(EmqxBuilder.EmqxTcpPort);
    
    public int BrokerSslPort => GetMappedPublicPort(EmqxBuilder.EmqxSslPort);
    
    public int BrokerWebSocketPort => GetMappedPublicPort(EmqxBuilder.EmqxWebSocketPort);
    
    public int BrokerSecureWebSocketPort => GetMappedPublicPort(EmqxBuilder.EmqxSecureWebSocketPort);
    
    public int BrokerDashboardPort => GetMappedPublicPort(EmqxBuilder.EmqxDashboardPort);
}