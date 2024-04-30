// -----------------------------------------------------------------------
// <copyright file="EmqxBuilder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TestContainers.Emqx;

/// <inheritdoc cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}" />
[PublicAPI]
public class EmqxBuilder: ContainerBuilder<EmqxBuilder, EmqxContainer, EmqxConfiguration>
{
    public const string EmqxImage = "emqx/emqx:5.5.1";

    public const ushort EmqxTcpPort = 1883;

    public const ushort EmqxSslPort = 8883;

    public const ushort EmqxWebSocketPort = 8083;

    public const ushort EmqxSecureWebSocketPort = 8084;
    
    public const ushort EmqxDashboardPort = 18083;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmqxBuilder" /> class.
    /// </summary>
    public EmqxBuilder()
        : this(new EmqxConfiguration())
    {
        DockerResourceConfiguration = Init().DockerResourceConfiguration;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmqxBuilder" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    private EmqxBuilder(EmqxConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        DockerResourceConfiguration = resourceConfiguration;
    }

    /// <inheritdoc />
    protected override EmqxConfiguration DockerResourceConfiguration { get; }

    /// <inheritdoc />
    public override EmqxContainer Build()
    {
        Validate();
        return new EmqxContainer(DockerResourceConfiguration);
    }

    /// <inheritdoc />
    protected override EmqxBuilder Init()
    {
        return base.Init()
            .WithImage(EmqxImage)
            .WithPortBinding(EmqxTcpPort, true)
            .WithPortBinding(EmqxSslPort, true)
            .WithPortBinding(EmqxWebSocketPort, true)
            .WithPortBinding(EmqxSecureWebSocketPort, true)
            .WithPortBinding(EmqxDashboardPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("EMQX .* is running now!"));
    }

    /// <inheritdoc />
    protected override EmqxBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new EmqxConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override EmqxBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new EmqxConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override EmqxBuilder Merge(EmqxConfiguration oldValue, EmqxConfiguration newValue)
    {
        return new EmqxBuilder(new EmqxConfiguration(oldValue, newValue));
    }
}