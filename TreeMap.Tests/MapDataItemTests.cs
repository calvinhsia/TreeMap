using System.IO;
using Xunit;
using TreeMap;

namespace TreeMap.Tests;

public class MapDataItemTests
{
    [Fact]
    public void PathSepAndDataSuffix_AreCorrect()
    {
        Assert.Equal(Path.DirectorySeparatorChar, MainWindow.PathSep);
        Assert.Equal("*" + Path.DirectorySeparatorChar, MainWindow.DataSuffix);
    }

    [Fact]
    public void MapDataItem_ToString_IncludesFields()
    {
        var item = new MainWindow.MapDataItem()
        {
            Depth = 2,
            Size = 100,
            NumFiles = 3,
            Index = 7
        };
        var s = item.ToString();
        Assert.Contains("Depth = 2", s);
        Assert.Contains("NumFiles = 3", s);
        Assert.Contains("Index = 7", s);
    }
}
