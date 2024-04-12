namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Base for all MQTT packets.
/// </summary>
public abstract class MqttPacket
{
    public abstract MqttPacketType PacketType { get; }
    
    public virtual bool Duplicate => false;
    
    public virtual QualityOfService QualityOfService => QualityOfService.AtMostOnce;
    
    public virtual bool RetainRequested => false;
    
    public override string ToString()
    {
        return $"{GetType().Name}[Type={PacketType}, QualityOfService={QualityOfService}, Duplicate={Duplicate}, Retain={RetainRequested}]";
    }
}

/// <summary>
/// Base for MQTT packets that require a packet identifier.
/// </summary>
public abstract class MqttPacketWithId : MqttPacket
{
    /// <summary>
    /// The unique identifier assigned to the packet.
    /// </summary>
    /// <remarks>
    /// Not all packets require an identifier.
    /// </remarks>
    public NonZeroUInt32 PacketId { get; set; }
}