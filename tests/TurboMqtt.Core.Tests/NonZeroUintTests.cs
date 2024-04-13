namespace TurboMqtt.Core.Tests;

public class NonZeroUintTests
{
    [Fact]
    public void ShouldFailOnZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NonZeroUInt32(0));
    }
    
    [Fact]
    public void ShouldSucceedOnNonZero()
    {
        var nonZero = new NonZeroUInt32(1);
        Assert.Equal(1u, nonZero.Value);
    }
}