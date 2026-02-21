// -----------------------------------------------------------------------
// <copyright file="SharedReceiveMaximumQuota.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Protocol.Pub;

/// <summary>
/// Thread-safe shared ReceiveMaximum quota enforced across both QoS 1 and QoS 2 publish actors.
/// Implements MQTT 5.0 §4.9: the total number of in-flight QoS 1 + QoS 2 publishes must not
/// exceed the broker-advertised ReceiveMaximum.
/// </summary>
internal sealed class SharedReceiveMaximumQuota
{
    private int _inFlight;
    private int _maximum; // 0 = unlimited

    /// <summary>
    /// Sets the maximum number of simultaneous in-flight QoS 1 + QoS 2 publishes.
    /// A value of 0 disables the quota (unlimited).
    /// </summary>
    public void SetMaximum(ushort maximum)
    {
        Interlocked.Exchange(ref _maximum, maximum);
    }

    /// <summary>
    /// Tries to claim one in-flight slot. Returns <c>true</c> and increments the counter
    /// when the quota allows it; returns <c>false</c> when the limit is reached.
    /// When the quota is unlimited (maximum = 0) this always returns <c>true</c> without
    /// incrementing the counter.
    /// </summary>
    public bool TryClaim()
    {
        var max = Volatile.Read(ref _maximum);
        if (max == 0) return true; // unlimited — no slot tracking needed

        int current;
        do
        {
            current = Volatile.Read(ref _inFlight);
            if (current >= max) return false;
        } while (Interlocked.CompareExchange(ref _inFlight, current + 1, current) != current);

        return true;
    }

    /// <summary>
    /// Releases a previously claimed in-flight slot. Must only be called once per successful
    /// <see cref="TryClaim"/> that returned <c>true</c> when the quota is limited.
    /// </summary>
    public void Release()
    {
        // Only decrement when a limit is active (i.e., a slot was actually claimed).
        if (Volatile.Read(ref _maximum) == 0) return;
        Interlocked.Decrement(ref _inFlight);
    }

    /// <summary>
    /// <c>true</c> when a ReceiveMaximum limit is active (maximum &gt; 0).
    /// </summary>
    public bool IsLimited => Volatile.Read(ref _maximum) > 0;
}
