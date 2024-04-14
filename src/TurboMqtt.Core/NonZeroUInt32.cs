// -----------------------------------------------------------------------
// <copyright file="NonZeroUInt32.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core;

/// <summary>
/// Subscription and packet identifiers must be greater than 0.
/// </summary>
public readonly struct NonZeroUInt32
{
    public static readonly NonZeroUInt32 MinValue = new(1);
    
    /// <summary>
    /// The value of the identifier.
    /// </summary>
    public uint Value { get; }

    public NonZeroUInt32() : this(1)
    {
    }

    public NonZeroUInt32(uint value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than 0.");
        }

        Value = value;
    }

    public static implicit operator uint(NonZeroUInt32 value) => value.Value;
    public static implicit operator NonZeroUInt32(uint value) => new(value);
}