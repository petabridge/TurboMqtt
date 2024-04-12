namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Packet sent to the client by the server in response to a <see cref="PingReqPacket"/>.
/// </summary>
public sealed class PingRespPacket : MqttPacket
{
    public static readonly PingRespPacket Instance = new PingRespPacket();
    
    private PingRespPacket(){}
    public override MqttPacketType PacketType => MqttPacketType.PingResp;
    
    public override string ToString()
    {
        return "PingResp";
    }
}