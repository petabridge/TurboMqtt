// -----------------------------------------------------------------------
// <copyright file="NonZeroUInt16.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt;

/// <summary>
/// Subscription and packet identifiers must be greater than 0.
/// </summary>
public readonly struct NonZeroUInt16
{
    public static readonly NonZeroUInt16 MinValue = new(1);
    
    /// <summary>
    /// The value of the identifier.
    /// </summary>
    public ushort Value { get; }

    public NonZeroUInt16() : this(1)
    {
    }

    public NonZeroUInt16(ushort value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than 0.");
        }

        Value = value;
    }

    public static implicit operator ushort(NonZeroUInt16 value) => value.Value;
    public static implicit operator NonZeroUInt16(ushort value) => new(value);

    public override string ToString()
    {
        return Value.ToString();
    }
}