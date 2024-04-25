// -----------------------------------------------------------------------
// <copyright file="UShortCounterSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Utility;

namespace TurboMqtt.Tests.Utility;

public class UShortCounterSpecs
{
    [Fact]
    public void UShortCounter_should_increment_by_one()
    {
        var counter = new UShortCounter();
        var first = counter.GetNextValue();
        var second = counter.GetNextValue();
        var third = counter.GetNextValue();

        first.Should().Be(1);
        second.Should().Be(2);
        third.Should().Be(3);
    }

    [Fact]
    public void UShortCounter_should_reset_to_one_when_exceeding_max_value()
    {
        var counter = new UShortCounter(ushort.MaxValue);
        var first = counter.GetNextValue();
        var second = counter.GetNextValue();
        var third = counter.GetNextValue();

        first.Should().Be(1);
        second.Should().Be(2);
        third.Should().Be(3);
    }
}