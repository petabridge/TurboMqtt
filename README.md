# TurboMqtt

The fastest MQTT client in .NET, written on top of [Akka.NET](https://getakka.net/).

![TurboMqtt logo](https://github.com/petabridge/TurboMqtt/blob/dev/docs/logo.png)

## Key Features

* MQTT 3.1.1 support;
* Extremely high performance;
* Extremely resource-efficient - pools memory and leverages asynchronous I/O best practices;
* Extremely robust fault tolerance - this is one of [Akka.NET's great strengths](https://petabridge.com/blog/akkadotnet-actors-restart/) and we've leveraged it in TurboMqtt;
* Supports all MQTT quality of service levels, with automatic publishing retries for QoS 1 and 2;
* Full [OpenTelemetry](https://opentelemetry.io/https://opentelemetry.io/) support;
* Automatic retry-reconnect in broker disconnect scenarios;
* Full support for `IAsyncEnumerable` and backpressure on the receiver side;
* Automatically de-duplicates packets on the receiver side; and
* Automatically acks QoS 1 and QoS 2 packets.

Simple interface that works at very high rates of speed with minimal resource utilization.

## License and Installation

TurboMqtt is licensed can be found here [`LICENSE.md`](LICENSE.md) - you are free to use it for non-production use, but for commercial production use you must [buy a license from Sdkbin](https://sdkbin.com/).