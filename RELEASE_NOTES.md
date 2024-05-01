#### 0.1.0 May 1st 2024 ####

Fully functioning MQTT 3.1.1 support without TLS. Hitting 160k msg/s in our benchmarks on QoS=0 - see [TurboMqtt Performance](https://github.com/petabridge/TurboMqtt/blob/dev/docs/Performance.md) for details.

Fixed some client resiliency problems, protecting against broker disconnect and failure scenarios with clean automatic restarts.

Upgraded to [Akka.NET 1.5.20](https://github.com/akkadotnet/akka.net/releases/tag/1.5.20).
