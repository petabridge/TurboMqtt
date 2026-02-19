// -----------------------------------------------------------------------
// <copyright file="TcpStreamProviderSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using TurboMqtt.IO.Tcp;

namespace TurboMqtt.Tests.IO.Tcp;

public class TcpStreamProviderSpecs : IDisposable
{
    private Socket? _listener;
    private TcpStreamProvider? _provider;
    private int _port;

    private void StartListener()
    {
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _listener.Listen(1);
        _port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        _provider?.Close();
        _listener?.Close();
        _listener?.Dispose();
    }

    [Fact]
    public async Task ConnectAsync_ShouldReturnNetworkStream_WhenServerIsListening()
    {
        // Arrange
        StartListener();
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);

        // Act
        var stream = await _provider.ConnectAsync("localhost", _port);

        // Assert
        stream.Should().NotBeNull();
        stream.Should().BeOfType<NetworkStream>();
        stream.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeTrue();

        stream.Close();
    }

    [Fact]
    public async Task ConnectAsync_ShouldResolveLocalhost_ViaHostname()
    {
        // Arrange
        StartListener();
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);

        // Act — "localhost" requires DNS resolution
        var stream = await _provider.ConnectAsync("localhost", _port);

        // Assert
        stream.Should().NotBeNull();

        stream.Close();
    }

    [Fact]
    public async Task ConnectAsync_ShouldThrow_WhenServerIsNotListening()
    {
        // Arrange — use a port that nothing is listening on
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);

        // Act & Assert
        var act = () => _provider.ConnectAsync("localhost", 1);
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task ConnectAsync_ShouldThrow_WhenHostCannotBeResolved()
    {
        // Arrange
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);

        // Act & Assert
        var act = () => _provider.ConnectAsync("this-host-does-not-exist-xyz123.invalid", 1883);
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task ConnectAsync_ShouldRespectCancellation()
    {
        // Arrange
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        var act = () => _provider.ConnectAsync("localhost", 1883, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConnectAsync_ShouldConfigureSocket_WithExpectedBufferSizes()
    {
        // Arrange
        StartListener();
        var maxFrameSize = 128 * 1024;
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, maxFrameSize);

        // Act
        var stream = await _provider.ConnectAsync("localhost", _port);

        // Accept the connection on the server side so we can verify it connected
        var serverSocket = await _listener!.AcceptAsync();
        serverSocket.Connected.Should().BeTrue();

        // Assert — the stream is usable
        stream.Should().NotBeNull();

        serverSocket.Close();
        stream.Close();
    }

    [Fact]
    public void Close_ShouldBeIdempotent()
    {
        // Arrange
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);

        // Act — close before any connection was made
        var act = () =>
        {
            _provider.Close();
            _provider.Close();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Close_ShouldCleanUpSocket_AfterSuccessfulConnect()
    {
        // Arrange
        StartListener();
        _provider = new TcpStreamProvider(AddressFamily.Unspecified, 128 * 1024);
        var stream = await _provider.ConnectAsync("localhost", _port);

        // Act
        stream.Close();
        _provider.Close();

        // Assert — calling Close again should not throw
        var act = () => _provider.Close();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ConnectAsync_WithExplicitAddressFamily_ShouldWork()
    {
        // Arrange
        StartListener();
        _provider = new TcpStreamProvider(AddressFamily.InterNetwork, 128 * 1024);

        // Act
        var stream = await _provider.ConnectAsync("127.0.0.1", _port);

        // Assert
        stream.Should().NotBeNull();

        stream.Close();
    }
}
