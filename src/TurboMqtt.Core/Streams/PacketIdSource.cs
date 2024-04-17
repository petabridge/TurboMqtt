// -----------------------------------------------------------------------
// <copyright file="PacketIdSource.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Streams;

// create a custom Source<ZonZeroUInt32, NotUsed> that generates unique packet IDs
// for each message that needs to be sent out over the wire
internal sealed class PacketIdFlow : GraphStage<FlowShape<MqttPacket, MqttPacket>>
{
    private readonly ushort _startingValue;
    
    /// <summary>
    /// Determines if the packet ID is required for the given <see cref="MqttPacket"/>
    /// </summary>
    /// <param name="packet">The type of packet</param>
    public static bool IsPacketIdRequired(MqttPacket packet)
    {
        if(packet is PublishPacket publishPacket && publishPacket.QualityOfService != QualityOfService.AtMostOnce)
            return true;
        switch (packet.PacketType)
        {
            case MqttPacketType.Subscribe:
            case MqttPacketType.Unsubscribe:
                return true;
            default:
                return false;
        }
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PacketIdFlow"/> class.
    /// </summary>
    /// <param name="startingValue">For testing purposes primarily</param>
    public PacketIdFlow(ushort startingValue = 0)
    {
        _startingValue = startingValue;
        Shape = new FlowShape<MqttPacket, MqttPacket>(In, Out);
    }
    
    public Inlet<MqttPacket> In { get; } = new("PacketIdFlow.In");

    public Outlet<MqttPacket> Out { get; } = new("PacketIdFlow.Out");

    public override FlowShape<MqttPacket, MqttPacket> Shape { get; }

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly PacketIdFlow _flow;
        
        public Logic(PacketIdFlow flow, ushort startingValue) : base(flow.Shape)
        {
            _flow = flow;
            _currentId = startingValue;
            SetHandler(flow.Out, this);
        }
        
        private ushort _currentId = 0;

        public override void OnPush()
        {
            var elem = Grab(_flow.In);
            if (IsPacketIdRequired(elem))
            {
                // ensure that we wrap around
                if (_currentId == ushort.MaxValue)
                {
                    _currentId = 0;
                }
                
                var packetWithId = (MqttPacketWithId) elem;

                // guarantees that we never hit zero
                packetWithId.PacketId = ++_currentId;
                Push(_flow.Out, packetWithId);
            }
            else
            {
                Push(_flow.Out, elem);
            }
            
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this, _startingValue);
    }
}