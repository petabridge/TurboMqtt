namespace TurboMqtt.Core.PacketTypes;

using static MqttPubAckHelpers;

/// <summary>
/// Used to acknowledge the receipt of a Publish packet.
/// </summary>
public sealed class PublishAckPacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.PubAck;
    
    // MQTT 5.0 Optional Properties
    
    /// <summary>
    /// Reason Code for the acknowledgment, available in MQTT 5.0.
    /// This field is optional and provides more detailed acknowledgment information.
    /// </summary>
    public MqttPubAckReasonCode ReasonCode { get; set; }
    
    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// These are key-value pairs that can be sent to provide additional information in the acknowledgment.
    /// </summary>
    public string ReasonString => ReasonCodeToString(ReasonCode);

    public override string ToString()
    {
        return $"PubAck: [PacketIdentifier={PacketId}] [ReasonCode={ReasonCode}]";
    }
}