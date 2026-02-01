using System.IO;
using System.Linq;
using TreeMap;

namespace TreeMap.Tests;

public class DiskScannerTests
{
    [Fact]
    public void Scan_SimpleDirectoryStructure_ReturnsExpectedSizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "treemap_test_") + Path.GetRandomFileName();
        Directory.CreateDirectory(root);
        try
        {
            // create files and subfolders
            var sub = Path.Combine(root, "subfolder");
            Directory.CreateDirectory(sub);
            var f1 = Path.Combine(root, "a.txt");
            File.WriteAllText(f1, "hello"); // 5 bytes
            var f2 = Path.Combine(sub, "b.txt");
            File.WriteAllText(f2, "world!"); // 6 bytes

            var dict = DiskScanner.Scan(root);
            // ensure keys with trailing sep exist
            var rootKey = root.EndsWith(MainWindow.PathSep.ToString()) ? root : root + MainWindow.PathSep;
            var subKey = rootKey + "subfolder" + MainWindow.PathSep;

            Assert.True(dict.ContainsKey(rootKey));
            Assert.True(dict.ContainsKey(subKey));
            Assert.True(dict.ContainsKey(subKey + MainWindow.DataSuffix));

            var rootItem = dict[rootKey];
            var subItem = dict[subKey];
            var subFilesItem = dict[subKey + MainWindow.DataSuffix];

            Assert.Equal(11, rootItem.Size); // 5 + 6
            Assert.Equal(6, subFilesItem.Size);
            Assert.Equal(6, subItem.Size);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
