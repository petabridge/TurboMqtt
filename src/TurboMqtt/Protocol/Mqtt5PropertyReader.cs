// -----------------------------------------------------------------------
// <copyright file="Mqtt5PropertyReader.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

/// <summary>
/// Static helper methods for reading MQTT 5.0 property values from a <see cref="ReadOnlySpan{T}"/> buffer.
/// The property identifier byte has already been consumed by the caller; each method reads only the
/// typed value and advances the buffer slice.
/// </summary>
/// <remarks>
/// Methods follow the same <c>ref ReadOnlySpan&lt;byte&gt;</c> advancement pattern as <see cref="Mqtt311Decoder"/>.
/// </remarks>
internal static class Mqtt5PropertyReader
{
    // ── Primitive readers ──────────────────────────────────────────────────

    /// <summary>
    /// Reads a single-byte value (Byte property type), advancing the buffer by 1 byte.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer contains fewer than 1 byte.</exception>
    public static byte ReadByte(ref ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 1)
            throw new MqttDecoderException(
                "Buffer too short to read a Byte property value.",
                MqttProtocolVersion.V5_0);

        var value = buffer[0];
        buffer = buffer.Slice(1);
        return value;
    }

    /// <summary>
    /// Reads a 2-byte big-endian unsigned short (Two Byte Integer property type), advancing the buffer by 2 bytes.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer contains fewer than 2 bytes.</exception>
    public static ushort ReadTwoByteInt(ref ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
            throw new MqttDecoderException(
                "Buffer too short to read a Two Byte Integer property value.",
                MqttProtocolVersion.V5_0);

        var value = (ushort)((buffer[0] << 8) | buffer[1]);
        buffer = buffer.Slice(2);
        return value;
    }

    /// <summary>
    /// Reads a 4-byte big-endian unsigned integer (Four Byte Integer property type), advancing the buffer by 4 bytes.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer contains fewer than 4 bytes.</exception>
    public static uint ReadFourByteInt(ref ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4)
            throw new MqttDecoderException(
                "Buffer too short to read a Four Byte Integer property value.",
                MqttProtocolVersion.V5_0);

        var value = ((uint)buffer[0] << 24)
                  | ((uint)buffer[1] << 16)
                  | ((uint)buffer[2] << 8)
                  | buffer[3];
        buffer = buffer.Slice(4);
        return value;
    }

    /// <summary>
    /// Attempts to read a Variable Byte Integer value (Variable Byte Integer property type),
    /// advancing the buffer by 1–4 bytes on success.
    /// </summary>
    /// <returns><c>true</c> on success; <c>false</c> if the buffer is too short or the encoding exceeds 4 bytes.</returns>
    public static bool TryReadVariableByteInt(ref ReadOnlySpan<byte> buffer, out uint value)
    {
        value = 0;
        uint multiplier = 1;
        var index = 0;

        byte encodedByte;
        do
        {
            if (index >= buffer.Length || index >= 4)
                return false;

            encodedByte = buffer[index];
            value += (uint)((encodedByte & 0x7F) * multiplier);
            multiplier *= 128;
            index++;
        } while ((encodedByte & 0x80) != 0);

        buffer = buffer.Slice(index);
        return true;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 encoded string (UTF-8 Encoded String property type),
    /// advancing the buffer by 2 + string byte length.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer is too short for the declared string length.</exception>
    public static string ReadUtf8String(ref ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
            throw new MqttDecoderException(
                "Buffer too short to read UTF-8 string length prefix.",
                MqttProtocolVersion.V5_0);

        var strByteLen = (buffer[0] << 8) | buffer[1];
        buffer = buffer.Slice(2);

        if (buffer.Length < strByteLen)
            throw new MqttDecoderException(
                $"Buffer too short to read UTF-8 string of declared length {strByteLen}.",
                MqttProtocolVersion.V5_0);

        var str = Encoding.UTF8.GetString(buffer.Slice(0, strByteLen));
        buffer = buffer.Slice(strByteLen);
        return str;
    }

    /// <summary>
    /// Reads a UTF-8 String Pair (User Property type, identifier 0x26):
    /// two consecutive length-prefixed UTF-8 strings (key then value),
    /// advancing the buffer past both strings.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer is too short.</exception>
    public static (string Key, string Value) ReadStringPair(ref ReadOnlySpan<byte> buffer)
    {
        var key = ReadUtf8String(ref buffer);
        var value = ReadUtf8String(ref buffer);
        return (key, value);
    }

    /// <summary>
    /// Reads a length-prefixed binary blob (Binary Data property type),
    /// advancing the buffer by 2 + data byte length.
    /// </summary>
    /// <exception cref="MqttDecoderException">Thrown when the buffer is too short for the declared data length.</exception>
    public static ReadOnlyMemory<byte> ReadBinaryData(ref ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
            throw new MqttDecoderException(
                "Buffer too short to read binary data length prefix.",
                MqttProtocolVersion.V5_0);

        var dataLen = (buffer[0] << 8) | buffer[1];
        buffer = buffer.Slice(2);

        if (buffer.Length < dataLen)
            throw new MqttDecoderException(
                $"Buffer too short to read binary data of declared length {dataLen}.",
                MqttProtocolVersion.V5_0);

        var data = buffer.Slice(0, dataLen).ToArray();
        buffer = buffer.Slice(dataLen);
        return new ReadOnlyMemory<byte>(data);
    }

    // ── Unknown identifier handling ────────────────────────────────────────

    /// <summary>
    /// Throws a <see cref="MqttDecoderException"/> for an unknown property identifier.
    /// Per MQTT 5.0 spec §2.2.2.2 it is a Protocol Error to send an unknown Property identifier.
    /// </summary>
    /// <exception cref="MqttDecoderException">Always thrown.</exception>
    public static void ThrowUnknownPropertyIdentifier(byte id) =>
        throw new MqttDecoderException(
            $"Unknown MQTT 5.0 property identifier: 0x{id:X2}. This is a Protocol Error per MQTT 5.0 §2.2.2.2.",
            MqttProtocolVersion.V5_0);
}
