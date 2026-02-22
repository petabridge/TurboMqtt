# TurboMqtt Quality of Service (QoS) Levels

This document explains MQTT Quality of Service (QoS) levels and how TurboMqtt handles them.

## Overview

MQTT defines three Quality of Service levels for message delivery:

| Level | Name | Guarantee | Latency | Best For |
|-------|------|-----------|---------|----------|
| **0** | AtMostOnce | Fire-and-forget | Lowest | Non-critical telemetry, high-frequency data |
| **1** | AtLeastOnce | At least once delivery | Medium | Critical messages, transactional data |
| **2** | ExactlyOnce | Exactly once delivery | Highest | Financial transactions, commands |

## QoS 0: At Most Once (Fire-and-Forget)

### Behavior
With QoS 0:
- The publisher sends the message to the broker once
- The broker forwards it to subscribers immediately
- No acknowledgment is required
- The message might be lost if the publisher or broker crashes

### TurboMqtt Implementation
When you publish with QoS 0:
```csharp
var msg = new MqttMessage("sensors/temperature", payload)
{
    QoS = QualityOfService.AtMostOnce
};

var result = await client.PublishAsync(msg);
// result.IsSuccess == true after message is queued
```

- `PublishAsync()` returns as soon as the message is queued for delivery
- It does NOT wait for the broker to confirm receipt
- If the client disconnects before transmission, the message is lost
- No automatic retry happens

### When to Use QoS 0
- Streaming sensor data where losing an occasional reading is acceptable
- High-frequency updates where latency is critical
- Non-critical status updates
- Any scenario where "best effort" delivery is sufficient

## QoS 1: At Least Once Delivery

### Behavior
With QoS 1:
1. Publisher sends PUBLISH packet with a unique packet ID
2. Broker receives PUBLISH and sends back PUBACK (acknowledgment)
3. Publisher receives PUBACK and considers delivery complete
4. Broker forwards message to all subscribers

If the publisher doesn't receive PUBACK within a timeout period, it automatically retries.

### TurboMqtt Implementation
When you publish with QoS 1:
```csharp
var msg = new MqttMessage("orders/new", orderPayload)
{
    QoS = QualityOfService.AtLeastOnce
};

var result = await client.PublishAsync(msg);
// PublishAsync() blocks until PUBACK is received
if (result.IsSuccess)
{
    // Broker has confirmed receipt
    Console.WriteLine("Order accepted by broker");
}
else
{
    // Failed to get acknowledgment
    Console.WriteLine($"Order rejected: {result.Reason}");
}
```

**Key points:**
- `PublishAsync()` blocks until the broker sends PUBACK
- TurboMqtt automatically retries if PUBACK is not received within `PublishRetryInterval` (default: 5 seconds)
- Max retry attempts controlled by `MaxPublishRetries` (default: 3)
- The same packet ID and message may be delivered multiple times if retries are needed

### Automatic Retry on Reconnection
If you publish a message at QoS 1, then lose the connection before receiving PUBACK:
- TurboMqtt stores the message
- After reconnection, it automatically retries sending the message
- The broker ensures the message is delivered to subscribers

### When to Use QoS 1
- Order processing and transactions
- Configuration updates
- Critical alerts and notifications
- Any scenario where "at least once" is acceptable

**Warning:** Be prepared for duplicate handling. If a publisher crashes after sending but before receiving PUBACK, the message may be sent twice.

## QoS 2: Exactly Once Delivery

### Behavior
QoS 2 uses a 4-step handshake to guarantee exactly-once delivery:

1. **PUBLISH** — Publisher sends message with unique packet ID
2. **PUBREC** — Broker receives and acknowledges with PUBREC
3. **PUBREL** — Publisher sends PUBREL to confirm release for delivery
4. **PUBCOMP** — Broker confirms with PUBCOMP

This handshake ensures:
- The message is received exactly once by the broker
- Duplicates are detected and discarded
- Message is delivered to subscribers exactly once

### TurboMqtt Implementation
When you publish with QoS 2:
```csharp
var msg = new MqttMessage("bank/transfer", transferData)
{
    QoS = QualityOfService.ExactlyOnce
};

var result = await client.PublishAsync(msg);
// PublishAsync() blocks until the full 4-step handshake completes
if (result.IsSuccess)
{
    // Message has been delivered exactly once by the broker
    Console.WriteLine("Transfer confirmed");
}
else
{
    Console.WriteLine($"Transfer failed: {result.Reason}");
}
```

**Key points:**
- `PublishAsync()` blocks until PUBCOMP is received
- Much slower than QoS 0 or QoS 1 due to the 4-step handshake
- Automatic retry on timeout or disconnection (like QoS 1)
- Duplicates are automatically filtered by `ClientAcksActor`

### Receiving with QoS 2
When a subscriber receives a QoS 2 message:
```csharp
var subscribeResult = await client.SubscribeAsync("bank/transfer", QualityOfService.ExactlyOnce);

await foreach (var msg in client.ReceivedMessages.ReadAllAsync())
{
    // msg is guaranteed to be received exactly once
    // TurboMqtt has already handled PUBREC/PUBREL/PUBCOMP internally
    Console.WriteLine($"Received: {msg.Topic}");
}
```

- TurboMqtt automatically sends PUBREC when the message arrives
- TurboMqtt automatically sends PUBREL to release the message
- TurboMqtt automatically deduplicates incoming messages
- You don't need to manually handle acknowledgments

### When to Use QoS 2
- Financial transactions and payments
- Medical records and critical health data
- Legal documents and audit logs
- Any scenario where duplicates would cause problems

## Backpressure and Flow Control

### `ReceiveMaximum`
TurboMqtt respects MQTT's backpressure mechanism. The `ReceiveMaximum` setting limits how many messages can be in-flight:

```csharp
var connectOptions = new MqttClientConnectOptions("my-app", MqttProtocolVersion.V3_1_1)
{
    ReceiveMaximum = 10  // Max 10 messages in-flight at a time
};
```

When a message is received:
- TurboMqtt sends PUBACK (QoS 1) or PUBREC (QoS 2)
- Counts the message as "in-flight"
- When the application reads the message and processes it
- The in-flight count decreases, allowing the broker to send more

### Channel Backpressure
Messages flow from the broker through a `System.Threading.Channel<MqttMessage>`:

```csharp
while (await client.ReceivedMessages.WaitToReadAsync())
{
    while (client.ReceivedMessages.TryRead(out var msg))
    {
        // If processing is slow here, the channel fills up
        // and the broker is paused from sending more messages
        await ProcessMessageAsync(msg);
    }
}
```

If your application is slow to process messages:
- The channel buffer fills up
- The broker is paused (no more PUBACK/PUBREC sent)
- The broker stops sending new messages
- Once you catch up, the broker resumes

This prevents your application from being overwhelmed with messages.

### Configuring Backpressure
```csharp
var tcpOptions = new MqttClientTcpOptions("mqtt.example.com", 1883)
{
    // Control socket buffer sizes
    ReceiveBufferSize = 65536,
    SendBufferSize = 65536
};
```

## Mixed QoS Levels in a Single Application

You can use different QoS levels for different topics in the same application:

```csharp
// Publish telemetry at QoS 0 (fast, best effort)
await client.PublishAsync(
    "sensors/temperature",
    tempData,
    QualityOfService.AtMostOnce,
    retain: false);

// Publish commands at QoS 1 (reliable)
await client.PublishAsync(
    "devices/pump/command",
    commandData,
    QualityOfService.AtLeastOnce,
    retain: false);

// Subscribe to critical alerts at QoS 2 (exactly once)
await client.SubscribeAsync("alerts/critical", QualityOfService.ExactlyOnce);

// Subscribe to status updates at QoS 1
await client.SubscribeAsync("status/update", QualityOfService.AtLeastOnce);
```

## Performance Implications

TurboMqtt is optimized for all QoS levels. However, here are typical throughput characteristics:

| QoS | Throughput | Latency | Packets |
|-----|-----------|---------|---------|
| 0 | ~300k msg/s | ~3 μs | 2× (PUBLISH + socket) |
| 1 | ~260k msg/s | ~4 μs | 4× (PUBLISH + PUBACK + broker forward + ACK) |
| 2 | ~110k msg/s | ~9 μs | 16× (4-step handshake × 2 for publish + receive) |

**Note:** These are in-process benchmarks. Real brokers add network latency.

Choose QoS levels based on your reliability requirements, not just performance. TurboMqtt handles all levels efficiently.

## Handling Publish Failures

When `PublishAsync()` returns `IsSuccess = false`:

```csharp
var result = await client.PublishAsync(msg);

if (!result.IsSuccess)
{
    // Possible reasons:
    // - Timeout waiting for ACK
    // - Connection lost and reconnection failed
    // - Invalid topic
    // - Client is not connected

    switch (result.Reason)
    {
        case "Timeout":
            // Retry later
            break;
        case "NotConnected":
            // Reconnect first
            await client.ConnectAsync();
            break;
        default:
            // Log and handle
            logger.LogError("Publish failed: {0}", result.Reason);
            break;
    }
}
```

TurboMqtt does NOT automatically retry publishes at the application level. You should implement your own retry logic or ensure messages are persisted if needed.

## Summary

| Feature | QoS 0 | QoS 1 | QoS 2 |
|---------|-------|-------|-------|
| Fire-and-forget | ✅ Yes | ❌ | ❌ |
| Blocks until ACK | ❌ | ✅ Yes | ✅ Yes |
| Auto-retry on timeout | ❌ | ✅ Yes | ✅ Yes |
| Duplicate possible | ✅ Yes | ✅ Yes | ❌ No |
| Deduplication | ❌ | ❌ | ✅ Yes |
| Latency | Lowest | Medium | Highest |
| Throughput | Highest | Medium | Lowest |
