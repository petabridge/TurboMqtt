using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TurboMqtt.Core.Tests")]
[assembly: InternalsVisibleTo("TurboMqtt.Core.Benchmarks")]
[assembly: InternalsVisibleTo("TurboMqtt.AotCompatibility.TestApp")] // needs to use the InMemoryTransport