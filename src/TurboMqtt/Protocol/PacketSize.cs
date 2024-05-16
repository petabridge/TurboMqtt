// -----------------------------------------------------------------------
// <copyright file="PacketSize.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Protocol;

/// <summary>
/// Data structure representing the size of a packet, including the content size and the size of the variable length header.
/// </summary>
public readonly struct PacketSize
{
    public static readonly PacketSize NoContent = new(0);
    
    public PacketSize(int contentSize)
    {
        if (contentSize < 0)
            throw new ArgumentOutOfRangeException(nameof(contentSize),
                "Content size must be greater than or equal to 0.");
        ContentSize = contentSize;
    }

    /// <summary>
    /// The value computed by our estimator
    /// </summary>
    public int ContentSize { get; }

    public int VariableLengthHeaderSize => MqttPacketSizeEstimator.GetPacketLengthHeaderSize(ContentSize);

    /// <summary>
    /// We add 1 byte for the fixed header, which is not included in the length
    /// </summary>
    public int TotalSize => ContentSize + VariableLengthHeaderSize + 1;
}