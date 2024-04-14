// -----------------------------------------------------------------------
// <copyright file="Mqtt311Encoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

public static class Mqtt311Encoder
{
    public static int EncodePackets(IEnumerable<(MqttPacket packet, int estimatedSize)> packets, ref Memory<byte> buffer)
    {
        var bytesWritten = 0;
        foreach (var (packet, size) in packets)
        {
            var newBytesWritten = EncodePacket(packet, ref buffer, size);
            buffer = buffer.Slice(newBytesWritten);
            bytesWritten += newBytesWritten;
        }

        return bytesWritten;
    }
    
    /// <summary>
    /// Encode an <see cref="MqttPacket"/> into a <see cref="Memory{T}"/> buffer using MQTT 3.1.1 protocol.
    /// </summary>
    /// <param name="packet">The MQTT 3.1.1 packet.</param>
    /// <param name="buffer">A buffer big enough to store <see cref="packet"/> - use <see cref="MqttPacketSizeEstimator"/> to precompute this.</param>
    /// <param name="estimatedSize">The expected size of this message</param>
    /// <exception cref="NotSupportedException">For packet types not supported in MQTT 3.1.1.</exception>
    /// <exception cref="ArgumentOutOfRangeException">For unrecognized packet types.</exception>
    /// <returns>The length of actual bytes encoded</returns>
    public static int EncodePacket(MqttPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Publish:
                return EncodePublishPacket((PublishPacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.PubAck:
            case MqttPacketType.PubRec:
            case MqttPacketType.PubRel:
            case MqttPacketType.PubComp:
            case MqttPacketType.UnsubAck:
                return EncodePacketWithIdOnly((MqttPacketWithId)packet, ref buffer, estimatedSize);
            case MqttPacketType.PingReq:
            case MqttPacketType.PingResp:
            case MqttPacketType.Disconnect:
                return EncodePacketWithFixedHeader(packet, ref buffer, estimatedSize);
            case MqttPacketType.Connect:
                return EncodeConnectPacket((ConnectPacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.ConnAck:
                return EncodeConnAckPacket((ConnAckPacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.Subscribe:
                return EncodeSubscribePacket((SubscribePacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.SubAck:
                return EncodeSubAckPacket((SubAckPacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.Unsubscribe:
                return EncodeUnsubscribePacket((UnsubscribePacket)packet, ref buffer, estimatedSize);
            case MqttPacketType.Auth: // MQTT 5.0 only - should throw an exception if we see this
                throw new NotSupportedException("MQTT 5.0 packets are not supported.");
            default:
                throw new ArgumentOutOfRangeException(nameof(packet), $"Unknown packet type: {packet.PacketType}");
        }
    }

    public static int EncodeUnsubscribePacket(UnsubscribePacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0;
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        bytesWritten += WriteUnsignedShort(ref span, (int)packet.PacketId.Value);
        foreach (var topic in packet.Topics)
        {
            bytesWritten += EncodeUtf8String(ref span, topic);
        }

        return bytesWritten;
    }

    public static int EncodeSubAckPacket(SubAckPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0; 
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        bytesWritten += WriteUnsignedShort(ref span, (int)packet.PacketId.Value);
        foreach (var qos in packet.ReasonCodes)
        {
            bytesWritten += WriteByte(ref span, (byte)qos);
        }
        
        return bytesWritten;
    }

    public static int EncodeSubscribePacket(SubscribePacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0;
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        bytesWritten += WriteUnsignedShort(ref span, (int)packet.PacketId.Value);
        foreach (var topic in packet.Topics)
        {
            bytesWritten += EncodeUtf8String(ref span, topic.Topic);
            bytesWritten += WriteByte(ref span, topic.Options.ToByte());
        }

        return bytesWritten;
    }

    public static int EncodeConnAckPacket(ConnAckPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0;
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        if (packet.SessionPresent)
        {
            bytesWritten += WriteByte(ref span, 0x01);
        }
        else
        {
            bytesWritten +=WriteByte(ref span, 0x00);
        }
        bytesWritten += WriteByte(ref span, (byte)packet.ReasonCode);
        return bytesWritten;
    }

    public static int EncodeConnectPacket(ConnectPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0;
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten +=EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        bytesWritten +=EncodeUtf8String(ref span, packet.ProtocolName);
        bytesWritten +=WriteByte(ref span, (byte)packet.ProtocolVersion);
        bytesWritten +=WriteByte(ref span, packet.Flags.Encode());
        bytesWritten +=WriteUnsignedShort(ref span, packet.KeepAliveSeconds);
        
        // payload
        bytesWritten +=EncodeUtf8String(ref span, packet.ClientId);
        if (packet.Will != null)
        {
            bytesWritten += EncodeUtf8String(ref span, packet.Will.Topic);
            bytesWritten += WriteUnsignedShort(ref span, packet.Will.Message.Length);
            packet.Will.Message.Span.CopyTo(span);
            span = span.Slice(packet.Will.Message.Length);
            bytesWritten += packet.Will.Message.Length;
        }

        if (packet.Flags.UsernameFlag)
        {
            Debug.Assert(packet.Username != null, "packet.Username != null");
            bytesWritten +=EncodeUtf8String(ref span, packet.Username);
        }
        
        if (packet.Flags.PasswordFlag)
        {
            Debug.Assert(packet.Password != null, "packet.Password != null");
            bytesWritten += EncodeUtf8String(ref span, packet.Password);
        }
        
        return bytesWritten;
    }

    public static int EncodePublishPacket(PublishPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var bytesWritten = 0;
        var span = buffer.Span;
        bytesWritten += WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        bytesWritten += EncodeUtf8String(ref span, packet.TopicName);
        if (packet.QualityOfService > QualityOfService.AtMostOnce)
        {
            bytesWritten += WriteUnsignedShort(ref span, (int)packet.PacketId.Value);
        }

        if (!packet.Payload.IsEmpty)
        {
            // copy the payload directly into the buffer
            bytesWritten += packet.Payload.Length;
            packet.Payload.Span.CopyTo(span);
        }

        return bytesWritten;
    }
    
    public static int EncodePacketWithFixedHeader(MqttPacket packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var span = buffer.Span;
        WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        WriteByte(ref span, 0);
        return 2;
    }
    
    public static int EncodePacketWithIdOnly(MqttPacketWithId packet, ref Memory<byte> buffer, int estimatedSize)
    {
        var span = buffer.Span;
        WriteByte(ref span, CalculateFirstByteOfFixedPacketHeader(packet));
        EncodeFrameHeaderWithByteShifting(ref span, estimatedSize);
        WriteUnsignedShort(ref span, (int)packet.PacketId.Value);
        return 4;
    }

    public static int WriteByte(ref Span<byte> buffer, byte a)
    {
        buffer[0] = a;
        buffer = buffer.Slice(1);
        return 1;
    }
    
    public static int WriteUnsignedShort(ref Span<byte> buffer, int a)
    {
        buffer[0] = (byte)(a >> 8);
        buffer[1] = (byte)(a & 0xFF);
        buffer = buffer.Slice(2);
        return 2;
    }

    public static int EncodeUtf8String(ref Span<byte> buffer, string str)
    {
        var strLen = Encoding.UTF8.GetByteCount(str);
        WriteUnsignedShort(ref buffer, strLen);
        Encoding.UTF8.GetBytes(str, buffer);
        buffer = buffer.Slice(strLen);
        return 2 + strLen;
    }
    
    public static byte CalculateFirstByteOfFixedPacketHeader(MqttPacket packet)
    {
        var ret = 0;
        ret |= (int)packet.PacketType << 4;
        if (packet.Duplicate)
        {
            ret |= 0x08;
        }
        ret |= (int)packet.QualityOfService << 1;
        if (packet.RetainRequested)
        {
            ret |= 0x01;
        }
       
        // safely encode the ret into a single byte
        return unchecked((byte)ret);
    }
    
    public static int EncodeFrameHeaderWithByteShifting(ref Span<byte> buffer, int length)
    {
        var headerLength = EncodeFrameHeader(ref buffer, 0, length);
        buffer = buffer.Slice(headerLength);
        return headerLength;
    }

    /// <summary>
    /// Returns the number of bytes written to the buffer.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    /// <returns>The number of bytes written</returns>
    public static int EncodeFrameHeader(ref Span<byte> buffer, int offset, int length)
    {
        var remainingLength = length;
        var index = offset;
        do
        {
            var encodedByte = remainingLength % 128;
            remainingLength /= 128;
            if (remainingLength > 0)
            {
                encodedByte |= 0x80;
            }

            buffer[index] = (byte)encodedByte;
            index++;
        } while (remainingLength > 0);
        
        return index - offset;
    }
}