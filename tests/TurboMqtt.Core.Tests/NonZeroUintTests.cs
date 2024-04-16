namespace TurboMqtt.Core.Tests;

public class NonZeroUintTests
{
    [Fact]
    public void ShouldFailOnZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NonZeroUInt16(0));
    }
    
    [Fact]
    public void ShouldSucceedOnNonZero()
    {
        var nonZero = new NonZeroUInt16(1);
        Assert.Equal(1u, nonZero.Value);
    }
}