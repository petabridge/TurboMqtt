﻿// -----------------------------------------------------------------------
// <copyright file="PacketSizeFilter.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Streams;

/// <summary>
/// Drops all packets greater than the maximum allowable size
/// </summary>
internal sealed class PacketSizeFilter : GraphStage<FlowShape<(MqttPacket, PacketSize), (MqttPacket, PacketSize)>>
{
    private readonly int _maxPacketSize;
    public Inlet<(MqttPacket, PacketSize)> In { get; } = new("PacketSizeFilter.In");
    public Outlet<(MqttPacket, PacketSize)> Out { get; } = new("PacketSizeFilter.Out");

    public PacketSizeFilter(int maxPacketSize)
    {
        _maxPacketSize = maxPacketSize;
        Shape = new FlowShape<(MqttPacket, PacketSize), (MqttPacket, PacketSize)>(In, Out);
    }

    public override FlowShape<(MqttPacket, PacketSize), (MqttPacket, PacketSize)> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private class Logic : InAndOutGraphStageLogic
    {
        private readonly PacketSizeFilter _stage;

        public Logic(PacketSizeFilter stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }

        public override void OnPush()
        {
            var (packet, size) = Grab(_stage.In);
            
            // have to adjust the packet size to account for the length header
            if (size.TotalSize > _stage._maxPacketSize)
            {
                Log.Warning("Dropping MQTT packet [{0}] for exceeding max size: {1} bytes.", packet, _stage._maxPacketSize);
                Pull(_stage.In); // Request next element
            }
            else
            {
                Push(_stage.Out, (packet, size));
            }
        }

        public override void OnPull() => Pull(_stage.In);
    }
}