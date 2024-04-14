// -----------------------------------------------------------------------
// <copyright file="Mqtt311Decoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// Serves as the base class for the MQTT5 Decoder.
/// </summary>
public class Mqtt311Decoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    public bool TryDecode(in ReadOnlyMemory<byte> additionalData, out ImmutableList<MqttPacket> packets)
    {
        packets = ImmutableList<MqttPacket>.Empty; 
        var rValue = false;
        
        ReadOnlyMemory<byte> workingBuffer = additionalData;
        
        // combine the buffer with the remainder, if appropriate
        // TODO: optimize this to remove allocations, but for now let's keep parsing simple
        if (!_remainder.IsEmpty)
        {
            var newBuffer = new byte[additionalData.Length + _remainder.Length];
            _remainder.Span.CopyTo(newBuffer);
            additionalData.Span.CopyTo(newBuffer.AsSpan(_remainder.Length));
            workingBuffer = newBuffer;
            _remainder = ReadOnlyMemory<byte>.Empty;
        }
        
        // check to see if we can continue decoding
        if (workingBuffer.Length < 2)
        {
            // save the remainder
            _remainder = workingBuffer;
            return false;
        }
        
        while (workingBuffer.Length > 0)
        {
            var packetType = MqttPacketType.Disconnect;
            var packetSize = 0;
            var headerLength = 0;
            
            // going to do temporary read-only memory slices here - we'll advance the working buffer as we go
            var currentPacket = workingBuffer.Span;

            // extract MqttPacketType
            packetType = (MqttPacketType)(currentPacket[0] & 0xF0);
            currentPacket = currentPacket.Slice(1);
            
            // extract packet size (the packet span will automatically advance past the size header)
            if (!TryGetPacketLength(ref currentPacket, out packetSize))
                return rValue; // we need more data to decode the packet size
            
            // check to see if we have enough data to decode the packet
            if (currentPacket.Length < packetSize)
            {
                // save the remainder
                _remainder = workingBuffer;
                return rValue;
            }
            
            headerLength = workingBuffer.Length - currentPacket.Length; // fixed header + length header size (need this to adjust the working buffer)
            
            // if we've made it this far, we have enough data to decode the packet
            // TODO: some of these packets are only to be handled by client, some only by server - need to enforce that
            /*
             * IMPORTANT: WE DO NOT MODIFY THE WORKING BUFFER UNTIL WE EXIT THE SWITCH STATEMENT
             * If specific methods need to advance the buffer, they should work off of a slice of the working buffer.
             *
             * That's what bufferForMsg is for.
             */
            var bufferForMsg = workingBuffer.Slice(0, headerLength + packetSize);
            
            switch (packetType)
            {
                case MqttPacketType.Publish:
                {
                    packets = packets.Add(DecodePublish(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.PubAck:
                {
                    packets = packets.Add(DecodePubAck(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.PubRec:
                {
                    packets = packets.Add(DecodePubRec(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.PubRel:
                {
                    packets = packets.Add(DecodePubRel(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.PubComp:
                {
                    packets = packets.Add(DecodePubComp(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.PingReq:
                    packets = packets.Add(PingReqPacket.Instance);
                    break;
                case MqttPacketType.PingResp:
                    packets = packets.Add(PingRespPacket.Instance);
                    break;
                case MqttPacketType.Connect:
                {
                    packets = packets.Add(DecodeConnect(ref bufferForMsg, packetSize, headerLength));
                    break;
                }
                case MqttPacketType.ConnAck:
                    packets = ConnAckPacket.Decode(additionalData);
                    break;
                case MqttPacketType.Subscribe:
                    packets = SubscribePacket.Decode(additionalData);
                    break;
                case MqttPacketType.SubAck:
                    packets = SubAckPacket.Decode(additionalData);
                    break;
                case MqttPacketType.Unsubscribe:
                    packets = UnsubscribePacket.Decode(additionalData);
                    break;
                case MqttPacketType.UnsubAck:
                    packets = DecodeUnsubscribeAck(additionalData);
                    break;
               
               
                case MqttPacketType.Disconnect:
                    packets = packets.Add(DecodeDisconnect(ref currentPacket));
                    break;
                case MqttPacketType.Auth: // MQTT 5.0 only - should throw an exception if we see this
                    throw new NotSupportedException("MQTT 5.0 packets are not supported.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(additionalData), $"Unknown packet type: {packetType}");
            }
            
            rValue = true;
            
            // advance the working buffer
            workingBuffer = workingBuffer.Slice(headerLength + packetSize);
        }
        
        return rValue;
    }

    public virtual MqttPacket DecodePubComp(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        var packet = new PubAckPacket();
        bufferForMsg = bufferForMsg.Slice(0, headerLength); // advance past the fixed + size header
        DecodePacketId(ref bufferForMsg, packet, ref packetSize);
        return packet;
    }

    public virtual MqttPacket DecodePubRel(ref ReadOnlyMemory<byte> bufferForMsg, int packetSize, int headerLength)
    {
        var packet = new PubRelPacket();
        bufferForMsg = bufferForMsg.Slice(0, headerLength); // advance past the fixed + size header
        DecodePacketId(ref bufferForMsg, packet, ref packetSize);
        return packet;
    }

    public virtual MqttPacket DecodePubRec(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var packet = new PubAckPacket();
        buffer = buffer.Slice(0, headerLength); // advance past the fixed + size header
        DecodePacketId(ref buffer, packet, ref remainingLength);
        return packet;
    }

    /// <summary>
    /// Helper method to decrease the remaining length of the buffer by a certain amount and enforce a minimum expected length.
    /// </summary>
    /// <param name="remainingLength">The current remaining length we are ticking down</param>
    /// <param name="minExpectedLength">The amount of remaining length we're removing</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void DecreaseRemainingLength(ref int remainingLength, int minExpectedLength)
    {
        if (remainingLength < minExpectedLength)
        {
            throw new ArgumentOutOfRangeException($"Remaining {remainingLength} is smaller than {minExpectedLength} - we are in an illegal state.");
        }
        remainingLength -= minExpectedLength;
    }
    
    protected static ushort DecodeUnsignedShort(ref ReadOnlyMemory<byte> buffer, ref int remainingLength)
    {
        var span = buffer.Span;
        var value = (ushort)(span[0] << 8 | span[1]);
        buffer = buffer.Slice(2);
        DecreaseRemainingLength(ref remainingLength, 2);
        return value;
    }
    
    public virtual PublishPacket DecodePublish(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var buffSpan = buffer.Span;
        var qualityOfService = (QualityOfService)((buffSpan[0] & 0x06) >> 1);
        var duplicate = (buffSpan[0] & 0x08) == 0x08;
        var retain = (buffSpan[0] & 0x01) == 0x01;
        buffer = buffer.Slice(0, headerLength); // advance past the fixed + size header
        
        var topicName = DecodeString(ref buffer, ref remainingLength, 2, int.MaxValue);
        // TODO: validate topic name
        var packet = new PublishPacket(qualityOfService, duplicate, retain, topicName);
        if (qualityOfService > QualityOfService.AtMostOnce)
        {
            DecodePacketId(ref buffer, packet, ref remainingLength);
        }
        
        if(remainingLength > 0)
        {
            packet.Payload = buffer.Slice(0, remainingLength);
            DecreaseRemainingLength(ref remainingLength, buffer.Length);
            remainingLength = 0;
        }
        else
        {
            packet.Payload = ReadOnlyMemory<byte>.Empty;
        }

        return packet;
    }
    
    public virtual PubAckPacket DecodePubAck(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        var packet = new PubAckPacket();
        buffer = buffer.Slice(0, headerLength); // advance past the fixed + size header
        DecodePacketId(ref buffer, packet, ref remainingLength);
        return packet;
    }
    
    protected static void DecodePacketId(ref ReadOnlyMemory<byte> buffer, MqttPacketWithId packet, ref int remainingLength)
    {
        var packetId = DecodeUnsignedShort(ref buffer, ref remainingLength);
        packet.PacketId = packetId;
        if (packetId == 0)
            throw new ArgumentOutOfRangeException(nameof(packetId), "PacketId cannot be 0.");
    }
    
    protected static string DecodeString(ref ReadOnlyMemory<byte> buffer, ref int remainingLength) => DecodeString(ref buffer, ref remainingLength, 0, int.MaxValue);
    
    protected static string DecodeString(ref ReadOnlyMemory<byte> buffer, ref int remainingLength, int minBytes, int maxBytes)
    {
        var length = DecodeUnsignedShort(ref buffer, ref remainingLength);
        
        if (length < minBytes || length > maxBytes)
        {
            throw new ArgumentOutOfRangeException($"String length {length} is outside of expected range [{minBytes}, {maxBytes}]");
        }
        
        if(length == 0)
            return string.Empty;
        
        DecreaseRemainingLength(ref remainingLength, length);
        var value = Encoding.UTF8.GetString(buffer.Span.Slice(0, length));
        buffer = buffer.Slice(0, length);
        return value;
    }
    
    protected virtual ConnectPacket DecodeConnect(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        buffer = buffer.Slice(0, headerLength); // advance past the fixed + size header
        var protocolName = DecodeString(ref buffer, ref remainingLength);
        if (!protocolName.Equals("MQTT", StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(protocolName), $"Invalid protocol name: {protocolName}");
        
        var protocolLevel = (MqttProtocolVersion)buffer.Span[0];
        DecreaseRemainingLength(ref remainingLength, 1);
        buffer = buffer.Slice(0,1);
        
        var flags = ConnectFlags.Decode(buffer.Span[0]);
        DecreaseRemainingLength(ref remainingLength, 1);
        buffer = buffer.Slice(0, 1);

        var packet = new ConnectPacket(protocolLevel)
        {
            Flags = flags,
            ProtocolName = protocolName
        };

        if(flags is { PasswordFlag: true, UsernameFlag: false })
            throw new ArgumentOutOfRangeException(nameof(flags), "Password flag is set, but username flag is not. [MQTT-3.1.2-22]");
    }
    
    protected virtual DisconnectPacket DecodeDisconnect(ref ReadOnlyMemory<byte> buffer, int remainingLength, int headerLength)
    {
        return DisconnectPacket.Instance;
    }

    internal static bool TryGetPacketLength(ref ReadOnlySpan<byte> span, out int bodyLength)
    {
        int multiplier = 1;
        int value = 0;
        int byteCount = 0;
        
        bodyLength = 0;

        byte encodedByte;
        do
        {
            if (byteCount >= span.Length) return false;

            encodedByte = span[byteCount++];
            value += (encodedByte & 0x7F) * multiplier;
            if (multiplier > 128 * 128 * 128) // 128 * 128 * 128
                return false;

            multiplier *= 128;
        } while ((encodedByte & 128) != 0);
        
        bodyLength = value;
        span = span.Slice(0, byteCount);

        return true;
    }


    // private UnsubscribeAckPacket DecodeUnsubscribeAck(in ReadOnlyMemory<byte> buffer)
    // {
    //     var packet = new UnsubscribeAckPacket();
    //     var span = buffer.Span;
    //     packet.PacketId = span.Slice(2, 2);
    //     return packet;
    // }
}