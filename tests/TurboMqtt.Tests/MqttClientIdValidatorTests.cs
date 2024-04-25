// -----------------------------------------------------------------------
// <copyright file="MqttClientIdValidatorTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Tests;

public class MqttClientIdValidatorTests
{
    [Theory]
    [InlineData("", true, "Client ID is valid or will be assigned by the server.")]
    [InlineData("validClientID123", true, "Client ID is valid.")]
    [InlineData("client-ID_with.multiple:validCharacters", true, "Client ID is valid.")]
    public void ValidateClientId_ValidCases(string clientId, bool expectedIsValid, string expectedMessage)
    {
        var result = MqttClientIdValidator.ValidateClientId(clientId);
        Assert.Equal(expectedIsValid, result.IsValid);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("\u0001\u0002\u0003", false, "Client ID contains invalid characters.")]
    [InlineData("client\u007FID", false, "Client ID contains invalid characters.")]
    public void ValidateClientId_InvalidCases(string clientId, bool expectedIsValid, string expectedMessage)
    {
        var result = MqttClientIdValidator.ValidateClientId(clientId);
        Assert.Equal(expectedIsValid, result.IsValid);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }
}