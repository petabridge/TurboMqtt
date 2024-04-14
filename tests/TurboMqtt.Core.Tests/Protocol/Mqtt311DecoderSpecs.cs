// -----------------------------------------------------------------------
// <copyright file="Mqtt311DecoderSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Protocol;

public class Mqtt311DecoderSpecs
{
    [Theory]
    [InlineData(new byte[] { 0x00 }, 0)]  // Just one byte needed for length 0
    [InlineData(new byte[] { 0x01 }, 1)]  // Correct single byte encoding
    [InlineData(new byte[] { 0x7F }, 127)]  // Single byte for 127
    [InlineData(new byte[] { 0x80, 0x01 }, 128)]  // Correct encoding for 128 (continuation bit set)
    [InlineData(new byte[] { 232, 0x07 }, 1000)]
    [InlineData(new byte[] { 0x80, 0x80, 0x01 }, 16384)]
    [InlineData(new byte[] { 0xD0, 0x86, 0x03 }, 50000)]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x01 }, 2097152)]
    [InlineData(new byte[] { 128, 173, 226, 4 }, 10000000)]
    public void ShouldParseValidFrameLengthHeader(byte[] header, int expectedLength)
    {
        var span = new ReadOnlySpan<byte>(header);
        var foundLength = Mqtt311Decoder.TryGetPacketLength(ref span, out var bodyLength);
        Assert.True(foundLength);
        Assert.Equal(expectedLength, bodyLength);
    }
}