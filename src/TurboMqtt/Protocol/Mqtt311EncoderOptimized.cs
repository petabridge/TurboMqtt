// -----------------------------------------------------------------------
// <copyright file="Mqtt311EncoderOptimized.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

/// <summary>
/// Optimized MQTT 3.1.1 encoder with performance improvements
/// </summary>
public static class Mqtt311EncoderOptimized
{
    // Pre-allocate UTF8 encoding to avoid per-call allocation
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, false);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodePackets(ReadOnlySpan<(MqttPacket packet, PacketSize estimatedSize)> packets, Span<byte> buffer)
    {
        var bytesWritten = 0;
        var workingBuffer = buffer;
        
        foreach (var (packet, size) in packets)
        {
            var newBytesWritten = EncodePacket(packet, workingBuffer, size);
            workingBuffer = workingBuffer.Slice(newBytesWritten);
            bytesWritten += newBytesWritten;
        }

        return bytesWritten;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodePacket(MqttPacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        return packet.PacketType switch
        {
            MqttPacketType.Publish => EncodePublishPacketOptimized((PublishPacket)packet, buffer, estimatedSize),
            MqttPacketType.Connect => EncodeConnectPacketOptimized((ConnectPacket)packet, buffer, estimatedSize),
            MqttPacketType.ConnAck => EncodeConnAckPacketOptimized((ConnAckPacket)packet, buffer, estimatedSize),
            MqttPacketType.Subscribe => EncodeSubscribePacketOptimized((SubscribePacket)packet, buffer, estimatedSize),
            MqttPacketType.SubAck => EncodeSubAckPacketOptimized((SubAckPacket)packet, buffer, estimatedSize),
            MqttPacketType.Unsubscribe => EncodeUnsubscribePacketOptimized((UnsubscribePacket)packet, buffer, estimatedSize),
            MqttPacketType.PubAck or MqttPacketType.PubRec or MqttPacketType.PubRel or MqttPacketType.PubComp or MqttPacketType.UnsubAck 
                => EncodePacketWithIdOnlyOptimized((MqttPacketWithId)packet, buffer),
            MqttPacketType.PingReq or MqttPacketType.PingResp or MqttPacketType.Disconnect 
                => EncodePacketWithFixedHeaderOptimized(packet, buffer),
            MqttPacketType.Auth => throw new NotSupportedException("MQTT 5.0 packets are not supported."),
            _ => throw new ArgumentOutOfRangeException(nameof(packet), $"Unknown packet type: {packet.PacketType}")
        };
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodePublishPacketOptimized(PublishPacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        var offset = 0;
        
        // Fixed header
        buffer[offset++] = CalculateFirstByteOfFixedPacketHeader(packet);
        offset += EncodeVariableLength(buffer.Slice(offset), estimatedSize.ContentSize);
        
        // Variable header - Topic name
        offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.TopicName);
        
        // Packet ID for QoS > 0
        if (packet.QualityOfService > QualityOfService.AtMostOnce)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), packet.PacketId.Value);
            offset += 2;
        }
        
        // Payload
        if (!packet.Payload.IsEmpty)
        {
            packet.Payload.Span.CopyTo(buffer.Slice(offset));
            offset += packet.Payload.Length;
        }
        
        return offset;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeConnectPacketOptimized(ConnectPacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        var offset = 0;
        
        buffer[offset++] = CalculateFirstByteOfFixedPacketHeader(packet);
        offset += EncodeVariableLength(buffer.Slice(offset), estimatedSize.ContentSize);
        offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.ProtocolName);
        buffer[offset++] = (byte)packet.ProtocolVersion;
        buffer[offset++] = packet.Flags.Encode();
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), packet.KeepAliveSeconds);
        offset += 2;
        
        // Payload
        offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.ClientId);
        
        if (packet.Will != null)
        {
            offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.Will.Topic);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), (ushort)packet.Will.Message.Length);
            offset += 2;
            packet.Will.Message.Span.CopyTo(buffer.Slice(offset));
            offset += packet.Will.Message.Length;
        }
        
        if (packet.Flags.UsernameFlag && packet.UserName != null)
        {
            offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.UserName);
        }
        
        if (packet.Flags.PasswordFlag && packet.Password != null)
        {
            offset += EncodeUtf8StringOptimized(buffer.Slice(offset), packet.Password);
        }
        
        return offset;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeConnAckPacketOptimized(ConnAckPacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        buffer[0] = CalculateFirstByteOfFixedPacketHeader(packet);
        buffer[1] = 2; // Remaining length is always 2
        buffer[2] = packet.SessionPresent ? (byte)0x01 : (byte)0x00;
        buffer[3] = (byte)packet.ReasonCode;
        return 4;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeSubscribePacketOptimized(SubscribePacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        var offset = 0;
        
        buffer[offset++] = CalculateFirstByteOfFixedPacketHeader(packet);
        offset += EncodeVariableLength(buffer.Slice(offset), estimatedSize.ContentSize);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), packet.PacketId.Value);
        offset += 2;
        
        foreach (var topic in packet.Topics)
        {
            offset += EncodeUtf8StringOptimized(buffer.Slice(offset), topic.Topic);
            buffer[offset++] = topic.Options.ToByte();
        }
        
        return offset;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeSubAckPacketOptimized(SubAckPacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        var offset = 0;
        
        buffer[offset++] = CalculateFirstByteOfFixedPacketHeader(packet);
        offset += EncodeVariableLength(buffer.Slice(offset), estimatedSize.ContentSize);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), packet.PacketId.Value);
        offset += 2;
        
        foreach (var qos in packet.ReasonCodes)
        {
            buffer[offset++] = (byte)qos;
        }
        
        return offset;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeUnsubscribePacketOptimized(UnsubscribePacket packet, Span<byte> buffer, PacketSize estimatedSize)
    {
        var offset = 0;
        
        buffer[offset++] = CalculateFirstByteOfFixedPacketHeader(packet);
        offset += EncodeVariableLength(buffer.Slice(offset), estimatedSize.ContentSize);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), packet.PacketId.Value);
        offset += 2;
        
        foreach (var topic in packet.Topics)
        {
            offset += EncodeUtf8StringOptimized(buffer.Slice(offset), topic);
        }
        
        return offset;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodePacketWithIdOnlyOptimized(MqttPacketWithId packet, Span<byte> buffer)
    {
        buffer[0] = CalculateFirstByteOfFixedPacketHeader(packet);
        buffer[1] = 2; // Remaining length is always 2 for packet with ID only
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), packet.PacketId.Value);
        return 4;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodePacketWithFixedHeaderOptimized(MqttPacket packet, Span<byte> buffer)
    {
        buffer[0] = CalculateFirstByteOfFixedPacketHeader(packet);
        buffer[1] = 0; // No variable header or payload
        return 2;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeUtf8StringOptimized(Span<byte> buffer, string? str)
    {
        if (string.IsNullOrEmpty(str))
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer, 0);
            return 2;
        }
        
        // For small strings, use stack allocation
        var maxByteCount = Utf8NoBom.GetMaxByteCount(str.Length);
        if (maxByteCount <= 256)
        {
            Span<byte> tempBuffer = stackalloc byte[maxByteCount];
            var actualByteCount = Utf8NoBom.GetBytes(str, tempBuffer);
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)actualByteCount);
            tempBuffer.Slice(0, actualByteCount).CopyTo(buffer.Slice(2));
            return 2 + actualByteCount;
        }
        
        // For larger strings, encode directly
        var byteCount = Utf8NoBom.GetByteCount(str);
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)byteCount);
        Utf8NoBom.GetBytes(str, buffer.Slice(2));
        return 2 + byteCount;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte CalculateFirstByteOfFixedPacketHeader(MqttPacket packet)
    {
        var ret = (byte)((int)packet.PacketType << 4);
        if (packet.Duplicate) ret |= 0x08;
        ret |= (byte)((int)packet.QualityOfService << 1);
        if (packet.RetainRequested) ret |= 0x01;
        return ret;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeVariableLength(Span<byte> buffer, int length)
    {
        var index = 0;
        do
        {
            var encodedByte = length % 128;
            length /= 128;
            if (length > 0)
                encodedByte |= 0x80;
            buffer[index++] = (byte)encodedByte;
        } while (length > 0);
        
        return index;
    }
}