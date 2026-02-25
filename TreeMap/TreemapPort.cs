using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TreeMap;

/// <summary>
/// Pre-computed parent→children lookup for O(1) child access instead of O(n) scans.
/// Build once, use many times during treemap rendering.
/// </summary>
internal class TreemapLookup
{
    private readonly Dictionary<string, List<string>> _childrenByParent;
    private readonly ConcurrentDictionary<string, MapDataItem> _dict;

    public TreemapLookup(ConcurrentDictionary<string, MapDataItem> dict)
    {
        _dict = dict;
        _childrenByParent = new Dictionary<string, List<string>>();

        // Build parent→children lookup in a single pass: O(n)
        foreach (var key in dict.Keys)
        {
            var depth = dict[key].Depth;
            // Find parent by looking for the path one level up
            var parentPath = GetParentPath(key, depth);
            if (parentPath != null)
            {
                if (!_childrenByParent.TryGetValue(parentPath, out var children))
                {
                    children = new List<string>();
                    _childrenByParent[parentPath] = children;
                }
                children.Add(key);
            }
        }

        // Sort children by size descending (do this once, not on every access)
        foreach (var kvp in _childrenByParent)
        {
            kvp.Value.Sort((a, b) => _dict[b].Size.CompareTo(_dict[a].Size));
        }
    }

    private static string? GetParentPath(string path, int depth)
    {
        // For a path like "C:\foo\bar\" at depth 3, parent is "C:\foo\" at depth 2
        // For data suffix paths like "C:\foo\*", parent is "C:\foo\"
        if (path.EndsWith("*"))
        {
            // Data suffix - parent is the directory
            return path.Substring(0, path.Length - 1);
        }

        // Find the second-to-last separator
        var trimmed = path.TrimEnd(TreeMapConstants.PathSep);
        var lastSep = trimmed.LastIndexOf(TreeMapConstants.PathSep);
        if (lastSep > 0)
        {
            return trimmed.Substring(0, lastSep + 1);
        }
        return null;
    }

    /// <summary>
    /// Get children of a path, already sorted by size descending. O(1) lookup.
    /// </summary>
    public List<string> GetChildren(string parentPath)
    {
        return _childrenByParent.TryGetValue(parentPath, out var children) ? children : new List<string>();
    }

    /// <summary>
    /// Check if a path has children. O(1) lookup.
    /// </summary>
    public bool HasChildren(string path)
    {
        return _childrenByParent.ContainsKey(path);
    }
}

public static class TreemapPort
{
    // Store current state for context menu operations
    public static ConcurrentDictionary<string, MapDataItem>? CurrentDict { get; private set; }
    public static string? CurrentRootPath { get; private set; }
    public static bool CurrentHorizontal { get; private set; } = true;
    public static string? LastClickedPath { get; set; } // Set by rectangle click

    /// <summary>
    /// Async treemap rendering with progress reporting.
    /// </summary>
    /// <param name="dict">The scanned data dictionary</param>
    /// <param name="canvas">Canvas to render to</param>
    /// <param name="parentPath">Root path to render from</param>
    /// <param name="parentRect">Rectangle bounds for rendering</param>
    /// <param name="parentTotalSize">Total size of parent</param>
    /// <param name="horizontal">Start with horizontal split</param>
    /// <param name="cts">Cancellation token source (optional, created if null)</param>
    /// <param name="progressWindow">Progress window to use (optional, created if null)</param>
    public static async Task MakeTreemapAsync(
        ConcurrentDictionary<string, MapDataItem> dict,
        Canvas canvas,
        string parentPath,
        Rect parentRect,
        long parentTotalSize,
        bool horizontal = true,
        CancellationTokenSource? cts = null,
        ProgressWindow? progressWindow = null)
    {
        // Store state for context menu operations
        CurrentDict = dict;
        CurrentRootPath = parentPath;
        CurrentHorizontal = horizontal;

        // Create CTS if not provided
        cts ??= new CancellationTokenSource();

        // Create progress window if not provided
        bool ownsProgressWindow = progressWindow == null;
        if (ownsProgressWindow)
        {
            progressWindow = new ProgressWindow($"Rendering {dict.Count:n0} items...", cts);
            await progressWindow.ShowAsync();
            progressWindow.SetPhase("🎨 Rendering Treemap");
        }

        try
        {
            // Build lookup once - O(n) instead of O(n²) repeated scans
            progressWindow!.Report("Building index...");
            var lookup = new TreemapLookup(dict);

            // Collect all visual elements
            var elements = new List<(Control element, double left, double top)>();
            int totalItems = dict.Count;

            progressWindow.Report("Collecting visual elements...");

            await CollectTreemapElementsAsync(dict, lookup, canvas, parentPath, parentRect, parentTotalSize, horizontal, elements,
                0, totalItems, progressWindow, cts);

            if (cts.IsCancellationRequested)
            {
                progressWindow.Report("Cancelled");
                return;
            }

            progressWindow.ReportProgress(0);
            progressWindow.Report($"Adding {elements.Count:n0} elements to canvas...");

            // Add elements to canvas in batches
            const int batchSize = 500;
            canvas.Children.Clear();

            for (int i = 0; i < elements.Count; i += batchSize)
            {
                if (cts.IsCancellationRequested)
                {
                    progressWindow.Report("Cancelled");
                    break;
                }

                var batch = elements.Skip(i).Take(batchSize).ToList();

                foreach (var (element, left, top) in batch)
                {
                    Canvas.SetLeft(element, left);
                    Canvas.SetTop(element, top);
                    canvas.Children.Add(element);
                }

                int pct = (int)((i + batchSize) * 100.0 / elements.Count);
                progressWindow.Report(Math.Min(pct, 100), $"Rendered {Math.Min(i + batchSize, elements.Count):n0} / {elements.Count:n0}");

                await Task.Delay(1);
            }

            if (!cts.IsCancellationRequested)
            {
                progressWindow.ReportProgress(100);
                if (ownsProgressWindow)
                    await Task.Delay(100); // Brief pause to show 100%
            }
        }
        finally
        {
            if (ownsProgressWindow)
                progressWindow?.Dispose();
        }
    }

    /// <summary>
    /// Async version of CollectTreemapElements that yields periodically for UI updates and reports progress.
    /// Uses pre-built lookup for O(1) child access instead of O(n) scans.
    /// </summary>
    private static async Task CollectTreemapElementsAsync(
        ConcurrentDictionary<string, MapDataItem> dict,
        TreemapLookup lookup,
        Canvas canvas,
        string parentPath,
        Rect parentRect,
        long parentTotalSize,
        bool horizontal,
        List<(Control, double, double)> elements,
        int processedItems,
        int totalItems,
        ProgressWindow progressWindow,
        CancellationTokenSource cts,
        string? rootPath = null)
    {
        // Track root path for relative display
        rootPath ??= parentPath;
        var rootPathLength = rootPath.Length - 1;

        if (cts.IsCancellationRequested) return;

        // O(1) lookup instead of O(n) LINQ query!
        var childKeys = lookup.GetChildren(parentPath);

        double x = parentRect.X;
        double y = parentRect.Y;
        double w = parentRect.Width;
        double h = parentRect.Height;
        var total = (double)parentTotalSize;
        double offset = 0;
        int itemsInThisBatch = 0;

        for (int i = 0; i < childKeys.Count; i++)
        {
            if (cts.IsCancellationRequested) return;

            var key = childKeys[i];
            var keyValue = dict[key];
            var size = keyValue.Size;
            double fraction = total == 0 ? 0 : (double)size / total;

            Rect r;
            if (horizontal)
            {
                var rw = w * fraction;
                r = new Rect(x + offset, y, rw, h);
                offset += rw;
            }
            else
            {
                var rh = h * fraction;
                r = new Rect(x, y + offset, w, rh);
                offset += rh;
            }

            // create rectangle shape
            var fillColor = Color.FromArgb(0xFF, (byte)((i * 97) % 255), (byte)((size / 7) % 255), (byte)(((i + 3) * 59) % 255));
            var fillBrush = new SolidColorBrush(fillColor);
            var rectW = r.Width < 0 ? 0 : r.Width;
            var rectH = r.Height < 0 ? 0 : r.Height;

            var isCloudItem = dict.ContainsKey(key) && keyValue.IsCloudOnly;
            var strokeBrush = isCloudItem ? Brushes.Red : Brushes.Black;
            var strokeThickness = isCloudItem ? 3.0 : 1.0;

            var rect = new Avalonia.Controls.Shapes.Rectangle
            {
                Fill = fillBrush,
                Width = rectW,
                Height = rectH,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness
            };
            rect.DataContext = key;

            var sizeStr = size >= 1_000_000_000 ? $"{size / 1_000_000_000.0:F2} GB" :
                          size >= 1_000_000 ? $"{size / 1_000_000.0:F2} MB" :
                          size >= 1_000 ? $"{size / 1_000.0:F2} KB" : $"{size} bytes";
            var cloudInfo = isCloudItem ? $"\n☁ Contains {keyValue.CloudFileCount} cloud file(s)" : "";
            ToolTip.SetTip(rect, $"{key}\n{sizeStr}{cloudInfo} #Files= {keyValue.NumFiles}");

            // Capture for closure
            var capturedKey = key;
            var capturedSize = size;
            var capturedHorizontal = horizontal;
            // Use async handler so drill-down uses the async renderer and keeps UI responsive
            rect.PointerPressed += async (s, e) =>
            {
                LastClickedPath = capturedKey;
                if (e.GetCurrentPoint(rect).Properties.IsLeftButtonPressed)
                {
                    canvas.Children.Clear();
                    long childTotal = dict.ContainsKey(capturedKey) ? dict[capturedKey].Size : capturedSize;
                    await MakeTreemapAsync(dict, canvas, capturedKey, new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height), childTotal, !capturedHorizontal);
                    e.Handled = true;
                }
            };

            elements.Add((rect, r.X, r.Y));
            processedItems++;
            itemsInThisBatch++;

            // Add text label
            if (r.Width > 20 && r.Height > 14)
            {
                var txt = new TextBlock
                {
                    Text = key.Substring(rootPathLength),
                    Foreground = Brushes.Black,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                txt.DataContext = key;

                double txtLeft, txtTop;
                if (r.Height > r.Width * 1.5 && r.Height > 60)
                {
                    txt.RenderTransform = new RotateTransform(90);
                    txt.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    txt.MaxWidth = r.Height - 8;
                    txtLeft = r.X + 14;
                    txtTop = r.Y + 4;
                }
                else
                {
                    txt.MaxWidth = r.Width - 8;
                    txtLeft = r.X + 4;
                    txtTop = r.Y + 4;
                }
                elements.Add((txt, txtLeft, txtTop));
            }

            // Report progress periodically (every 50 items)
            if (itemsInThisBatch >= 50)
            {
                int pct = totalItems > 0 ? (int)(elements.Count * 100.0 / (totalItems * 2)) : 0;
                var relativePath = key.StartsWith(rootPath) ? key.Substring(rootPath.Length) : key;
                if (string.IsNullOrEmpty(relativePath)) relativePath = key;
                progressWindow.Report(Math.Min(pct, 50), $"Collecting: {relativePath}");
                itemsInThisBatch = 0;
                await Task.Delay(1);
            }

            // O(1) check instead of O(n) Any() query!
            if (lookup.HasChildren(key) && (r.Width > 40 && r.Height > 20))
            {
                await CollectTreemapElementsAsync(dict, lookup, canvas, key, r, keyValue.Size, !horizontal, elements,
                    processedItems, totalItems, progressWindow, cts, rootPath);
            }
        }
    }

    // Synchronous MakeTreemap removed in favor of the async renderer MakeTreemapAsync.
    // Drill-down handlers now call MakeTreemapAsync to redraw using the async path with progress.
}
