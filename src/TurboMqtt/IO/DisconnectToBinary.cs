// -----------------------------------------------------------------------
// <copyright file="DisconnectToBinary.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Diagnostics;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO;

/// <summary>
/// Utility class designed to ensure that we always flush a disconnect packet to transports even when there are none.
/// </summary>
internal static class DisconnectToBinary
{
    /// <summary>
    /// Used when the broker disconnects from us normally.
    /// </summary>
    public static readonly DisconnectPacket NormalDisconnectPacket = new()
    {
        ReasonCode = DisconnectReasonCode.NormalDisconnection,
        Duplicate = true
    };
    
    public static (IMemoryOwner<byte> buffer, int estimatedSize) ToBinary(this DisconnectPacket packet,
        MqttProtocolVersion version)
    {
        if (version == MqttProtocolVersion.V5_0)
            throw new NotSupportedException();

        var estimate = MqttPacketSizeEstimator.EstimatePacketSize(packet, version);
        var fullSize = estimate + 2; // add 2 bytes for the fixed header
        Memory<byte> bytes = new byte[fullSize];

        switch (version)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                var actualSize = Mqtt311Encoder.EncodePacket(packet, ref bytes, estimate);
                Debug.Assert(actualSize == fullSize,
                    $"Actual size {actualSize} did not match estimated size {fullSize}");
                break;
            }
        }

        return (new UnsharedMemoryOwner<byte>(bytes), fullSize);
    }
}