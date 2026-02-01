namespace TreeMap.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        // simple sanity check
        Xunit.Assert.True(true);
    }

    [Fact]
    public void MathWorks()
    {
        Xunit.Assert.Equal(2, 1 + 1);
    }
}