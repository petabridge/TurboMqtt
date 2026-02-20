// -----------------------------------------------------------------------
// <copyright file="Mqtt5PropertyWriter.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text;

namespace TurboMqtt.Protocol;

/// <summary>
/// Static helper methods for writing MQTT 5.0 properties into a <see cref="Span{T}"/> buffer.
/// Each method writes the property identifier byte followed by the encoded value, advancing
/// the buffer slice by the number of bytes written.
/// </summary>
/// <remarks>
/// All methods follow the same pattern as <see cref="Mqtt311Encoder"/>:
/// use <c>ref Span&lt;byte&gt;</c> to advance the write position in place.
/// </remarks>
internal static class Mqtt5PropertyWriter
{
    // ── Primitive writers ──────────────────────────────────────────────────

    /// <summary>
    /// Writes a 1-byte identifier followed by a 1-byte value (Byte property type).
    /// Total bytes written: 2.
    /// </summary>
    public static int WriteByte(ref Span<byte> buffer, byte id, byte value)
    {
        buffer[0] = id;
        buffer[1] = value;
        buffer = buffer.Slice(2);
        return 2;
    }

    /// <summary>
    /// Writes a 1-byte identifier followed by a 2-byte big-endian unsigned integer (Two Byte Integer property type).
    /// Total bytes written: 3.
    /// </summary>
    public static int WriteTwoByteInt(ref Span<byte> buffer, byte id, ushort value)
    {
        buffer[0] = id;
        buffer[1] = (byte)(value >> 8);
        buffer[2] = (byte)(value & 0xFF);
        buffer = buffer.Slice(3);
        return 3;
    }

    /// <summary>
    /// Writes a 1-byte identifier followed by a 4-byte big-endian unsigned integer (Four Byte Integer property type).
    /// Total bytes written: 5.
    /// </summary>
    public static int WriteFourByteInt(ref Span<byte> buffer, byte id, uint value)
    {
        buffer[0] = id;
        buffer[1] = (byte)(value >> 24);
        buffer[2] = (byte)(value >> 16);
        buffer[3] = (byte)(value >> 8);
        buffer[4] = (byte)(value & 0xFF);
        buffer = buffer.Slice(5);
        return 5;
    }

    /// <summary>
    /// Writes a 1-byte identifier followed by a Variable Byte Integer (Variable Byte Integer property type).
    /// Total bytes written: 2–5 depending on the magnitude of <paramref name="value"/>.
    /// </summary>
    public static int WriteVariableByteInt(ref Span<byte> buffer, byte id, uint value)
    {
        buffer[0] = id;
        buffer = buffer.Slice(1);
        var vbiLen = EncodeVariableByteInt(ref buffer, value);
        return 1 + vbiLen;
    }

    /// <summary>
    /// Writes a 1-byte identifier followed by a length-prefixed UTF-8 encoded string (UTF-8 Encoded String property type).
    /// Total bytes written: 3 + UTF-8 byte length of <paramref name="value"/>.
    /// </summary>
    public static int WriteUtf8String(ref Span<byte> buffer, byte id, string value)
    {
        var strByteLen = Encoding.UTF8.GetByteCount(value);
        buffer[0] = id;
        buffer[1] = (byte)(strByteLen >> 8);
        buffer[2] = (byte)(strByteLen & 0xFF);
        Encoding.UTF8.GetBytes(value, buffer.Slice(3));
        buffer = buffer.Slice(3 + strByteLen);
        return 3 + strByteLen;
    }

    /// <summary>
    /// Writes a User Property (identifier 0x26) as two consecutive length-prefixed UTF-8 encoded strings
    /// (UTF-8 String Pair property type).
    /// Total bytes written: 5 + UTF-8 byte length of <paramref name="key"/> + UTF-8 byte length of <paramref name="value"/>.
    /// </summary>
    public static int WriteStringPair(ref Span<byte> buffer, string key, string value)
    {
        var keyByteLen = Encoding.UTF8.GetByteCount(key);
        var valByteLen = Encoding.UTF8.GetByteCount(value);

        buffer[0] = Mqtt5PropertyIdentifiers.UserProperty;
        buffer[1] = (byte)(keyByteLen >> 8);
        buffer[2] = (byte)(keyByteLen & 0xFF);
        Encoding.UTF8.GetBytes(key, buffer.Slice(3));
        buffer = buffer.Slice(3 + keyByteLen);

        buffer[0] = (byte)(valByteLen >> 8);
        buffer[1] = (byte)(valByteLen & 0xFF);
        Encoding.UTF8.GetBytes(value, buffer.Slice(2));
        buffer = buffer.Slice(2 + valByteLen);

        return 1 + 2 + keyByteLen + 2 + valByteLen;
    }

    /// <summary>
    /// Writes a 1-byte identifier followed by a length-prefixed binary blob (Binary Data property type).
    /// Total bytes written: 3 + <paramref name="data"/>.Length.
    /// </summary>
    public static int WriteBinaryData(ref Span<byte> buffer, byte id, ReadOnlySpan<byte> data)
    {
        buffer[0] = id;
        buffer[1] = (byte)(data.Length >> 8);
        buffer[2] = (byte)(data.Length & 0xFF);
        data.CopyTo(buffer.Slice(3));
        buffer = buffer.Slice(3 + data.Length);
        return 3 + data.Length;
    }

    // ── Size helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of bytes required to encode <paramref name="value"/> as a Variable Byte Integer
    /// (not including the 1-byte property identifier).
    /// </summary>
    public static int GetVariableByteIntSize(uint value) => value switch
    {
        < 128 => 1,
        < 16_384 => 2,
        < 2_097_152 => 3,
        _ => 4
    };

    // ── Internal helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Encodes <paramref name="value"/> as a Variable Byte Integer directly into <paramref name="buffer"/>,
    /// advancing the buffer by the number of bytes written. Returns the number of bytes written.
    /// </summary>
    internal static int EncodeVariableByteInt(ref Span<byte> buffer, uint value)
    {
        var remainingLength = value;
        var index = 0;
        do
        {
            var encodedByte = remainingLength % 128;
            remainingLength /= 128;
            if (remainingLength > 0)
                encodedByte |= 0x80;
            buffer[index] = (byte)encodedByte;
            index++;
        } while (remainingLength > 0);

        buffer = buffer.Slice(index);
        return index;
    }
}
