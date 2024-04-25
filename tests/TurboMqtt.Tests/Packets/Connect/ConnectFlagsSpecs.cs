// -----------------------------------------------------------------------
// <copyright file="ConnectFlagsSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;

namespace TurboMqtt.Tests.Packets.Connect;

public class ConnectFlagsSpecs
{
    public class when_creating_connect_flags
    {
        [Fact]
        public void it_should_have_clean_session_flag_set_to_false_by_default()
        {
            var flags = new ConnectFlags();
            flags.CleanSession.Should().BeFalse();
        }

        [Fact]
        public void it_should_have_will_flag_set_to_false_by_default()
        {
            var flags = new ConnectFlags();
            flags.WillFlag.Should().BeFalse();
        }

        [Fact]
        public void it_should_have_will_retain_flag_set_to_false_by_default()
        {
            var flags = new ConnectFlags();
            flags.WillRetain.Should().BeFalse();
        }

        [Fact]
        public void it_should_have_will_qos_flag_set_to_0_by_default()
        {
            var flags = new ConnectFlags();
            flags.WillQoS.Should().Be(0);
        }

        [Fact]
        public void it_should_have_password_flag_set_to_false_by_default()
        {
            var flags = new ConnectFlags();
            flags.PasswordFlag.Should().BeFalse();
        }

        [Fact]
        public void it_should_have_username_flag_set_to_false_by_default()
        {
            var flags = new ConnectFlags();
            flags.UsernameFlag.Should().BeFalse();
        }
    }

    // create test cases for serializing and deserializing ConnectFlags
    public class when_serializing_and_deserializing_connect_flags
    {
        [Theory]
        [InlineData(QualityOfService.AtMostOnce)]
        [InlineData(QualityOfService.AtLeastOnce)]
        [InlineData(QualityOfService.ExactlyOnce)]
        public void it_should_correctly_serialize_and_deserialize_flags_MQTT311_with_WillFlag(QualityOfService targetQualityOfService)
        {
            var flags = new ConnectFlags
            {
                CleanSession = false,
                WillFlag = true,
                WillRetain = true,
                WillQoS = targetQualityOfService,
                PasswordFlag = true,
                UsernameFlag = true
            };

            var bytes = flags.Encode();
            var deserialized = ConnectFlags.Decode(bytes);

            deserialized.CleanSession.Should().BeFalse();
            deserialized.WillFlag.Should().BeTrue();
            deserialized.WillRetain.Should().BeTrue();
            deserialized.WillQoS.Should().Be(targetQualityOfService);
            deserialized.PasswordFlag.Should().BeTrue();
            deserialized.UsernameFlag.Should().BeTrue();
        }
        
        [Fact]
        public void it_should_correctly_serialize_and_deserialize_flags_MQTT311_without_WillFlag()
        {
            var flags = new ConnectFlags
            {
                CleanSession = false,
                PasswordFlag = true,
                UsernameFlag = true
            };

            var bytes = flags.Encode();
            var deserialized = ConnectFlags.Decode(bytes);

            deserialized.CleanSession.Should().BeFalse();
            deserialized.WillFlag.Should().BeFalse();
            deserialized.WillRetain.Should().BeFalse(); // WillRetain is not supported in MQTT 3.1.1, so this should be false
            deserialized.PasswordFlag.Should().BeTrue();
            deserialized.UsernameFlag.Should().BeTrue();
        }
        
        [Theory]
        [InlineData(QualityOfService.AtMostOnce)]
        [InlineData(QualityOfService.AtLeastOnce)]
        [InlineData(QualityOfService.ExactlyOnce)]
        public void it_should_correctly_serialize_and_deserialize_flags_MQTT5(QualityOfService targetQualityOfService)
        {
            var flags = new ConnectFlags
            {
                CleanSession = false,
                WillFlag = true,
                WillRetain = true,
                WillQoS = targetQualityOfService,
                PasswordFlag = true,
                UsernameFlag = true
            };

            var bytes = flags.Encode();
            var deserialized = ConnectFlags.Decode(bytes);

            deserialized.CleanSession.Should().BeFalse();
            deserialized.WillFlag.Should().BeTrue();
            deserialized.WillRetain.Should().BeTrue(); // WillRetain is supported in MQTT 5.0, so this should be true
            deserialized.WillQoS.Should().Be(targetQualityOfService);
            deserialized.PasswordFlag.Should().BeTrue();
            deserialized.UsernameFlag.Should().BeTrue();
        }
    }
}