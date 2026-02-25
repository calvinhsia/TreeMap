using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TreeMap
{
    /// <summary>
    /// Result of a disk scan operation, including data and any errors encountered.
    /// Contains instance methods to populate and recalculate sizes so a ScanResult
    /// can perform the scan operation itself.
    /// Renamed from ScanResult to DiskScanResult for clarity.
    /// </summary>
    public class DiskScanResult
    {
        public ConcurrentDictionary<string, MapDataItem> Data { get; } = new();
        public List<ScanError> Errors { get; } = new();
        public int SkippedSymlinks { get; set; }
        public int CloudFileCount { get; set; }
        public long CloudFileLogicalSize { get; set; } // Size if all cloud files were downloaded
        // RootPath of the scan (trailing path separator included). Set by ScanWithErrorsAsync so callers
        // don't need to infer the root from the dictionary keys.
        public string? RootPath { get; set; }

        public bool HasErrors => Errors.Count > 0;
        public string ErrorSummary => HasErrors
            ? $"{Errors.Count} error(s): {Errors[0].Message}{(Errors.Count > 1 ? $" (+{Errors.Count - 1} more)" : "")}"
            : "";

        // OneDrive/cloud file attributes (Windows 10+)
        // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000 - file data is not locally available
        // FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000 - file data will be recalled on open
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
        private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

        // Estimated size of a cloud placeholder file on disk
        private const long PlaceholderSizeEstimate = 1024; // ~1KB

        /// <summary>
        /// Calculates the display size based on cloud file handling option.
        /// </summary>
        private static long CalculateSize(long localSize, long cloudLogicalSize, int cloudFileCount, CloudFileHandling cloudHandling)
        {
            return cloudHandling switch
            {
                CloudFileHandling.ExcludeFromSize => localSize,
                CloudFileHandling.IncludePlaceholderSize => localSize + (cloudFileCount > 0 ? cloudFileCount * PlaceholderSizeEstimate : (cloudLogicalSize > 0 ? PlaceholderSizeEstimate : 0)),
                _ => localSize + cloudLogicalSize // IncludeLogicalSize (default)
            };
        }

        /// <summary>
        /// Recalculates all sizes in this scan result based on a new cloud handling option.
        /// </summary>
        public void RecalculateSizes(CloudFileHandling cloudHandling)
        {
            foreach (var kvp in Data)
            {
                var item = kvp.Value;
                item.Size = CalculateSize(item.LocalSize, item.CloudLogicalSize, item.CloudFileCount, cloudHandling);
            }
        }

        /// <summary>
        /// Populate this DiskScanResult by scanning the given root path.
        /// </summary>
        public async Task PopulateAsync(string rootPath, IProgress<string>? progress = null, System.Threading.CancellationToken cancellationToken = default, CloudFileHandling cloudHandling = CloudFileHandling.IncludeLogicalSize)
        {
            if (!rootPath.EndsWith(TreeMapConstants.PathSep.ToString()))
            {
                rootPath += TreeMapConstants.PathSep;
            }
            RootPath = rootPath;
            await ScanInternal(rootPath, rootPath, progress, cancellationToken, cloudHandling).ConfigureAwait(false);
        }

        // Helper methods previously on the DiskScanner static helper are now private members
        // so the DiskScanResult is self-contained.
        private long GetFileSizeSafe(FileInfo finfo)
        {
            try
            {
                return finfo.Length;
            }
            catch (FileNotFoundException ex)
            {
                this.Errors.Add(new ScanError { Path = finfo.FullName, Message = "File not found", ExceptionType = ex.GetType().Name });
                return 0;
            }
            catch (IOException ex)
            {
                this.Errors.Add(new ScanError { Path = finfo.FullName, Message = ex.Message, ExceptionType = ex.GetType().Name });
                return 0;
            }
        }

        private void LogError(string path, Exception ex)
        {
            this.Errors.Add(new ScanError
            {
                Path = path,
                Message = ex.Message,
                ExceptionType = ex.GetType().Name
            });
        }

        private static bool IsCloudOnlyFile(FileAttributes attrs)
        {
            if ((attrs & RecallOnDataAccess) != 0 || (attrs & RecallOnOpen) != 0)
            {
                return true;
            }
            if ((attrs & FileAttributes.Offline) != 0)
            {
                return true;
            }
            return false;
        }

        private async Task<(long localSize, long cloudLogicalSize, int fileCount)> ScanInternal(string cPath, string rootPath, IProgress<string>? progress, System.Threading.CancellationToken cancellationToken, CloudFileHandling cloudHandling = CloudFileHandling.IncludeLogicalSize)
        {
            var dict = this.Data;
            long curdirLocalFileSize = 0;
            long curdirCloudLogicalSize = 0;
            long childLocalSize = 0;
            long childCloudLogicalSize = 0;
            int curdirFileCount = 0;
            int childFileCount = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = cPath.StartsWith(rootPath) ? cPath[rootPath.Length..] : cPath; if (string.IsNullOrEmpty(relativePath)) relativePath = "."; progress?.Report(relativePath);

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(cPath);
                if (!dirInfo.Exists)
                {
                    LogError(cPath, new DirectoryNotFoundException($"Directory not found: {cPath}"));
                    return (0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                LogError(cPath, ex);
                return (0, 0, 0);
            }

            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                this.SkippedSymlinks++;
                return (0, 0, 0);
            }

            var nDepth = cPath.Where(c => c == TreeMapConstants.PathSep).Count();
            string[] curDirFiles = [];
            try
            {
                curDirFiles = Directory.GetFiles(cPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError(cPath, ex);
                return (0, 0, 0);
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError(cPath, ex);
                return (0, 0, 0);
            }
            catch (IOException ex)
            {
                LogError(cPath, ex);
                return (0, 0, 0);
            }

            if (curDirFiles.Length > 0)
            {
                bool hasCloudFiles = false;
                int cloudFileCountInDir = 0;
                foreach (var file in curDirFiles)
                {
                    try
                    {
                        var finfo = new FileInfo(file);
                        var logicalSize = GetFileSizeSafe(finfo);
                        if (IsCloudOnlyFile(finfo.Attributes))
                        {
                            this.CloudFileCount++;
                            this.CloudFileLogicalSize += logicalSize;
                            cloudFileCountInDir++;
                            hasCloudFiles = true;
                            curdirCloudLogicalSize += logicalSize;
                        }
                        else
                        {
                            curdirLocalFileSize += logicalSize;
                        }
                    }
                    catch (PathTooLongException ex)
                    {
                        LogError(file, ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        LogError(file, ex);
                    }
                    catch (IOException ex)
                    {
                        LogError(file, ex);
                    }
                }
                dict[cPath + TreeMapConstants.DataSuffix] = new MapDataItem()
                {
                    Depth = nDepth + 1,
                    Size = CalculateSize(curdirLocalFileSize, curdirCloudLogicalSize, cloudFileCountInDir, cloudHandling),
                    LocalSize = curdirLocalFileSize,
                    CloudLogicalSize = curdirCloudLogicalSize,
                    NumFiles = curDirFiles.Length,
                    Index = dict.Count,
                    IsCloudOnly = hasCloudFiles,
                    CloudFileCount = cloudFileCountInDir
                };
                curdirFileCount = curDirFiles.Length;
            }

            string[] curDirFolders = [];
            try
            {
                curDirFolders = Directory.GetDirectories(cPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError(cPath, ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError(cPath, ex);
            }
            catch (IOException ex)
            {
                LogError(cPath, ex);
            }

            if (curDirFolders.Length > 0)
            {
                foreach (var dir in curDirFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var childPath = Path.Combine(cPath, Path.GetFileName(dir));
                    if (!childPath.EndsWith(TreeMapConstants.PathSep.ToString()))
                        childPath += TreeMapConstants.PathSep;
                    var (childLocal, childCloud, childFiles) = await ScanInternal(childPath, rootPath, progress, cancellationToken, cloudHandling).ConfigureAwait(false);
                    childLocalSize += childLocal;
                    childCloudLogicalSize += childCloud;
                    childFileCount += childFiles;
                }
            }

            var totalLocalSize = curdirLocalFileSize + childLocalSize;
            var totalCloudLogicalSize = curdirCloudLogicalSize + childCloudLogicalSize;
            var totalFileCount = curdirFileCount + childFileCount;
            dict[cPath] = new MapDataItem()
            {
                Depth = nDepth,
                Size = CalculateSize(totalLocalSize, totalCloudLogicalSize, 0, cloudHandling),
                LocalSize = totalLocalSize,
                CloudLogicalSize = totalCloudLogicalSize,
                NumFiles = totalFileCount,
                Index = dict.Count
            };

            return (totalLocalSize, totalCloudLogicalSize, totalFileCount);
        }
    }

    public class ScanError
    {
        public string Path { get; init; } = "";
        public string Message { get; init; } = "";
        public string ExceptionType { get; init; } = "";
    }

    /// <summary>
    /// Options for how to handle cloud-only files during scanning.
    /// </summary>
    public enum CloudFileHandling
    {
        /// <summary>Include cloud files with their logical size (as if downloaded)</summary>
        IncludeLogicalSize,
        /// <summary>Exclude cloud files from size calculations (they use minimal actual space)</summary>
        ExcludeFromSize,
        /// <summary>Include cloud files with estimated placeholder size (~1KB each)</summary>
        IncludePlaceholderSize
    }




}
