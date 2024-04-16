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
    public PacketIdSource()
    {
        Shape = new SourceShape<ushort>(Out);
    }

    public Outlet<ushort> Out { get; } = new("PacketIdSource.Out");

    public override SourceShape<ushort> Shape { get; }

    private sealed class Logic(PacketIdSource source) : OutGraphStageLogic(source.Shape)
    {
        private ushort _currentId = 0;

        public override void OnPull()
        {
            // ensure that we wrap around
            if (_currentId == ushort.MaxValue)
            {
                _currentId = 0;
            }

            // guarantees that we never hit zero
            Push(source.Out, ++_currentId);
        }
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }
}