// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Diagnostics.Metrics;
using Akka;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Telemetry;

namespace TurboMqtt.Core.Streams;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class OpenTelemetryFlows
{
    public static IGraph<FlowShape<MqttPacket, MqttPacket>, NotUsed> MqttPacketRateTelemetryFlow(
        MqttProtocolVersion version, string clientId, OpenTelemetryConfig.Direction direction)
    {
        var g = new MqttPacketRateTelemetryFlow(version, clientId, direction);
        return g;
    }
    
    public static IGraph<FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)>, NotUsed> MqttBitRateTelemetryFlow(
        string clientId, MqttProtocolVersion version, OpenTelemetryConfig.Direction direction)
    {
        var g = new MqttBitRateTelemetryFlow(clientId, version, direction);
        return g;
    }
}

/// <summary>
/// Used to measure the inbound and outbound bit rate of MQTT packets.
/// </summary>
internal sealed class MqttBitRateTelemetryFlow : GraphStage<FlowShape<(
    IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)>>{
    
    private readonly string _clientId;
    private readonly MqttProtocolVersion _version;
    private readonly OpenTelemetryConfig.Direction _direction;

    public MqttBitRateTelemetryFlow(string clientId, MqttProtocolVersion version, OpenTelemetryConfig.Direction direction)
    {
        _clientId = clientId;
        _version = version;
        _direction = direction;
        Shape = new FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)>(In, Out);
    }
    
    public Inlet<(IMemoryOwner<byte> buffer, int readableBytes)> In { get; } = new("MqttBitRateTelemetryFlow.in");
    public Outlet<(IMemoryOwner<byte> buffer, int readableBytes)> Out { get; } = new("MqttBitRateTelemetryFlow.out");

    public override FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)> Shape
    {
        get;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }
    
    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly MqttBitRateTelemetryFlow _flow;
        private readonly Counter<long> _bitRateCounter;
        
        public Logic(MqttBitRateTelemetryFlow flow) : base(flow.Shape)
        {
            _flow = flow;
            _bitRateCounter = OpenTelemetryConfig.CreateBitRateCounter(flow._clientId, flow._version, flow._direction);
        }

        public override void OnPush()
        {
            var msg = Grab(_flow.In);

            // record the number of bytes
            _bitRateCounter.Add(msg.readableBytes);

            Push(_flow.Out, msg);
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
    
}

/// <summary>
/// Uses OTEL metrics to measure the flow of inbound MQTT packets.
/// </summary>
internal sealed class MqttPacketRateTelemetryFlow : GraphStage<FlowShape<MqttPacket, MqttPacket>>
{
    private readonly string _clientId;
    private readonly MqttProtocolVersion _version;
    private readonly OpenTelemetryConfig.Direction _direction;


    public MqttPacketRateTelemetryFlow(MqttProtocolVersion version, string clientId,
        OpenTelemetryConfig.Direction direction)
    {
        _version = version;
        _clientId = clientId;
        _direction = direction;
        Shape = new FlowShape<MqttPacket, MqttPacket>(In, Out);
    }

    public override FlowShape<MqttPacket, MqttPacket> Shape { get; }
    public Inlet<MqttPacket> In { get; } = new("MqttPacketRateTelemetryFlow.in");
    public Outlet<MqttPacket> Out { get; } = new("MqttPacketRateTelemetryFlow.out");

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private class Logic : InAndOutGraphStageLogic
    {
        private readonly MqttPacketRateTelemetryFlow _flow;
        private readonly Counter<long> _msgCounter;

        public Logic(MqttPacketRateTelemetryFlow flow) : base(flow.Shape)
        {
            _flow = flow;

            _msgCounter = OpenTelemetryConfig.CreateMessagesCounter(flow._clientId, flow._version, flow._direction);
        }

        public override void OnPush()
        {
            var msg = Grab(_flow.In);

            // record the packet type
            _msgCounter.Add(1,
                new KeyValuePair<string, object?>(OpenTelemetryConfig.PacketTypeTag, msg.PacketType.ToString()));

            Push(_flow.Out, msg);
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}