// -----------------------------------------------------------------------
// <copyright file="MqttTopicValidatorSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Tests;

public class MqttTopicValidatorSpecs
{
    public static readonly TheoryData<string> FailureCases = new TheoryData<string>(
    
        "foo/bar+", // invalid wildcard position
        "home/kit+chen/light", // invalid wildcard position
        "home/+/kitchen+", // invalid wildcard position
        "home+kitchen/light", // invalid wildcard position
        
        // generate some invalid uses of the '#' wildcard
        "foo/#/bar", // invalid wildcard position
        
        // generate some invalid uses of the '$' characterq
        "$foo/bar", // invalid use of '$' character
        
        // generate some invalid uses of the null character
        "foo\0bar" // invalid use of null character
    );
    
    [Theory]
    [MemberData(nameof(FailureCases))]
    public void ShouldFailValidationForTopicSubscription(string topic)
    {
        var result = MqttTopicValidator.ValidateTopic(topic, true);
        result.IsValid.Should().BeFalse();
    }
    
    public static readonly TheoryData<string> SuccessCases = new TheoryData<string>(
        "home/kitchen/light",
        "home/kitchen/temperature",
        "home/kitchen/humidity",
        "home/+/pressure",
        "home/kitchen/pressure/#"
    );
    
    [Theory]
    [MemberData(nameof(SuccessCases))]
    public void ShouldPassValidationForTopicSubscription(string topic)
    {
        var result = MqttTopicValidator.ValidateTopic(topic, true);
        result.IsValid.Should().BeTrue();
    }
}