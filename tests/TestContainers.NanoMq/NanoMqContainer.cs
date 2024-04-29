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

    /// <summary>
    /// Gets the NanoMq broker address.
    /// </summary>
    /// <returns>The NanoMq broker address.</returns>
    public string GetBrokerAddress()
    {
        var endpoint = new UriBuilder("tcp", Hostname, GetMappedPublicPort(NanoMqBuilder.NanoMqTcpPort));
        return endpoint.ToString();
    }
    
    
}