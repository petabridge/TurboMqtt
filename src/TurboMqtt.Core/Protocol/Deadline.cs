// -----------------------------------------------------------------------
// <copyright file="Deadline.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// A sortable deadline structure used to indicate when requests are due.
/// </summary>
internal readonly struct Deadline : IComparable<Deadline>
{
    public Deadline(DateTimeOffset time)
    {
        Time = time;
    }

    public DateTimeOffset Time { get; }

    public bool IsOverdue => DateTimeOffset.UtcNow >= Time;

    public int CompareTo(Deadline other)
    {
        return Time.CompareTo(other.Time);
    }
    
    public static Deadline Now => new(DateTimeOffset.UtcNow);
    
    public static Deadline FromNow(TimeSpan duration) => new(DateTimeOffset.UtcNow + duration);
}