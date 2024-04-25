// -----------------------------------------------------------------------
// <copyright file="TurbotMqttHostingExtensions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;
using TurboMqtt.Client;

namespace TurboMqtt;

/// <summary>
/// Used to tie into the Akka.NET <see cref="ActorSystem"/> and start up the TurboMqtt server.
/// </summary>
public static class TurbotMqttHostingExtensions
{
    /// <summary>
    /// Registers the <see cref="IMqttClientFactory"/> with the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// Needs a <see cref="ActorSystem"/> to run - will create one if none is found in the <see cref="IServiceCollection"/>.
    ///
    /// For best results, use Akka.Hosting to create and manage the <see cref="ActorSystem"/> for you.
    /// https://www.nuget.org/packages/Akka.Hosting
    /// </remarks>
    public static IServiceCollection AddTurboMqttClientFactory(this IServiceCollection services)
    {
        services.AddSingleton<IMqttClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                // start our own local ActorSystem
                system = ActorSystem.Create("turbomqtt");
                system.Log.Info("Created new Akka.NET ActorSystem {0} - none found in IServiceCollection", system.Name);
            }
            
            return new MqttClientFactory(system);

        });

        return services;
    }
}