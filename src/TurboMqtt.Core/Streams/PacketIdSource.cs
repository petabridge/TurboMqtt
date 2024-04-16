// -----------------------------------------------------------------------
// <copyright file="PacketIdSource.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboMqtt.Core.Streams;

// create a custom Source<ZonZeroUInt32, NotUsed> that generates unique packet IDs
// for each message that needs to be sent out over the wire
internal sealed class PacketIdSource : GraphStage<SourceShape<ushort>>
{
    private readonly ushort _startingValue;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PacketIdSource"/> class.
    /// </summary>
    /// <param name="startingValue">For testing purposes primarily</param>
    public PacketIdSource(ushort startingValue = 0)
    {
        _startingValue = startingValue;
        Shape = new SourceShape<ushort>(Out);
    }

    public Outlet<ushort> Out { get; } = new("PacketIdSource.Out");

    public override SourceShape<ushort> Shape { get; }

    private sealed class Logic : OutGraphStageLogic
    {
        private readonly PacketIdSource _source;
        
        public Logic(PacketIdSource source, ushort startingValue) : base(source.Shape)
        {
            _source = source;
            _currentId = startingValue;
            SetHandler(source.Out, this);
        }
        
        private ushort _currentId = 0;

        public override void OnPull()
        {
            // ensure that we wrap around
            if (_currentId == ushort.MaxValue)
            {
                _currentId = 0;
            }

            // guarantees that we never hit zero
            Push(_source.Out, ++_currentId);
        }
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this, _startingValue);
    }
}