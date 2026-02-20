// -----------------------------------------------------------------------
// <copyright file="Mqtt5PropertyRoundtripTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using FsCheck.Xunit;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Protocol;

/// <summary>
/// Unit and property-based tests for <see cref="Mqtt5PropertyWriter"/> and
/// <see cref="Mqtt5PropertyReader"/>. Implements Task 3.0 "Done when" criteria:
/// each property type roundtrips, VBI boundary values, UTF-8 edge cases, and
/// unknown-identifier error handling.
/// </summary>
public class Mqtt5PropertyRoundtripTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Write a byte property and read it back, returning the read value.</summary>
    private static byte ByteRoundtrip(byte id, byte value)
    {
        var buf = new byte[16];
        var writeSpan = buf.AsSpan();
        Mqtt5PropertyWriter.WriteByte(ref writeSpan, id, value);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, 1); // skip identifier byte
        return Mqtt5PropertyReader.ReadByte(ref readSpan);
    }

    private static ushort TwoByteIntRoundtrip(ushort value)
    {
        var buf = new byte[16];
        var writeSpan = buf.AsSpan();
        Mqtt5PropertyWriter.WriteTwoByteInt(ref writeSpan, 0x21, value);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, 2);
        return Mqtt5PropertyReader.ReadTwoByteInt(ref readSpan);
    }

    private static uint FourByteIntRoundtrip(uint value)
    {
        var buf = new byte[16];
        var writeSpan = buf.AsSpan();
        Mqtt5PropertyWriter.WriteFourByteInt(ref writeSpan, 0x02, value);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, 4);
        return Mqtt5PropertyReader.ReadFourByteInt(ref readSpan);
    }

    private static (bool ok, uint result) VariableByteIntRoundtrip(uint value)
    {
        var buf = new byte[16];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteVariableByteInt(ref writeSpan, 0x0B, value);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        var ok = Mqtt5PropertyReader.TryReadVariableByteInt(ref readSpan, out var result);
        return (ok, result);
    }

    private static string Utf8StringRoundtrip(byte id, string value)
    {
        var maxSize = System.Text.Encoding.UTF8.GetByteCount(value) + 16;
        var buf = new byte[maxSize];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteUtf8String(ref writeSpan, id, value);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        return Mqtt5PropertyReader.ReadUtf8String(ref readSpan);
    }

    private static (string key, string value) StringPairRoundtrip(string key, string val)
    {
        var maxSize = System.Text.Encoding.UTF8.GetByteCount(key)
                    + System.Text.Encoding.UTF8.GetByteCount(val) + 16;
        var buf = new byte[maxSize];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteStringPair(ref writeSpan, key, val);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1); // skip 0x26 identifier
        return Mqtt5PropertyReader.ReadStringPair(ref readSpan);
    }

    private static byte[] BinaryDataRoundtrip(byte[] original)
    {
        var buf = new byte[original.Length + 16];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteBinaryData(ref writeSpan, 0x09, original);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        return Mqtt5PropertyReader.ReadBinaryData(ref readSpan).ToArray();
    }

    // ── Byte ──────────────────────────────────────────────────────────────

    [Fact]
    public void Byte_PropertyRoundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.PayloadFormatIndicator;
        const byte original = 0xAB;

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteByte(ref writeSpan, id, original);
        written.Should().Be(2);

        buf[0].Should().Be(id);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, 1);
        var readValue = Mqtt5PropertyReader.ReadByte(ref readSpan);
        readValue.Should().Be(original);
    }

    // ── Two Byte Integer ──────────────────────────────────────────────────

    [Fact]
    public void TwoByteInt_PropertyRoundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.ReceiveMaximum;
        const ushort original = 0xBEEF;

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteTwoByteInt(ref writeSpan, id, original);
        written.Should().Be(3);
        buf[0].Should().Be(id);

        var readSpan = new ReadOnlySpan<byte>(buf, 1, 2);
        var readValue = Mqtt5PropertyReader.ReadTwoByteInt(ref readSpan);
        readValue.Should().Be(original);
    }

    // ── Four Byte Integer ─────────────────────────────────────────────────

    [Fact]
    public void FourByteInt_PropertyRoundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.SessionExpiryInterval;
        const uint original = 0xDEADBEEF;

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteFourByteInt(ref writeSpan, id, original);
        written.Should().Be(5);
        buf[0].Should().Be(id);

        var readSpan = new ReadOnlySpan<byte>(buf, 1, 4);
        var readValue = Mqtt5PropertyReader.ReadFourByteInt(ref readSpan);
        readValue.Should().Be(original);
    }

    // ── Variable Byte Integer ─────────────────────────────────────────────

    [Theory]
    [InlineData(0u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(16383u)]
    [InlineData(16384u)]
    [InlineData(2097151u)]
    [InlineData(2097152u)]
    [InlineData(268435455u)]
    public void VariableByteInt_BoundaryValues_Roundtrip(uint original)
    {
        const byte id = Mqtt5PropertyIdentifiers.SubscriptionIdentifier;

        var buf = new byte[16];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteVariableByteInt(ref writeSpan, id, original);

        var expectedVbiSize = Mqtt5PropertyWriter.GetVariableByteIntSize(original);
        written.Should().Be(1 + expectedVbiSize, $"VBI({original}) should be {expectedVbiSize} byte(s) + 1 id byte");
        buf[0].Should().Be(id);

        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        Mqtt5PropertyReader.TryReadVariableByteInt(ref readSpan, out var readValue)
            .Should().BeTrue("VBI should parse successfully");
        readValue.Should().Be(original);
    }

    // ── UTF-8 String ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]           // empty
    [InlineData("hello")]      // ASCII
    [InlineData("topic/path")] // ASCII with slash
    [InlineData("日本語")]      // multi-byte UTF-8
    [InlineData("emoji 🎉")]   // 4-byte emoji
    public void Utf8String_EdgeCases_Roundtrip(string original)
    {
        const byte id = Mqtt5PropertyIdentifiers.ContentType;
        var result = Utf8StringRoundtrip(id, original);
        result.Should().Be(original);
    }

    // ── UTF-8 String Pair ─────────────────────────────────────────────────

    [Fact]
    public void StringPair_Roundtrips()
    {
        const string key = "x-custom-header";
        const string val = "some-value";

        var buf = new byte[256];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteStringPair(ref writeSpan, key, val);

        buf[0].Should().Be(Mqtt5PropertyIdentifiers.UserProperty);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        var (readKey, readValue) = Mqtt5PropertyReader.ReadStringPair(ref readSpan);
        readKey.Should().Be(key);
        readValue.Should().Be(val);
    }

    [Fact]
    public void StringPair_EmptyKeyAndValue_Roundtrips()
    {
        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteStringPair(ref writeSpan, "", "");

        // id(1) + len(2) + "" + len(2) + "" = 5
        written.Should().Be(5);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        var (k, v) = Mqtt5PropertyReader.ReadStringPair(ref readSpan);
        k.Should().Be("");
        v.Should().Be("");
    }

    // ── Binary Data ───────────────────────────────────────────────────────

    [Fact]
    public void BinaryData_Empty_Roundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.CorrelationData;
        var original = Array.Empty<byte>();

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteBinaryData(ref writeSpan, id, original);
        written.Should().Be(3); // id + 2-byte length + 0 data bytes

        buf[0].Should().Be(id);
        var readSpan = new ReadOnlySpan<byte>(buf, 1, 2); // id already skipped, read length(2) + data(0)
        var readValue = Mqtt5PropertyReader.ReadBinaryData(ref readSpan);
        readValue.Length.Should().Be(0);
    }

    [Fact]
    public void BinaryData_OneByte_Roundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.AuthenticationData;
        byte[] original = [0xFF];

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteBinaryData(ref writeSpan, id, original);
        written.Should().Be(4); // id(1) + len(2) + data(1)

        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        var readValue = Mqtt5PropertyReader.ReadBinaryData(ref readSpan);
        readValue.ToArray().Should().BeEquivalentTo(original);
    }

    [Fact]
    public void BinaryData_MultipleBytes_Roundtrips()
    {
        const byte id = Mqtt5PropertyIdentifiers.CorrelationData;
        byte[] original = [0x01, 0x02, 0x03, 0xAA, 0xBB, 0xCC];

        var buf = new byte[64];
        var writeSpan = buf.AsSpan();
        var written = Mqtt5PropertyWriter.WriteBinaryData(ref writeSpan, id, original);
        written.Should().Be(3 + original.Length);

        var readSpan = new ReadOnlySpan<byte>(buf, 1, written - 1);
        var readValue = Mqtt5PropertyReader.ReadBinaryData(ref readSpan);
        readValue.ToArray().Should().BeEquivalentTo(original);
    }

    // ── GetVariableByteIntSize helper ─────────────────────────────────────

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(16383u, 2)]
    [InlineData(16384u, 3)]
    [InlineData(2097151u, 3)]
    [InlineData(2097152u, 4)]
    [InlineData(268435455u, 4)]
    public void GetVariableByteIntSize_ReturnsCorrectSize(uint value, int expectedSize)
    {
        Mqtt5PropertyWriter.GetVariableByteIntSize(value).Should().Be(expectedSize);
    }

    // ── Unknown property identifier ───────────────────────────────────────

    [Fact]
    public void ThrowUnknownPropertyIdentifier_ThrowsMqttDecoderException()
    {
        const byte unknownId = 0x7F; // not a valid MQTT 5.0 property identifier

        var act = () => Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(unknownId);
        act.Should().Throw<MqttDecoderException>()
            .WithMessage("*0x7F*");
    }

    [Fact]
    public void ThrowUnknownPropertyIdentifier_MessageContainsProtocolErrorReference()
    {
        var act = () => Mqtt5PropertyReader.ThrowUnknownPropertyIdentifier(0x00);
        act.Should().Throw<MqttDecoderException>()
            .Which.ProtocolVersion.Should().Be(MqttProtocolVersion.V5_0);
    }

    // ── Reader buffer-too-short guards ────────────────────────────────────
    // Note: ReadOnlySpan<byte> is a ref struct and cannot be captured in lambdas,
    // so these tests use direct try/catch instead of FluentAssertions lambda syntax.

    [Fact]
    public void ReadByte_EmptyBuffer_ThrowsMqttDecoderException()
    {
        MqttDecoderException? caught = null;
        try
        {
            var empty = new ReadOnlySpan<byte>(Array.Empty<byte>());
            Mqtt5PropertyReader.ReadByte(ref empty);
        }
        catch (MqttDecoderException ex) { caught = ex; }
        caught.Should().NotBeNull("ReadByte on empty buffer must throw MqttDecoderException");
    }

    [Fact]
    public void ReadTwoByteInt_OneByte_ThrowsMqttDecoderException()
    {
        MqttDecoderException? caught = null;
        try
        {
            var span = new ReadOnlySpan<byte>(new byte[] { 0x01 });
            Mqtt5PropertyReader.ReadTwoByteInt(ref span);
        }
        catch (MqttDecoderException ex) { caught = ex; }
        caught.Should().NotBeNull("ReadTwoByteInt with 1 byte must throw MqttDecoderException");
    }

    [Fact]
    public void ReadFourByteInt_ThreeBytes_ThrowsMqttDecoderException()
    {
        MqttDecoderException? caught = null;
        try
        {
            var span = new ReadOnlySpan<byte>(new byte[] { 0x01, 0x02, 0x03 });
            Mqtt5PropertyReader.ReadFourByteInt(ref span);
        }
        catch (MqttDecoderException ex) { caught = ex; }
        caught.Should().NotBeNull("ReadFourByteInt with 3 bytes must throw MqttDecoderException");
    }

    [Fact]
    public void ReadUtf8String_DeclaredLengthExceedsBuffer_ThrowsMqttDecoderException()
    {
        // Length prefix says 100 bytes but buffer only has 2 bytes after the prefix
        MqttDecoderException? caught = null;
        try
        {
            var span = new ReadOnlySpan<byte>(new byte[] { 0x00, 0x64, 0x01, 0x02 }); // length=100, data=2 bytes
            Mqtt5PropertyReader.ReadUtf8String(ref span);
        }
        catch (MqttDecoderException ex) { caught = ex; }
        caught.Should().NotBeNull("ReadUtf8String with truncated buffer must throw MqttDecoderException");
    }

    [Fact]
    public void TryReadVariableByteInt_EmptyBuffer_ReturnsFalse()
    {
        var data = Array.Empty<byte>();
        var empty = new ReadOnlySpan<byte>(data);
        Mqtt5PropertyReader.TryReadVariableByteInt(ref empty, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadVariableByteInt_FiveByteEncoding_ReturnsFalse()
    {
        // All 4 bytes have the continuation bit set — encoding exceeds max 4 bytes
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };
        var span = new ReadOnlySpan<byte>(data);
        // 5-byte encoding is invalid in MQTT; stops at index 4 and returns false
        Mqtt5PropertyReader.TryReadVariableByteInt(ref span, out _).Should().BeFalse();
    }

    // ── FsCheck property: random values roundtrip ─────────────────────────

    [Property]
    public Property RandomByte_Roundtrips()
    {
        return Prop.ForAll(
            Arb.From<byte>(),
            Arb.From<byte>(),
            (id, original) => ByteRoundtrip(id, original) == original);
    }

    [Property]
    public Property RandomTwoByteInt_Roundtrips()
    {
        return Prop.ForAll(
            Arb.From<ushort>(),
            original => TwoByteIntRoundtrip(original) == original);
    }

    [Property]
    public Property RandomFourByteInt_Roundtrips()
    {
        return Prop.ForAll(
            Arb.From<uint>(),
            original => FourByteIntRoundtrip(original) == original);
    }

    [Property]
    public Property RandomVariableByteInt_InRange_Roundtrips()
    {
        // VBI max value is 268435455 (0xFFFFFFF)
        var gen = Gen.Choose(0, 268435455).Select(i => (uint)i);
        return Prop.ForAll(Arb.From(gen), original =>
        {
            var (ok, result) = VariableByteIntRoundtrip(original);
            return ok && result == original;
        });
    }

    [Property]
    public Property RandomUtf8String_WithoutNullChars_Roundtrips()
    {
        // Filter out null characters (MQTT §1.5.4 forbids U+0000 in UTF-8 strings)
        var gen = Arb.Generate<string>()
            .Where(s => s != null && !s.Contains('\0'));
        return Prop.ForAll(Arb.From(gen), original =>
            Utf8StringRoundtrip(Mqtt5PropertyIdentifiers.ContentType, original) == original);
    }

    [Property]
    public Property RandomBinaryData_Roundtrips()
    {
        return Prop.ForAll(
            Arb.From<byte[]>().Filter(b => b != null),
            original => BinaryDataRoundtrip(original).SequenceEqual(original));
    }

    [Property]
    public Property RandomStringPair_WithoutNullChars_Roundtrips()
    {
        var gen = from key in Arb.Generate<string>()
                      .Where(s => s != null && !s.Contains('\0'))
                  from val in Arb.Generate<string>()
                      .Where(s => s != null && !s.Contains('\0'))
                  select (key, val);

        return Prop.ForAll(Arb.From(gen), pair =>
        {
            var (key, val) = pair;
            var (rKey, rVal) = StringPairRoundtrip(key, val);
            return rKey == key && rVal == val;
        });
    }
}
