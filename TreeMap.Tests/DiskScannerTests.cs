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
            var rootKey = root.EndsWith(TreeMap.TreeMapConstants.PathSep.ToString()) ? root : root + TreeMap.TreeMapConstants.PathSep;
            var subKey = rootKey + "subfolder" + TreeMap.TreeMapConstants.PathSep;

            Assert.True(dict.ContainsKey(rootKey));
            Assert.True(dict.ContainsKey(subKey));
            Assert.True(dict.ContainsKey(subKey + TreeMap.TreeMapConstants.DataSuffix));

            var rootItem = dict[rootKey];
            var subItem = dict[subKey];
            var subFilesItem = dict[subKey + TreeMap.TreeMapConstants.DataSuffix];

            Assert.Equal(11, rootItem.Size); // 5 + 6
            Assert.Equal(6, subFilesItem.Size);
            Assert.Equal(6, subItem.Size);

            // Verify file counts are tracked (both direct and recursive)
            var rootFilesItem = dict[rootKey + TreeMap.TreeMapConstants.DataSuffix];
            Assert.Equal(1, rootFilesItem.NumFiles); // a.txt (direct files in root)
            Assert.Equal(1, subFilesItem.NumFiles);  // b.txt (direct files in subfolder)

            // Folder entries should have recursive file counts
            Assert.Equal(2, rootItem.NumFiles); // a.txt + b.txt (recursive total)
            Assert.Equal(1, subItem.NumFiles);  // b.txt (recursive total for subfolder)
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
