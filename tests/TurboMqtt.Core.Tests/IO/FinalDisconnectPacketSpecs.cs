// -----------------------------------------------------------------------
// <copyright file="FinalDisconnectPacketSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.IO;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.IO;

public class FinalDisconnectPacketSpecs
{
    [Fact]
    public void ShouldEncodeDisconnectPacket()
    {
        var (buf, size) = DisconnectToBinary.NormalDisconnectPacket.ToBinary(MqttProtocolVersion.V3_1_1);
        buf.Memory.Length.Should().Be(size);
        size.Should().BeGreaterThan(0);
    }
}