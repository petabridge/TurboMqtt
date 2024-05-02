// -----------------------------------------------------------------------
// <copyright file="PacketDeDuplicationFlow.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Utility;

namespace TurboMqtt.Streams;

/// <summary>
/// Used to de-duplicate recently seen packets.
/// </summary>
internal sealed class PacketDeDuplicationFlow : GraphStage<FlowShape<PublishPacket, PublishPacket>>
{
    private readonly int _bufferSize;
    private readonly TimeSpan _bufferExpiry;

    public PacketDeDuplicationFlow(int bufferSize, TimeSpan bufferExpiry)
    {
        _bufferSize = bufferSize;
        _bufferExpiry = bufferExpiry;
        
        // assert that buffer size is at least 1
        if (bufferSize < 1)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be at least 1");

        // assert that bufferExpiry is non-zero
        if (bufferExpiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(bufferExpiry), "Buffer expiry must be greater than zero");
        
        Shape = new FlowShape<PublishPacket, PublishPacket>(In, Out);
    }

    public Inlet<PublishPacket> In { get; } = new("PacketDeDuplicationFlow.in");
    public Outlet<PublishPacket> Out { get; } = new("PacketDeDuplicationFlow.out");
    
    public override FlowShape<PublishPacket, PublishPacket> Shape { get; }
    
    
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }
    
    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly PacketDeDuplicationFlow _stage;
        private readonly TopicCacheManager<ushort> _publishIds;
        private Deadline _expiryDeadline;
        
        public Logic(PacketDeDuplicationFlow stage) : base(stage.Shape)
        {
            _stage = stage;
            _publishIds = new TopicCacheManager<ushort>(_stage._bufferSize, _stage._bufferExpiry);
            _expiryDeadline = Deadline.FromNow(_stage._bufferExpiry);
            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }

        public override void OnPush()
        {
            var publishPacket = Grab(_stage.In);

            switch (publishPacket.QualityOfService)
            {
                case QualityOfService.AtMostOnce:
                    Push(_stage.Out, publishPacket);
                    break;
                case QualityOfService.AtLeastOnce:
                case QualityOfService.ExactlyOnce:
                {
                    var packetId = publishPacket.PacketId.Value;
                    if (_publishIds.ContainsItem(publishPacket.TopicName, packetId))
                    {
                        // we've seen this packet before
                        Log.Debug("Dropping duplicate packet with id [{0}]", packetId);
                        Pull(_stage.In);
                    }
                    else
                    {
                        // create a record of this packet
                        _publishIds.AddItem(publishPacket.TopicName, packetId);
                        Push(_stage.Out, publishPacket);
                    }
                    break;
                
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            // check for expired items
            if (_expiryDeadline.IsOverdue)
            {
                _publishIds.ClearCache(publishPacket.TopicName);
                _expiryDeadline = Deadline.FromNow(_stage._bufferExpiry);
            }
        }

        public override void OnPull()
        {
            Pull(_stage.In);
        }
    }
}