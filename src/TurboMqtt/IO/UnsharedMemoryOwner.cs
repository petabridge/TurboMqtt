// -----------------------------------------------------------------------
// <copyright file="UnsharedMemoryOwner.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;

namespace TurboMqtt.IO;

/// <summary>
/// INTERNAL API
/// </summary>
/// <remarks>
/// Used on the read-side of the Akka.Streams graph to ensure that we don't accidentally share memory buffers
/// during async operations.
/// </remarks>
/// <typeparam name="T">The type of content being shared - usually bytes.s</typeparam>
internal sealed class UnsharedMemoryOwner<T> : IMemoryOwner<T>
{
    public UnsharedMemoryOwner(Memory<T> memory)
    {
        Memory = memory;
    }

    public void Dispose()
    {
        // no-op
    }

    public Memory<T> Memory { get; }
}