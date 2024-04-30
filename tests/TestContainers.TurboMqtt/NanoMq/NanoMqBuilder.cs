// -----------------------------------------------------------------------
// <copyright file="NanoMqBuilder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TestContainers.NanoMq;

/// <inheritdoc cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}" />
[PublicAPI]
public class NanoMqBuilder: ContainerBuilder<NanoMqBuilder, NanoMqContainer, NanoMqConfiguration>
{
    public const string NanoMqImage = "emqx/nanomq:0.21-slim";

    public const ushort NanoMqTcpPort = 1883;

    public const ushort NanoMqWebSocketPort = 8883;

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqBuilder" /> class.
    /// </summary>
    public NanoMqBuilder()
        : this(new NanoMqConfiguration())
    {
        DockerResourceConfiguration = Init().DockerResourceConfiguration;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqBuilder" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    private NanoMqBuilder(NanoMqConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        DockerResourceConfiguration = resourceConfiguration;
    }

    /// <inheritdoc />
    protected override NanoMqConfiguration DockerResourceConfiguration { get; }

    /// <inheritdoc />
    public override NanoMqContainer Build()
    {
        Validate();
        return new NanoMqContainer(DockerResourceConfiguration);
    }

    /// <inheritdoc />
    protected override NanoMqBuilder Init()
    {
        return base.Init()
            .WithImage(NanoMqImage)
            .WithPortBinding(NanoMqTcpPort, true)
            .WithPortBinding(NanoMqWebSocketPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("NanoMQ Broker is started successfully!"));
    }

    /// <inheritdoc />
    protected override NanoMqBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new NanoMqConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override NanoMqBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new NanoMqConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override NanoMqBuilder Merge(NanoMqConfiguration oldValue, NanoMqConfiguration newValue)
    {
        return new NanoMqBuilder(new NanoMqConfiguration(oldValue, newValue));
    }
}