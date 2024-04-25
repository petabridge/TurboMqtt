// -----------------------------------------------------------------------
// <copyright file="LoggingHelpers.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace TurboMqtt.Client;

/// <summary>
/// INTERNAL API
/// </summary>
/// <remarks>
/// Aimed at making it easier to create friendly names for our loggers.
/// </remarks>
internal static class LoggingHelpers
{
    public static ILoggingAdapter CreateLogger<T>(this ActorSystem sys, string name)
    {
        var fullName = $"<{typeof(T).Name}> {name}";
        
        return new BusLogging(sys.EventStream, fullName, typeof(T), sys.Settings.LogFormatter);
    }
}