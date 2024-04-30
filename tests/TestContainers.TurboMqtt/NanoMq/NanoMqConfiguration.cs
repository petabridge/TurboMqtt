namespace TestContainers.NanoMq;

/// <inheritdoc cref="ContainerConfiguration" />
[PublicAPI]
public class NanoMqConfiguration : ContainerConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqConfiguration" /> class.
    /// </summary>
    public NanoMqConfiguration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public NanoMqConfiguration(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public NanoMqConfiguration(IContainerConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public NanoMqConfiguration(NanoMqConfiguration resourceConfiguration)
        : this(new NanoMqConfiguration(), resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoMqConfiguration" /> class.
    /// </summary>
    /// <param name="oldValue">The old Docker resource configuration.</param>
    /// <param name="newValue">The new Docker resource configuration.</param>
    public NanoMqConfiguration(NanoMqConfiguration oldValue, NanoMqConfiguration newValue)
        : base(oldValue, newValue)
    {
    }
}