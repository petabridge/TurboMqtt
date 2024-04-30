// -----------------------------------------------------------------------
// <copyright file="NonWindowsTheoryAttribute.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace TurboMqtt.Tests;

/// <summary>
/// <para>
/// This custom XUnit Fact attribute will skip unit tests if the run-time environment is windows
/// </para>
/// <para>
/// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
/// any unit tests with <see cref="NonWindowsFactAttribute"/> with its <see cref="Skip"/> property
/// set will always be skipped, regardless of the environment variable content.
/// </para>
/// </summary>
public class NonWindowsFactAttribute : FactAttribute
{
    private string? _skip;

    /// <summary>
    /// Marks the test so that it will not be run, and gets or sets the skip reason
    /// </summary>
    public override string Skip
    {
        get
        {
            if (_skip != null)
                return _skip;
                
            var platform = Environment.OSVersion.Platform;
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            return isWindows ? SkipUnix ?? "Skipped on Windows platforms" : string.Empty;
        }
        set => _skip = value;
    }

    /// <summary>
    /// The reason why this unit test is being skipped by the <see cref="NonWindowsFactAttribute"/>.
    /// Note that the original <see cref="Skip"/> property takes precedence over this message. 
    /// </summary>
    public string? SkipUnix { get; set; }
}

/// <summary>
/// <para>
/// This custom XUnit Fact attribute will skip unit tests if the run-time environment is windows
/// </para>
/// <para>
/// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
/// any unit tests with <see cref="NonWindowsTheoryAttribute"/> with its <see cref="Skip"/> property
/// set will always be skipped, regardless of the environment variable content.
/// </para>
/// </summary>
public class NonWindowsTheoryAttribute : TheoryAttribute
{
    private string? _skip;

    /// <summary>
    /// Marks the test so that it will not be run, and gets or sets the skip reason
    /// </summary>
    public override string Skip
    {
        get
        {
            if (_skip != null)
                return _skip;
            
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            return isWindows ? SkipUnix ?? "Skipped on Windows platforms" : string.Empty;
        }
        set => _skip = value;
    }

    /// <summary>
    /// The reason why this unit test is being skipped by the <see cref="NonWindowsFactAttribute"/>.
    /// Note that the original <see cref="Skip"/> property takes precedence over this message. 
    /// </summary>
    public string? SkipUnix { get; set; }
}