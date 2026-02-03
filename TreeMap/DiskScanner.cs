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
        public static ConcurrentDictionary<string, MapDataItem> Scan(string rootPath)
        {
            // backward-compatible synchronous API; run the async scan and wait
            return ScanAsync(rootPath, null, default).GetAwaiter().GetResult();
        }

        public static Task<ConcurrentDictionary<string, MapDataItem>> ScanAsync(string rootPath, IProgress<string>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            var dict = new ConcurrentDictionary<string, MapDataItem>();
            if (!rootPath.EndsWith(TreeMapConstants.PathSep.ToString()))
            {
                rootPath += TreeMapConstants.PathSep;
            }

            // Run the recursive scan on a background thread to avoid blocking the caller
            return Task.Run(async () =>
            {
                await ScanInternal(rootPath, dict, progress, cancellationToken).ConfigureAwait(false);
                return dict;
            }, cancellationToken);
        }

        private static async Task<long> ScanInternal(string cPath, ConcurrentDictionary<string, MapDataItem> dict, IProgress<string>? progress, System.Threading.CancellationToken cancellationToken)
        {
            long totalSize = 0;
            long curdirFileSize = 0;
            long curdirFolderSize = 0;

            // Check cancellation and report progress
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(cPath);

            var dirInfo = new DirectoryInfo(cPath);
            if (!dirInfo.Exists)
                return 0;
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return 0;
            }
            var nDepth = cPath.Where(c => c == TreeMapConstants.PathSep).Count();
            string[] curDirFiles = Array.Empty<string>();
            try
            {
                curDirFiles = Directory.GetFiles(cPath);
            }
            catch (UnauthorizedAccessException) { return 0; }
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
                dict[cPath + TreeMapConstants.DataSuffix] = new MapDataItem()
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
                    cancellationToken.ThrowIfCancellationRequested();
                    // ensure trailing sep
                    var childPath = Path.Combine(cPath, Path.GetFileName(dir));
                    if (!childPath.EndsWith(TreeMapConstants.PathSep.ToString()))
                        childPath += TreeMapConstants.PathSep;
                    curdirFolderSize += await ScanInternal(childPath, dict, progress, cancellationToken).ConfigureAwait(false);
                }
            }
            totalSize += curdirFileSize + curdirFolderSize;
            dict[cPath] = new MapDataItem() { Depth = nDepth, Size = curdirFileSize + curdirFolderSize, Index = dict.Count };

            return totalSize;
        }
    }
}
