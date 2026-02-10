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
    /// </summary>
    public class ScanResult
    {
        public ConcurrentDictionary<string, MapDataItem> Data { get; } = new();
        public List<ScanError> Errors { get; } = new();
        public int SkippedSymlinks { get; set; }
        public int CloudFileCount { get; set; }
        public long CloudFileLogicalSize { get; set; } // Size if all cloud files were downloaded

        public bool HasErrors => Errors.Count > 0;
        public string ErrorSummary => HasErrors 
            ? $"{Errors.Count} error(s): {Errors[0].Message}{(Errors.Count > 1 ? $" (+{Errors.Count - 1} more)" : "")}"
            : "";
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

    // Simple, testable scanner that mirrors the directory scanning logic from MainWindow
    public static class DiskScanner
    {
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
        /// Recalculates all sizes in a scan result based on a new cloud handling option.
        /// This avoids needing to rescan the disk when the user changes the cloud handling preference.
        /// </summary>
        public static void RecalculateSizes(ScanResult result, CloudFileHandling cloudHandling)
        {
            foreach (var kvp in result.Data)
            {
                var item = kvp.Value;
                item.Size = CalculateSize(item.LocalSize, item.CloudLogicalSize, item.CloudFileCount, cloudHandling);
            }
        }

        public static ConcurrentDictionary<string, MapDataItem> Scan(string rootPath)
        {
            // backward-compatible synchronous API; run the async scan and wait
            return ScanAsync(rootPath, null, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Scans a directory and returns detailed results including any errors.
        /// </summary>
        /// <param name="rootPath">The root directory to scan</param>
        /// <param name="progress">Optional progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="cloudHandling">How to handle cloud-only files (default: include logical size)</param>
        public static Task<ScanResult> ScanWithErrorsAsync(
            string rootPath, 
            IProgress<string>? progress = null, 
            System.Threading.CancellationToken cancellationToken = default,
            CloudFileHandling cloudHandling = CloudFileHandling.IncludeLogicalSize)
        {
            var result = new ScanResult();
            if (!rootPath.EndsWith(TreeMapConstants.PathSep.ToString()))
            {
                rootPath += TreeMapConstants.PathSep;
            }

            return Task.Run(async () =>
            {
                await ScanInternal(rootPath, rootPath, result, progress, cancellationToken, cloudHandling).ConfigureAwait(false);
                return result;
            }, cancellationToken);
        }

        public static Task<ConcurrentDictionary<string, MapDataItem>> ScanAsync(string rootPath, IProgress<string>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            // For backward compatibility, just return the data dictionary
            return ScanWithErrorsAsync(rootPath, progress, cancellationToken)
                .ContinueWith(t => t.Result.Data, cancellationToken);
        }

        /// <summary>
        /// Checks if a file is a cloud-only placeholder (OneDrive, iCloud, etc.)
        /// These files would trigger a download if we access their content.
        /// </summary>
        private static bool IsCloudOnlyFile(FileAttributes attrs)
        {
            // Check for cloud placeholder attributes
            if ((attrs & RecallOnDataAccess) != 0 || (attrs & RecallOnOpen) != 0)
            {
                return true;
            }
            // Also check Offline attribute which some cloud providers use
            if ((attrs & FileAttributes.Offline) != 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets file size without triggering OneDrive download.
        /// For cloud-only files, uses FileInfo.Length which reads from the reparse point metadata.
        /// </summary>
        private static long GetFileSizeSafe(FileInfo finfo, ScanResult result)
        {
            try
            {
                // FileInfo.Length reads the size from file metadata/reparse point
                // without triggering a download for cloud files
                return finfo.Length;
            }
            catch (FileNotFoundException ex)
            {
                result.Errors.Add(new ScanError { Path = finfo.FullName, Message = "File not found", ExceptionType = ex.GetType().Name });
                return 0;
            }
            catch (IOException ex)
            {
                result.Errors.Add(new ScanError { Path = finfo.FullName, Message = ex.Message, ExceptionType = ex.GetType().Name });
                return 0;
            }
        }

        private static void LogError(ScanResult result, string path, Exception ex)
        {
            result.Errors.Add(new ScanError 
            { 
                Path = path, 
                Message = ex.Message, 
                ExceptionType = ex.GetType().Name 
            });
        }

        private static async Task<(long localSize, long cloudLogicalSize, int fileCount)> ScanInternal(string cPath, string rootPath, ScanResult result, IProgress<string>? progress, System.Threading.CancellationToken cancellationToken, CloudFileHandling cloudHandling = CloudFileHandling.IncludeLogicalSize)
        {
            var dict = result.Data;
            long curdirLocalFileSize = 0;
            long curdirCloudLogicalSize = 0;
            long childLocalSize = 0;
            long childCloudLogicalSize = 0;
            int curdirFileCount = 0;
            int childFileCount = 0;
            // Check cancellation and report progress
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = cPath.StartsWith(rootPath) ? cPath.Substring(rootPath.Length) : cPath; if (string.IsNullOrEmpty(relativePath)) relativePath = "."; progress?.Report(relativePath);

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(cPath);
                if (!dirInfo.Exists)
                {
                    LogError(result, cPath, new DirectoryNotFoundException($"Directory not found: {cPath}"));
                    return (0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                LogError(result, cPath, ex);
                return (0, 0, 0);
            }

            // Skip reparse points (symlinks, junctions) to avoid infinite loops
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                result.SkippedSymlinks++;
                return (0, 0, 0);
            }

            var nDepth = cPath.Where(c => c == TreeMapConstants.PathSep).Count();
            string[] curDirFiles = Array.Empty<string>();
            try
            {
                curDirFiles = Directory.GetFiles(cPath);
            }
            catch (UnauthorizedAccessException ex) 
            { 
                LogError(result, cPath, ex);
                return (0, 0, 0); 
            }
            catch (DirectoryNotFoundException ex) 
            { 
                LogError(result, cPath, ex);
                return (0, 0, 0);
            }
            catch (IOException ex) 
            { 
                LogError(result, cPath, ex);
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
                        var logicalSize = GetFileSizeSafe(finfo, result);

                        // Track cloud-only files
                        if (IsCloudOnlyFile(finfo.Attributes))
                        {
                            result.CloudFileCount++;
                            result.CloudFileLogicalSize += logicalSize;
                            cloudFileCountInDir++;
                            hasCloudFiles = true;

                            // Store cloud logical size separately for recalculation
                            curdirCloudLogicalSize += logicalSize;
                        }
                        else
                        {
                            // Local file - add actual size
                            curdirLocalFileSize += logicalSize;
                        }
                    }
                    catch (PathTooLongException ex) 
                    { 
                        LogError(result, file, ex);
                    }
                    catch (UnauthorizedAccessException ex) 
                    { 
                        LogError(result, file, ex);
                    }
                    catch (IOException ex) 
                    { 
                        LogError(result, file, ex);
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

            string[] curDirFolders = Array.Empty<string>();
            try
            {
                curDirFolders = Directory.GetDirectories(cPath);
            }
            catch (UnauthorizedAccessException ex) 
            { 
                LogError(result, cPath, ex);
            }
            catch (DirectoryNotFoundException ex) 
            { 
                LogError(result, cPath, ex);
            }
            catch (IOException ex) 
            { 
                LogError(result, cPath, ex);
            }

            if (curDirFolders.Length > 0)
            {
                foreach (var dir in curDirFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // ensure trailing sep
                    var childPath = Path.Combine(cPath, Path.GetFileName(dir));
                    if (!childPath.EndsWith(TreeMapConstants.PathSep.ToString()))
                        childPath += TreeMapConstants.PathSep;
                    var (childLocal, childCloud, childFiles) = await ScanInternal(childPath, rootPath, result, progress, cancellationToken, cloudHandling).ConfigureAwait(false);
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
}
