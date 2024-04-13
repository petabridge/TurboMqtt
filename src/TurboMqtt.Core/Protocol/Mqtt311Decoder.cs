// -----------------------------------------------------------------------
// <copyright file="Mqtt311Decoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// Serves as the base class for the MQTT5 Decoder.
/// </summary>
public class Mqtt311Decoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;
    
    public bool TryDecode(in ReadOnlyMemory<byte> additionalData, out IEnumerable<MqttPacket> packet)
    {
        packet = Array.Empty<MqttPacket>();
        return true;
        var rValue = false;
        while (true)
        {
            var packetType = MqttPacketType.Disconnect;
            if (!_remainder.IsEmpty) // check our leftovers first
            {
                packetType = (MqttPacketType)(additionalData.Span[0] & 0xF0);
            }
        }
        // if(_remainder.Length > 0)
        // {
        //     // we have a partial message from earlier - so we need to concatenate it with the new data
        // }
        //
        // if (additionalData.Length < 2) // We need at least 2 bytes to determine the packet type
        // {
        //     packet = default;
        //     return false;
        // }
        //
        // var packetType = (MqttPacketType)(additionalData.Span[0] & 0xF0);
        // switch (packetType)
        // {
        //     case MqttPacketType.Publish:
        //         packet = PublishPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.Connect:
        //         packet = ConnectPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.ConnAck:
        //         packet = ConnAckPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.PubAck:
        //         packet = PubAckPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.PubRec:
        //         packet = PubRecPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.PubRel:
        //         packet = PubRelPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.PubComp:
        //         packet = PubCompPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.Subscribe:
        //         packet = SubscribePacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.SubAck:
        //         packet = SubAckPacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.Unsubscribe:
        //         packet = UnsubscribePacket.Decode(additionalData);
        //         return true;
        //     case MqttPacketType.UnsubAck:
        //         packet = DecodeUnsubscribeAck(additionalData);
        //         return true;
        //     case MqttPacketType.PingReq:
        //         packet = PingReqPacket.Instance;
        //         return true;
        //     case MqttPacketType.PingResp:
        //         packet = PingRespPacket.Instance;
        //         return true;
        //     case MqttPacketType.Disconnect:
        //         packet = DisconnectPacket.Instance;
        //         return true;
        //     case MqttPacketType.Auth: // MQTT 5.0 only - should throw an exception if we see this
        //         throw new NotSupportedException("MQTT 5.0 packets are not supported.");
        //     default:
        //         throw new ArgumentOutOfRangeException(nameof(additionalData), $"Unknown packet type: {packetType}");
        // }
    }
    
    internal static (int headerLength, int bodyLength) GetPacketLength(in ReadOnlySpan<byte> span)
    {
        int multiplier = 1;
        int value = 0;
        int byteCount = 0;

        byte encodedByte;
        do {
            if (byteCount >= span.Length) throw new ArgumentException("The buffer does not contain enough data.");
            
            encodedByte = span[byteCount++];
            value += (encodedByte & 0x7F) * multiplier;
            if (multiplier > 128*128*128)  // 128 * 128 * 128
                throw new Exception("Malformed Remaining Length");

            multiplier *= 128;
        } while ((encodedByte & 128) != 0);

        return (byteCount, value);
    }

    
    // private UnsubscribeAckPacket DecodeUnsubscribeAck(in ReadOnlyMemory<byte> buffer)
    // {
    //     var packet = new UnsubscribeAckPacket();
    //     var span = buffer.Span;
    //     packet.PacketId = span.Slice(2, 2);
    //     return packet;
    // }
}