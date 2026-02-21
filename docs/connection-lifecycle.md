# TurboMqtt Client Connection Lifecycle

This document explains how TurboMqtt manages the connection lifecycle and provides guidance on configuring reconnection behavior for your use case.

## Client States

A TurboMqtt client progresses through the following states:

### Connecting
The client has been created and `ConnectAsync()` has been called, but the connection to the broker is not yet established. During this phase:
- The client is establishing the TCP/TLS connection to the broker
- The MQTT CONNECT packet is being sent and awaited
- Any subscription state is being restored (if applicable)

### Connected
The client is fully connected to the MQTT broker and ready to publish and receive messages. In this state:
- `IsConnected` returns `true`
- `PublishAsync()` calls will be processed
- `SubscribeAsync()` calls will succeed
- Messages will be received on subscribed topics

### Reconnecting
The client has lost its connection to the broker and is automatically attempting to reconnect. During this phase:
- `IsConnected` returns `false`
- Published messages may be queued (depending on your configuration)
- The client will retry the connection according to `MaxReconnectAttempts` and `ReconnectTimeout` settings
- Once successfully reconnected, subscriptions are automatically restored

### Disconnected / Failed
The client is no longer connected to the broker. This can happen in two ways:

1. **Graceful Disconnection** — You called `DisconnectAsync()` to cleanly close the connection
2. **Failed Connection** — The client exhausted its reconnection attempts and gave up

Once in this state, the only way to reconnect is to call `ConnectAsync()` again.

## Configurable Reconnection Options

When creating a client with `MqttClientConnectOptions`, you can configure how reconnection behaves:

### `MaxReconnectAttempts` (default: 3)
The maximum number of consecutive reconnection attempts the client will make before giving up.

```csharp
var connectOptions = new MqttClientConnectOptions("my-client", MqttProtocolVersion.V3_1_1)
{
    MaxReconnectAttempts = 5  // Try up to 5 times before giving up
};
```

### `ReconnectTimeout` (default: 5 seconds)
The maximum amount of time to wait for a single reconnection attempt to complete. If a reconnection attempt takes longer than this timeout, it is considered failed.

```csharp
var connectOptions = new MqttClientConnectOptions("my-client", MqttProtocolVersion.V3_1_1)
{
    ReconnectTimeout = TimeSpan.FromSeconds(10)  // Wait up to 10 seconds per attempt
};
```

### `ConnectTimeout` (on `MqttClientTcpOptions`, default: 10 seconds)
The maximum amount of time to wait for the initial TCP connection to establish.

```csharp
var tcpOptions = new MqttClientTcpOptions("mqtt.example.com", 1883)
{
    ConnectTimeout = TimeSpan.FromSeconds(15)
};
```

## Automatic vs Manual Reconnection

### Automatic Reconnection
TurboMqtt automatically reconnects in the following scenarios:

1. **Broker-initiated disconnect** — The broker closes the connection unexpectedly
2. **Network failure** — The underlying network connection is lost
3. **Keep-alive timeout** — No packets received within `KeepAliveSeconds * 1.5`

Once automatic reconnection is triggered:
- The client moves to the **Reconnecting** state
- Up to `MaxReconnectAttempts` reconnection attempts are made
- Each attempt has a `ReconnectTimeout` deadline
- If successful, the client returns to **Connected** state and subscriptions are restored

### When Reconnection Gives Up
If all `MaxReconnectAttempts` are exhausted without a successful reconnection, the client moves to **Disconnected / Failed** state. At this point:
- `IsConnected` returns `false`
- The client will not attempt further reconnections
- You must call `ConnectAsync()` again to reconnect

### Manual Reconnection
You can explicitly control connection and disconnection:

```csharp
// Graceful disconnect — sends DISCONNECT packet and closes cleanly
await client.DisconnectAsync(cancellationToken);

// Abort connection immediately without sending packets
await client.AbortConnectionAsync();

// Reconnect after disconnection
var connectResult = await client.ConnectAsync(cancellationToken);
```

## `DisconnectAsync()` vs `DisposeAsync()`

Understanding the difference is important for graceful shutdown:

### `DisconnectAsync()`
Gracefully disconnects from the MQTT broker:
- Sends a DISCONNECT packet to the broker
- Waits for the disconnect to complete
- Does NOT dispose of the client
- Does NOT clean up internal resources

```csharp
await client.DisconnectAsync(cancellationToken);
// Client is disconnected but still usable
var reconnectResult = await client.ConnectAsync(cancellationToken);  // OK to call ConnectAsync again
```

### `DisposeAsync()`
Completely shuts down and cleans up the client:
- Calls `DisconnectAsync()` internally (if not already disconnected)
- Stops all internal actors and streams
- Releases all memory and resources
- Client cannot be used after disposal

```csharp
await client.DisposeAsync();
// Client is now unusable
// var result = await client.ConnectAsync(...);  // ERROR: client is disposed
```

## Example: Handling Connection Failures

Here's a typical pattern for connecting with appropriate error handling:

```csharp
var connectOptions = new MqttClientConnectOptions("my-app", MqttProtocolVersion.V3_1_1)
{
    MaxReconnectAttempts = 5,
    ReconnectTimeout = TimeSpan.FromSeconds(10)
};

var tcpOptions = new MqttClientTcpOptions("mqtt.example.com", 1883)
{
    ConnectTimeout = TimeSpan.FromSeconds(15)
};

var client = await factory.CreateTcpClient(connectOptions, tcpOptions);

try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var result = await client.ConnectAsync(cts.Token);

    if (!result.IsSuccess)
    {
        Console.WriteLine($"Failed to connect: {result.Reason}");
        // Decide whether to retry, give up, or try different broker
        return;
    }

    Console.WriteLine("Connected to broker");

    // Subscribe and publish as needed...

    // Clean shutdown
    await client.DisconnectAsync(CancellationToken.None);
}
finally
{
    // Always dispose of the client when done
    await client.DisposeAsync();
}
```

## How Subscriptions Are Restored After Reconnection

When TurboMqtt automatically reconnects:
1. The client re-establishes the TCP connection to the broker
2. It sends a fresh CONNECT packet
3. All previous subscriptions are automatically re-subscribed
4. Messages received on those subscriptions continue flowing to your `ReceivedMessages` channel

This happens transparently — you don't need to resubscribe manually after a reconnection.

**Note:** This automatic restoration depends on whether the broker maintains session state. Set `CleanSession = false` in `MqttClientConnectOptions` if you want the broker to preserve your subscription list across reconnections.
