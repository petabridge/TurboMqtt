// -----------------------------------------------------------------------
// <copyright file="Mqtt5Encoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

/// <summary>
/// Encodes MQTT 5.0 packets into binary frames.
/// </summary>
/// <remarks>
/// The key difference from MQTT 3.1.1 is that every variable header is followed by a
/// Properties section: a Variable Byte Integer (property length) followed by typed
/// property key-value pairs.  ACK packets use a compact 2-byte form when the Reason
/// Code is Success and there are no properties (OASIS §3.4.2.1 et al.).
/// </remarks>
internal static class Mqtt5Encoder
{
    // ── Public API ──────────────────────────────────────────────────────────

    public static int EncodePackets(
        IEnumerable<(MqttPacket packet, PacketSize estimatedSize)> packets,
        ref Memory<byte> buffer)
    {
        var bytesWritten = 0;
        var workingBuffer = buffer;
        foreach (var (packet, size) in packets)
        {
            var written = EncodePacket(packet, ref workingBuffer, size);
            workingBuffer = workingBuffer.Slice(written);
            bytesWritten += written;
        }

        return bytesWritten;
    }

    /// <summary>
    /// Encodes a single MQTT 5.0 packet into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="packet">The packet to encode.</param>
    /// <param name="buffer">
    /// A buffer at least <see cref="PacketSize.TotalSize"/> bytes large (use
    /// <see cref="MqttPacketSizeEstimator"/> to pre-compute this).
    /// </param>
    /// <param name="estimatedSize">Upper-bound size from the estimator; used only for the buffer-size guard.</param>
    /// <returns>The number of bytes actually written.</returns>
    public static int EncodePacket(MqttPacket packet, ref Memory<byte> buffer, PacketSize estimatedSize)
    {
        if (buffer.Length < estimatedSize.TotalSize)
            throw new ArgumentException("Buffer is too small for the estimated packet size.");

        return packet.PacketType switch
        {
            MqttPacketType.Connect => EncodeConnectPacket((ConnectPacket)packet, ref buffer),
            MqttPacketType.ConnAck => EncodeConnAckPacket((ConnAckPacket)packet, ref buffer),
            MqttPacketType.Publish => EncodePublishPacket((PublishPacket)packet, ref buffer),
            MqttPacketType.PubAck => EncodePubAckPacket((PubAckPacket)packet, ref buffer),
            MqttPacketType.PubRec => EncodePubRecPacket((PubRecPacket)packet, ref buffer),
            MqttPacketType.PubRel => EncodePubRelPacket((PubRelPacket)packet, ref buffer),
            MqttPacketType.PubComp => EncodePubCompPacket((PubCompPacket)packet, ref buffer),
            MqttPacketType.Subscribe => EncodeSubscribePacket((SubscribePacket)packet, ref buffer),
            MqttPacketType.SubAck => EncodeSubAckPacket((SubAckPacket)packet, ref buffer),
            MqttPacketType.Unsubscribe => EncodeUnsubscribePacket((UnsubscribePacket)packet, ref buffer),
            MqttPacketType.UnsubAck => EncodeUnsubAckPacket((UnsubAckPacket)packet, ref buffer),
            MqttPacketType.PingReq or MqttPacketType.PingResp => EncodePingPacket(packet, ref buffer),
            MqttPacketType.Disconnect => EncodeDisconnectPacket((DisconnectPacket)packet, ref buffer),
            MqttPacketType.Auth => EncodeAuthPacket((AuthPacket)packet, ref buffer),
            _ => throw new ArgumentOutOfRangeException(nameof(packet), $"Unknown packet type: {packet.PacketType}")
        };
    }

    // ── CONNECT ─────────────────────────────────────────────────────────────

    /// <summary>
    /// CONNECT packet structure (OASIS §3.1):
    /// Fixed Header | Remaining Length | Protocol Name | Protocol Level (5) | Connect Flags |
    /// Keep Alive | Connect Properties Length | Connect Properties |
    /// [Client ID | [Will Props Len | Will Props | Will Topic | Will Payload] |
    ///  [Username] | [Password]]
    /// </summary>
    public static int EncodeConnectPacket(ConnectPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var connectPropsSize = ComputeConnectPropertiesSize(packet);
        var willPropsSize = packet.Flags.WillFlag && packet.Will != null
            ? ComputeWillPropertiesSize(packet.Will)
            : 0;
        var contentSize = ComputeConnectContentSize(packet, connectPropsSize, willPropsSize);

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);

        // Variable header
        bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, "MQTT");
        bytesWritten += Mqtt311Encoder.WriteByte(ref span, 5); // Protocol Level 5
        bytesWritten += Mqtt311Encoder.WriteByte(ref span, packet.Flags.Encode());
        bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.KeepAliveSeconds);

        // Connect Properties
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)connectPropsSize);
        bytesWritten += WriteConnectProperties(ref span, packet);

        // Payload: Client ID
        bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, packet.ClientId);

        // Payload: Will (if present)
        if (packet.Flags.WillFlag && packet.Will != null)
        {
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)willPropsSize);
            bytesWritten += WriteWillProperties(ref span, packet.Will);

            bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, packet.Will.Topic);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.Will.Message.Length);
            packet.Will.Message.Span.CopyTo(span);
            span = span.Slice(packet.Will.Message.Length);
            bytesWritten += packet.Will.Message.Length;
        }

        // Payload: Username, Password
        if (packet.Flags.UsernameFlag && packet.UserName != null)
            bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, packet.UserName);

        if (packet.Flags.PasswordFlag && packet.Password != null)
            bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, packet.Password);

        return bytesWritten;
    }

    // ── CONNACK ─────────────────────────────────────────────────────────────

    public static int EncodeConnAckPacket(ConnAckPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = ComputeConnAckPropertiesSize(packet);
        // content = session present (1) + reason code (1) + VBI(propsSize) + propsSize
        var contentSize = 2 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteByte(ref span, packet.SessionPresent ? (byte)0x01 : (byte)0x00);
        bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)packet.ReasonCode);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
        bytesWritten += WriteConnAckProperties(ref span, packet);

        return bytesWritten;
    }

    // ── PUBLISH ─────────────────────────────────────────────────────────────

    public static int EncodePublishPacket(PublishPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = ComputePublishPropertiesSize(packet);
        var topicBytes = Encoding.UTF8.GetByteCount(packet.TopicName);
        // content = topic (2+len) + [packet id (2)] + VBI(propsSize) + propsSize + payload
        var contentSize = 2 + topicBytes
            + (packet.QualityOfService > QualityOfService.AtMostOnce ? 2 : 0)
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.Payload.Length;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, packet.TopicName);

        if (packet.QualityOfService > QualityOfService.AtMostOnce)
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);

        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
        bytesWritten += WritePublishProperties(ref span, packet);

        if (!packet.Payload.IsEmpty)
        {
            packet.Payload.Span.CopyTo(span);
            bytesWritten += packet.Payload.Length;
        }

        return bytesWritten;
    }

    // ── PUBACK ──────────────────────────────────────────────────────────────

    public static int EncodePubAckPacket(PubAckPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var hasProps = !string.IsNullOrEmpty(packet.ReasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = packet.ReasonCode == MqttPubAckReasonCode.Success && !hasProps;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));

        if (isCompact)
        {
            // Compact: Remaining Length = 2 (just packet ID)
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, 0x02);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        }
        else
        {
            var propsSize = ComputeAckPropertiesSize(packet.ReasonString, packet.UserProperties);
            var contentSize = 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
            bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)packet.ReasonCode);
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
            bytesWritten += WriteAckProperties(ref span, packet.ReasonString, packet.UserProperties);
        }

        return bytesWritten;
    }

    // ── PUBREC ──────────────────────────────────────────────────────────────

    public static int EncodePubRecPacket(PubRecPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var reasonCode = packet.ReasonCode ?? PubRecReasonCode.Success;
        var reasonString = packet.ReasonString;
        var hasProps = !string.IsNullOrEmpty(reasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubRecReasonCode.Success && !hasProps;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));

        if (isCompact)
        {
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, 0x02);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        }
        else
        {
            var propsSize = ComputeAckPropertiesSize(reasonString, packet.UserProperties);
            var contentSize = 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
            bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)reasonCode);
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
            bytesWritten += WriteAckProperties(ref span, reasonString, packet.UserProperties);
        }

        return bytesWritten;
    }

    // ── PUBREL ──────────────────────────────────────────────────────────────

    public static int EncodePubRelPacket(PubRelPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var reasonCode = packet.ReasonCode ?? PubRelReasonCode.Success;
        var reasonString = packet.ReasonString;
        var hasProps = !string.IsNullOrEmpty(reasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubRelReasonCode.Success && !hasProps;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));

        if (isCompact)
        {
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, 0x02);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        }
        else
        {
            var propsSize = ComputeAckPropertiesSize(reasonString, packet.UserProperties);
            var contentSize = 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
            bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)reasonCode);
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
            bytesWritten += WriteAckProperties(ref span, reasonString, packet.UserProperties);
        }

        return bytesWritten;
    }

    // ── PUBCOMP ─────────────────────────────────────────────────────────────

    public static int EncodePubCompPacket(PubCompPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var reasonCode = packet.ReasonCode ?? PubCompReasonCode.Success;
        var reasonString = packet.ReasonString;
        var hasProps = !string.IsNullOrEmpty(reasonString)
            || (packet.UserProperties != null && packet.UserProperties.Count > 0);
        var isCompact = reasonCode == PubCompReasonCode.Success && !hasProps;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));

        if (isCompact)
        {
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, 0x02);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        }
        else
        {
            var propsSize = ComputeAckPropertiesSize(reasonString, packet.UserProperties);
            var contentSize = 2 + 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
            bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
            bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)reasonCode);
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
            bytesWritten += WriteAckProperties(ref span, reasonString, packet.UserProperties);
        }

        return bytesWritten;
    }

    // ── SUBSCRIBE ───────────────────────────────────────────────────────────

    public static int EncodeSubscribePacket(SubscribePacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = ComputeSubscribePropertiesSize(packet);
        var topicsPayloadSize = packet.Topics.Sum(t => 2 + Encoding.UTF8.GetByteCount(t.Topic) + 1);
        // content = packet ID (2) + VBI(propsSize) + propsSize + topics
        var contentSize = 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + topicsPayloadSize;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
        bytesWritten += WriteSubscribeProperties(ref span, packet);

        foreach (var topic in packet.Topics)
        {
            bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, topic.Topic);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, topic.Options.ToByte());
        }

        return bytesWritten;
    }

    // ── SUBACK ──────────────────────────────────────────────────────────────

    public static int EncodeSubAckPacket(SubAckPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = ComputeSubAckPropertiesSize(packet);
        // content = packet ID (2) + VBI(propsSize) + propsSize + reason codes
        var contentSize = 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.ReasonCodes.Count;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
        bytesWritten += WriteSubAckProperties(ref span, packet);

        foreach (var rc in packet.ReasonCodes)
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)rc);

        return bytesWritten;
    }

    // ── UNSUBSCRIBE ─────────────────────────────────────────────────────────

    public static int EncodeUnsubscribePacket(UnsubscribePacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = packet.UserProperties != null && packet.UserProperties.Count > 0
            ? ComputeUserPropertiesSize(packet.UserProperties)
            : 0;
        var topicsPayloadSize = packet.Topics.Sum(t => 2 + Encoding.UTF8.GetByteCount(t));
        var contentSize = 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + topicsPayloadSize;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);

        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);

        foreach (var topic in packet.Topics)
            bytesWritten += Mqtt311Encoder.EncodeUtf8String(ref span, topic);

        return bytesWritten;
    }

    // ── UNSUBACK ────────────────────────────────────────────────────────────

    public static int EncodeUnsubAckPacket(UnsubAckPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        var contentSize = 2
            + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize)
            + propsSize
            + packet.ReasonCodes.Count;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteUnsignedShort(ref span, packet.PacketId.Value);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);

        if (!string.IsNullOrEmpty(packet.ReasonString))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);

        foreach (var rc in packet.ReasonCodes)
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)rc);

        return bytesWritten;
    }

    // ── PINGREQ / PINGRESP ──────────────────────────────────────────────────

    public static int EncodePingPacket(MqttPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        Mqtt311Encoder.WriteByte(ref span, 0x00);
        return 2;
    }

    // ── DISCONNECT ──────────────────────────────────────────────────────────

    public static int EncodeDisconnectPacket(DisconnectPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var reasonCode = packet.ReasonCode ?? DisconnectReasonCode.NormalDisconnection;
        var propsSize = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (!string.IsNullOrEmpty(packet.ServerReference))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ServerReference);
        if (packet.SessionExpiryInterval.HasValue)
            propsSize += 1 + 4;
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        var isCompact = reasonCode == DisconnectReasonCode.NormalDisconnection && propsSize == 0;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));

        if (isCompact)
        {
            // Compact: Remaining Length = 0 (no reason code, no properties)
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, 0x00);
        }
        else
        {
            var contentSize = 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;
            bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
            bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)reasonCode);
            bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);

            if (!string.IsNullOrEmpty(packet.ReasonString))
                bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, packet.ReasonString);
            if (!string.IsNullOrEmpty(packet.ServerReference))
                bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ServerReference, packet.ServerReference);
            if (packet.SessionExpiryInterval.HasValue)
                bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.SessionExpiryInterval, packet.SessionExpiryInterval.Value);
            if (packet.UserProperties != null && packet.UserProperties.Count > 0)
                bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        }

        return bytesWritten;
    }

    // ── AUTH ────────────────────────────────────────────────────────────────

    public static int EncodeAuthPacket(AuthPacket packet, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var bytesWritten = 0;

        var propsSize = 0;
        // Authentication Method is always present on AuthPacket
        propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (!packet.AuthenticationData.IsEmpty)
            propsSize += 1 + 2 + packet.AuthenticationData.Length;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            propsSize += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            propsSize += ComputeUserPropertiesSize(packet.UserProperties);

        var contentSize = 1 + Mqtt5PropertyWriter.GetVariableByteIntSize((uint)propsSize) + propsSize;

        bytesWritten += Mqtt311Encoder.WriteByte(ref span, Mqtt311Encoder.CalculateFirstByteOfFixedPacketHeader(packet));
        bytesWritten += Mqtt311Encoder.EncodeFrameHeaderWithByteShifting(ref span, contentSize);
        bytesWritten += Mqtt311Encoder.WriteByte(ref span, (byte)packet.ReasonCode);
        bytesWritten += Mqtt5PropertyWriter.EncodeVariableByteInt(ref span, (uint)propsSize);
        bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.AuthenticationMethod, packet.AuthenticationMethod);

        if (!packet.AuthenticationData.IsEmpty)
            bytesWritten += Mqtt5PropertyWriter.WriteBinaryData(ref span, Mqtt5PropertyIdentifiers.AuthenticationData, packet.AuthenticationData.Span);
        if (!string.IsNullOrEmpty(packet.ReasonString))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);

        return bytesWritten;
    }

    // ── Private: property size helpers ──────────────────────────────────────

    private static int ComputeConnectPropertiesSize(ConnectPacket packet)
    {
        // SEI(1+4) + MaxPktSz(1+4) + TopAlias(1+2) + RRI(1+1) + RPI(1+1) = 17
        // ReceiveMaximum(1+2) = 3 is written only when non-zero: MQTT 5.0 §3.1.2.11.3
        // states it is a Protocol Error to include ReceiveMaximum with value 0.
        var size = 5 + 5 + 3 + 2 + 2 + (packet.ReceiveMaximum > 0 ? 3 : 0);

        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            size += ComputeUserPropertiesSize(packet.UserProperties);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            size += 1 + 2 + packet.AuthenticationData.Value.Length;

        return size;
    }

    private static int ComputeWillPropertiesSize(MqttLastWill will)
    {
        var size = 0;
        if (will.DelayInterval != 0) size += 1 + 4;
        if (will.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified) size += 1 + 1;
        if (will.MessageExpiryInterval != 0) size += 1 + 4;
        if (!string.IsNullOrEmpty(will.ContentType)) size += 1 + 2 + Encoding.UTF8.GetByteCount(will.ContentType);
        if (!string.IsNullOrEmpty(will.ResponseTopic)) size += 1 + 2 + Encoding.UTF8.GetByteCount(will.ResponseTopic);
        if (will.WillCorrelationData.HasValue && !will.WillCorrelationData.Value.IsEmpty)
            size += 1 + 2 + will.WillCorrelationData.Value.Length;
        if (will.WillProperties != null && will.WillProperties.Count > 0)
            size += ComputeUserPropertiesSize(will.WillProperties);
        return size;
    }

    private static int ComputeConnectContentSize(ConnectPacket packet, int connectPropsSize, int willPropsSize)
    {
        var size = 0;
        // Variable header
        size += 2 + 4; // "MQTT" (2-byte length prefix + 4 bytes)
        size += 1;     // Protocol Level
        size += 1;     // Connect Flags
        size += 2;     // Keep Alive
        // Connect Properties section
        size += Mqtt5PropertyWriter.GetVariableByteIntSize((uint)connectPropsSize) + connectPropsSize;
        // Payload: Client ID
        size += 2 + Encoding.UTF8.GetByteCount(packet.ClientId);
        // Payload: Will
        if (packet.Flags.WillFlag && packet.Will != null)
        {
            size += Mqtt5PropertyWriter.GetVariableByteIntSize((uint)willPropsSize) + willPropsSize;
            size += 2 + Encoding.UTF8.GetByteCount(packet.Will.Topic);
            size += 2 + packet.Will.Message.Length;
        }
        // Payload: Username
        if (packet.Flags.UsernameFlag && packet.UserName != null)
            size += 2 + Encoding.UTF8.GetByteCount(packet.UserName);
        // Payload: Password
        if (packet.Flags.PasswordFlag && packet.Password != null)
            size += 2 + Encoding.UTF8.GetByteCount(packet.Password);
        return size;
    }

    private static int ComputeConnAckPropertiesSize(ConnAckPacket packet)
    {
        var size = 0;
        if (packet.SessionExpiryInterval.HasValue) size += 1 + 4;
        if (packet.ReceiveMaximum.HasValue) size += 1 + 2;
        if (packet.MaximumQoS.HasValue) size += 1 + 1;
        if (packet.RetainAvailable.HasValue) size += 1 + 1;
        if (packet.MaximumPacketSize.HasValue) size += 1 + 4;
        if (!string.IsNullOrEmpty(packet.AssignedClientIdentifier))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AssignedClientIdentifier);
        if (packet.TopicAliasMaximum.HasValue) size += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            size += ComputeUserPropertiesSize(packet.UserProperties);
        if (packet.WildcardSubscriptionAvailable.HasValue) size += 1 + 1;
        if (packet.SubscriptionIdentifiersAvailable.HasValue) size += 1 + 1;
        if (packet.SharedSubscriptionAvailable.HasValue) size += 1 + 1;
        if (packet.ServerKeepAlive.HasValue) size += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ResponseInformation))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ResponseInformation);
        if (!string.IsNullOrEmpty(packet.ServerReference))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ServerReference);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            size += 1 + 2 + packet.AuthenticationData.Value.Length;
        return size;
    }

    private static int ComputePublishPropertiesSize(PublishPacket packet)
    {
        var size = 0;
        if (packet.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified) size += 1 + 1;
        if (packet.MessageExpiryInterval != 0) size += 1 + 4;
        if (packet.TopicAlias != 0) size += 1 + 2;
        if (!string.IsNullOrEmpty(packet.ResponseTopic))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ResponseTopic);
        if (packet.CorrelationData.HasValue && !packet.CorrelationData.Value.IsEmpty)
            size += 1 + 2 + packet.CorrelationData.Value.Length;
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            size += ComputeUserPropertiesSize(packet.UserProperties);
        if (packet.SubscriptionIdentifiers != null && packet.SubscriptionIdentifiers.Count > 0)
        {
            foreach (var sid in packet.SubscriptionIdentifiers)
                size += 1 + Mqtt5PropertyWriter.GetVariableByteIntSize(sid);
        }
        if (!string.IsNullOrEmpty(packet.ContentType))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ContentType);
        return size;
    }

    private static int ComputeAckPropertiesSize(string? reasonString, IReadOnlyList<KeyValuePair<string, string>>? userProps)
    {
        var size = 0;
        if (!string.IsNullOrEmpty(reasonString))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(reasonString);
        if (userProps != null && userProps.Count > 0)
            size += ComputeUserPropertiesSize(userProps);
        return size;
    }

    private static int ComputeSubscribePropertiesSize(SubscribePacket packet)
    {
        var size = 0;
        if (packet.SubscriptionIdentifier.HasValue)
            size += 1 + Mqtt5PropertyWriter.GetVariableByteIntSize(packet.SubscriptionIdentifier.Value);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            size += ComputeUserPropertiesSize(packet.UserProperties);
        return size;
    }

    private static int ComputeSubAckPropertiesSize(SubAckPacket packet)
    {
        var size = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            size += 1 + 2 + Encoding.UTF8.GetByteCount(packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            size += ComputeUserPropertiesSize(packet.UserProperties);
        return size;
    }

    private static int ComputeUserPropertiesSize(IReadOnlyList<KeyValuePair<string, string>> userProperties)
    {
        var size = 0;
        foreach (var (key, value) in userProperties)
            size += 1 + 2 + Encoding.UTF8.GetByteCount(key) + 2 + Encoding.UTF8.GetByteCount(value);
        return size;
    }

    // ── Private: property write helpers ─────────────────────────────────────

    private static int WriteConnectProperties(ref Span<byte> span, ConnectPacket packet)
    {
        var bytesWritten = 0;
        // Always-present properties
        bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.SessionExpiryInterval, packet.SessionExpiryInterval);
        // ReceiveMaximum=0 is a Protocol Error (MQTT 5.0 §3.1.2.11.3); only write when non-zero.
        if (packet.ReceiveMaximum > 0)
            bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.ReceiveMaximum, packet.ReceiveMaximum);
        bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.MaximumPacketSize, packet.MaximumPacketSize);
        bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.TopicAliasMaximum, packet.TopicAliasMaximum);
        bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.RequestResponseInformation,
            (byte)(packet.RequestResponseInformation ? 1 : 0));
        bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.RequestProblemInformation,
            (byte)(packet.RequestProblemInformation ? 1 : 0));
        // Conditional properties
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.AuthenticationMethod, packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            bytesWritten += Mqtt5PropertyWriter.WriteBinaryData(ref span, Mqtt5PropertyIdentifiers.AuthenticationData, packet.AuthenticationData.Value.Span);
        return bytesWritten;
    }

    private static int WriteWillProperties(ref Span<byte> span, MqttLastWill will)
    {
        var bytesWritten = 0;
        if (will.DelayInterval != 0)
            bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.WillDelayInterval, will.DelayInterval);
        if (will.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.PayloadFormatIndicator, (byte)will.PayloadFormatIndicator);
        if (will.MessageExpiryInterval != 0)
            bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.MessageExpiryInterval, will.MessageExpiryInterval);
        if (!string.IsNullOrEmpty(will.ContentType))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ContentType, will.ContentType);
        if (!string.IsNullOrEmpty(will.ResponseTopic))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ResponseTopic, will.ResponseTopic);
        if (will.WillCorrelationData.HasValue && !will.WillCorrelationData.Value.IsEmpty)
            bytesWritten += Mqtt5PropertyWriter.WriteBinaryData(ref span, Mqtt5PropertyIdentifiers.CorrelationData, will.WillCorrelationData.Value.Span);
        if (will.WillProperties != null && will.WillProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, will.WillProperties);
        return bytesWritten;
    }

    private static int WriteConnAckProperties(ref Span<byte> span, ConnAckPacket packet)
    {
        var bytesWritten = 0;
        if (packet.SessionExpiryInterval.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.SessionExpiryInterval, packet.SessionExpiryInterval.Value);
        if (packet.ReceiveMaximum.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.ReceiveMaximum, packet.ReceiveMaximum.Value);
        if (packet.MaximumQoS.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.MaximumQoS, (byte)packet.MaximumQoS.Value);
        if (packet.RetainAvailable.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.RetainAvailable, packet.RetainAvailable.Value ? (byte)1 : (byte)0);
        if (packet.MaximumPacketSize.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.MaximumPacketSize, packet.MaximumPacketSize.Value);
        if (!string.IsNullOrEmpty(packet.AssignedClientIdentifier))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.AssignedClientIdentifier, packet.AssignedClientIdentifier);
        if (packet.TopicAliasMaximum.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.TopicAliasMaximum, packet.TopicAliasMaximum.Value);
        if (!string.IsNullOrEmpty(packet.ReasonString))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        if (packet.WildcardSubscriptionAvailable.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.WildcardSubscriptionAvailable, packet.WildcardSubscriptionAvailable.Value ? (byte)1 : (byte)0);
        if (packet.SubscriptionIdentifiersAvailable.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.SubscriptionIdentifiersAvailable, packet.SubscriptionIdentifiersAvailable.Value ? (byte)1 : (byte)0);
        if (packet.SharedSubscriptionAvailable.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.SharedSubscriptionAvailable, packet.SharedSubscriptionAvailable.Value ? (byte)1 : (byte)0);
        if (packet.ServerKeepAlive.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.ServerKeepAlive, packet.ServerKeepAlive.Value);
        if (!string.IsNullOrEmpty(packet.ResponseInformation))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ResponseInformation, packet.ResponseInformation);
        if (!string.IsNullOrEmpty(packet.ServerReference))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ServerReference, packet.ServerReference);
        if (!string.IsNullOrEmpty(packet.AuthenticationMethod))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.AuthenticationMethod, packet.AuthenticationMethod);
        if (packet.AuthenticationData.HasValue && !packet.AuthenticationData.Value.IsEmpty)
            bytesWritten += Mqtt5PropertyWriter.WriteBinaryData(ref span, Mqtt5PropertyIdentifiers.AuthenticationData, packet.AuthenticationData.Value.Span);
        return bytesWritten;
    }

    private static int WritePublishProperties(ref Span<byte> span, PublishPacket packet)
    {
        var bytesWritten = 0;
        if (packet.PayloadFormatIndicator != PayloadFormatIndicator.Unspecified)
            bytesWritten += Mqtt5PropertyWriter.WriteByte(ref span, Mqtt5PropertyIdentifiers.PayloadFormatIndicator, (byte)packet.PayloadFormatIndicator);
        if (packet.MessageExpiryInterval != 0)
            bytesWritten += Mqtt5PropertyWriter.WriteFourByteInt(ref span, Mqtt5PropertyIdentifiers.MessageExpiryInterval, packet.MessageExpiryInterval);
        if (packet.TopicAlias != 0)
            bytesWritten += Mqtt5PropertyWriter.WriteTwoByteInt(ref span, Mqtt5PropertyIdentifiers.TopicAlias, packet.TopicAlias);
        if (!string.IsNullOrEmpty(packet.ResponseTopic))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ResponseTopic, packet.ResponseTopic);
        if (packet.CorrelationData.HasValue && !packet.CorrelationData.Value.IsEmpty)
            bytesWritten += Mqtt5PropertyWriter.WriteBinaryData(ref span, Mqtt5PropertyIdentifiers.CorrelationData, packet.CorrelationData.Value.Span);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        if (packet.SubscriptionIdentifiers != null && packet.SubscriptionIdentifiers.Count > 0)
        {
            foreach (var sid in packet.SubscriptionIdentifiers)
                bytesWritten += Mqtt5PropertyWriter.WriteVariableByteInt(ref span, Mqtt5PropertyIdentifiers.SubscriptionIdentifier, sid);
        }
        if (!string.IsNullOrEmpty(packet.ContentType))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ContentType, packet.ContentType);
        return bytesWritten;
    }

    private static int WriteAckProperties(ref Span<byte> span, string? reasonString,
        IReadOnlyList<KeyValuePair<string, string>>? userProps)
    {
        var bytesWritten = 0;
        if (!string.IsNullOrEmpty(reasonString))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, reasonString);
        if (userProps != null && userProps.Count > 0)
            bytesWritten += WriteUserProperties(ref span, userProps);
        return bytesWritten;
    }

    private static int WriteSubscribeProperties(ref Span<byte> span, SubscribePacket packet)
    {
        var bytesWritten = 0;
        if (packet.SubscriptionIdentifier.HasValue)
            bytesWritten += Mqtt5PropertyWriter.WriteVariableByteInt(ref span, Mqtt5PropertyIdentifiers.SubscriptionIdentifier, packet.SubscriptionIdentifier.Value);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        return bytesWritten;
    }

    private static int WriteSubAckProperties(ref Span<byte> span, SubAckPacket packet)
    {
        var bytesWritten = 0;
        if (!string.IsNullOrEmpty(packet.ReasonString))
            bytesWritten += Mqtt5PropertyWriter.WriteUtf8String(ref span, Mqtt5PropertyIdentifiers.ReasonString, packet.ReasonString);
        if (packet.UserProperties != null && packet.UserProperties.Count > 0)
            bytesWritten += WriteUserProperties(ref span, packet.UserProperties);
        return bytesWritten;
    }

    private static int WriteUserProperties(ref Span<byte> span, IReadOnlyList<KeyValuePair<string, string>> userProperties)
    {
        var bytesWritten = 0;
        foreach (var (key, value) in userProperties)
            bytesWritten += Mqtt5PropertyWriter.WriteStringPair(ref span, key, value);
        return bytesWritten;
    }
}
