// -----------------------------------------------------------------------
// <copyright file="MqttDecoderSource.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Streams;

internal sealed class MqttDecoderSource : GraphStage<SourceShape<ImmutableList<MqttPacket>>>
{
    private readonly PipeReader _reader;
    private readonly MqttProtocolVersion _protocolVersion;

    public MqttDecoderSource(PipeReader reader, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1)
    {
        _reader = reader;
        _protocolVersion = protocolVersion;
        Out = new Outlet<ImmutableList<MqttPacket>>($"MqttDecoderSink{_protocolVersion}.Out");
        Shape = new SourceShape<ImmutableList<MqttPacket>>(Out);
    }

    protected override Attributes InitialAttributes => DefaultAttributes.Select;

    public Outlet<ImmutableList<MqttPacket>> Out { get; }

    public override SourceShape<ImmutableList<MqttPacket>> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private class Logic : OutGraphStageLogic
    {
        private readonly PipeReader _pipeReader;
        private readonly MqttProtocolVersion _protocolVersion;
        private readonly MqttDecoderSource _graphStage;
        private readonly Mqtt311Decoder _mqtt311Decoder;
        private readonly Action<ReadResult> _onReadReady;
        
        public Logic(MqttDecoderSource graphStage) : base(graphStage.Shape)
        {
            _graphStage = graphStage;
            _pipeReader = graphStage._reader;
            _protocolVersion = graphStage._protocolVersion;
            _mqtt311Decoder = new Mqtt311Decoder();
            _onReadReady = GetAsyncCallback<ReadResult>(HandleReadResult);
            SetHandler(graphStage.Out, this);
        }

        public override void OnPull()
        {
            if (_pipeReader.TryRead(out var readResult))
            {
                HandleReadResult(readResult);
            }
            else
            {
                var continuation = _pipeReader.ReadAsync();
                if (continuation.IsCompletedSuccessfully)
                {
                    HandleReadResult(continuation.GetAwaiter().GetResult());
                }
                else
                {
                    async Task WaitForRead()
                    {
                        var r = await continuation;
                        _onReadReady(r);
                    }

                    _ = WaitForRead();
                }
            }
        }

        private void HandleReadResult(ReadResult readResult)
        {
            if (!readResult.Buffer.IsEmpty)
            {
                PushReadBytes(readResult.Buffer);
            }

            if (readResult.IsCompleted)
            {
                // we are done reading
                CompleteStage();
            }
        }

        private void PushReadBytes(in ReadOnlySequence<byte> buffer)
        {
            // consume this entire sequence by copying it into a new buffer
            // have to copy because there's no guarantee we can safely release a shared buffer
            // once we hand the message over to the end-user.
            var newMemory = new Memory<byte>(new byte[buffer.Length]);
            buffer.CopyTo(newMemory.Span);
            _pipeReader.AdvanceTo(buffer.Start, buffer.End);

            if (_mqtt311Decoder.TryDecode(newMemory, out var decoded))
            {
                Log.Debug("Decoded [{0}] packets totaling [{1}] bytes", decoded.Count, newMemory.Length);
                Push(_graphStage.Out, decoded);
            }
        }
    }
}