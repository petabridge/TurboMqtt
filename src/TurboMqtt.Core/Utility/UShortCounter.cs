// -----------------------------------------------------------------------
// <copyright file="UShortCounter.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Utility;

public sealed class UShortCounter(ushort start = 0)
{
    private int _current = start;

    public ushort GetNextValue()
    {
        while (true)
        {
            var original = _current;
            var incremented = original + 1;

            if (incremented > ushort.MaxValue) // Check if we exceed the ushort maximum value
                incremented = 1; // Reset to 1 if we exceed ushort.MaxValue

            // Atomically update the 'current' if it is still the 'original' value
            var old = Interlocked.CompareExchange(ref _current, incremented, original);
            if (old == original)
                return (ushort)incremented; // Successfully updated
        }
    }
}