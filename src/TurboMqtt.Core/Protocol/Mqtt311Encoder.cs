// -----------------------------------------------------------------------
// <copyright file="Mqtt311Encoder.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Protocol;

public class Mqtt311Encoder
{
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