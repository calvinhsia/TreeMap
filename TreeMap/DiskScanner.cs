using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TreeMap
{
    // Simple, testable scanner that mirrors the directory scanning logic from MainWindow
    public static class DiskScanner
    {
        public static ConcurrentDictionary<string, MainWindow.MapDataItem> Scan(string rootPath)
        {
            var dict = new ConcurrentDictionary<string, MainWindow.MapDataItem>();
            if (!rootPath.EndsWith(MainWindow.PathSep.ToString()))
            {
                rootPath += MainWindow.PathSep;
            }
            // run synchronously but use recursion
            ScanInternal(rootPath, dict).GetAwaiter().GetResult();
            return dict;
        }

        private static async Task<long> ScanInternal(string cPath, ConcurrentDictionary<string, MainWindow.MapDataItem> dict)
        {
            long totalSize = 0;
            long curdirFileSize = 0;
            long curdirFolderSize = 0;

            await Task.Run(async () =>
            {
                var dirInfo = new DirectoryInfo(cPath);
                if (!dirInfo.Exists)
                    return;
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }
                var nDepth = cPath.Where(c => c == MainWindow.PathSep).Count();
                var curDirFiles = Array.Empty<string>();
                try
                {
                    curDirFiles = Directory.GetFiles(cPath);
                }
                catch (UnauthorizedAccessException) { return; }
                if (curDirFiles.Length > 0)
                {
                    foreach (var file in curDirFiles)
                    {
                        try
                        {
                            var finfo = new FileInfo(file);
                            curdirFileSize += finfo.Length;
                        }
                        catch (PathTooLongException) { }
                    }
                    dict[cPath + MainWindow.DataSuffix] = new MainWindow.MapDataItem()
                    {
                        Depth = nDepth + 1,
                        Size = curdirFileSize,
                        NumFiles = curDirFiles.Length,
                        Index = dict.Count
                    };
                }
                string[] curDirFolders = Array.Empty<string>();
                try
                {
                    curDirFolders = Directory.GetDirectories(cPath);
                }
                catch (UnauthorizedAccessException) { }
                if (curDirFolders.Length > 0)
                {
                    foreach (var dir in curDirFolders)
                    {
                        // ensure trailing sep
                        var childPath = Path.Combine(cPath, Path.GetFileName(dir));
                        if (!childPath.EndsWith(MainWindow.PathSep.ToString()))
                            childPath += MainWindow.PathSep;
                        curdirFolderSize += await ScanInternal(childPath, dict);
                    }
                }
                totalSize += curdirFileSize + curdirFolderSize;
                dict[cPath] = new MainWindow.MapDataItem() { Depth = nDepth, Size = curdirFileSize + curdirFolderSize, Index = dict.Count };
            });

            return totalSize;
        }
    }
}
