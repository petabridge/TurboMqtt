// -----------------------------------------------------------------------
// <copyright file="Mqtt311EncoderSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Protocol;

public class Mqtt311EncoderSpecs
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(1000, new byte[] { 232, 0x07 })]
    [InlineData(16384, new byte[] { 0x80, 0x80, 0x01 })]
    [InlineData(50000, new byte[] { 0xD0, 0x86, 0x03 })]
    [InlineData(2097152, new byte[] { 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(10000000, new byte[] { 128, 173, 226, 4 })]
    public void ShouldEncodeFrameHeader(int length, byte[] expected)
    {
        var buffer = new Span<byte>(new byte[4]);
        var written = Mqtt311Encoder.EncodeFrameHeader(ref buffer, 0, length);
        buffer.Slice(0, written).ToArray().Should().BeEquivalentTo(expected);
        Assert.Equal(expected.Length, written);
    }
}