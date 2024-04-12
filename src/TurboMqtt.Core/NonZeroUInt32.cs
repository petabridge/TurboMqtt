namespace TurboMqtt.Core;

/// <summary>
/// Subscription and packet identifiers must be greater than 0.
/// </summary>
public readonly struct NonZeroUInt32
{
    /// <summary>
    /// The value of the identifier.
    /// </summary>
    public uint Value { get; }
    
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