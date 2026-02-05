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

public static class TreemapPort
{
    // Store current state for context menu operations
    public static ConcurrentDictionary<string, MapDataItem>? CurrentDict { get; private set; }
    public static string? CurrentRootPath { get; private set; }
    public static bool CurrentHorizontal { get; private set; } = true;
    public static string? LastClickedPath { get; set; } // Set by rectangle click

    /// <summary>
    /// Async treemap rendering with progress reporting to a ProgressWindow.
    /// Use this when you want to share a progress window between scanning and rendering.
    /// </summary>
    public static async Task MakeTreemapWithProgressAsync(
        ConcurrentDictionary<string, MapDataItem> dict,
        Canvas canvas,
        string parentPath,
        Rect parentRect,
        long parentTotalSize,
        bool horizontal,
        CancellationTokenSource cts,
        ProgressWindow progressWindow)
    {
        // Store state for context menu operations
        CurrentDict = dict;
        CurrentRootPath = parentPath;
        CurrentHorizontal = horizontal;

        // Collect all visual elements (must be on UI thread since we create Avalonia objects)
        var elements = new List<(Control element, double left, double top)>();
        int totalItems = dict.Count;
        int processedItems = 0;

        progressWindow.Report("Collecting visual elements...");

        // Collection must happen on UI thread (Avalonia objects require it)
        CollectTreemapElements(dict, canvas, parentPath, parentRect, parentTotalSize, horizontal, elements, ref processedItems, totalItems, null);

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

            // Add batch on UI thread
            foreach (var (element, left, top) in batch)
            {
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, top);
                canvas.Children.Add(element);
            }

            // Report progress
            int pct = (int)((i + batchSize) * 100.0 / elements.Count);
            progressWindow.ReportProgress(Math.Min(pct, 100));
            progressWindow.Report($"Rendered {Math.Min(i + batchSize, elements.Count):n0} / {elements.Count:n0}");

            // Yield to allow UI updates
            await Task.Delay(1);
        }

        progressWindow.ReportProgress(100);
    }

    /// <summary>
    /// Async version that creates its own progress window.
    /// Supports cancellation via the progress window's Cancel button.
    /// </summary>
    public static async Task MakeTreemapWithProgressWindowAsync(
        ConcurrentDictionary<string, MapDataItem> dict,
        Canvas canvas,
        string parentPath,
        Rect parentRect,
        long parentTotalSize,
        bool horizontal = true,
        CancellationTokenSource? cts = null)
    {
        // Store state for context menu operations
        CurrentDict = dict;
        CurrentRootPath = parentPath;
        CurrentHorizontal = horizontal;

        // Create CTS if not provided (for cancel button)
        cts ??= new CancellationTokenSource();

        // Show progress window
        using var progressWindow = new ProgressWindow($"Rendering {dict.Count:n0} items...", cts);
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("🎨 Rendering Treemap");

        // Collect all visual elements (must be on UI thread since we create Avalonia objects)
        var elements = new List<(Control element, double left, double top)>();
        int totalItems = dict.Count;
        int processedItems = 0;

        progressWindow.Report("Collecting elements...");

        // Collection must happen on UI thread (Avalonia objects require it)
        CollectTreemapElements(dict, canvas, parentPath, parentRect, parentTotalSize, horizontal, elements, ref processedItems, totalItems, null);

        if (cts.IsCancellationRequested)
        {
            progressWindow.Report("Cancelled");
            return;
        }

        progressWindow.ReportProgress(0);

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

            // Add batch on UI thread
            foreach (var (element, left, top) in batch)
            {
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, top);
                canvas.Children.Add(element);
            }

            // Report progress
            int pct = (int)((i + batchSize) * 100.0 / elements.Count);
            progressWindow.Report(pct, $"Rendered {Math.Min(i + batchSize, elements.Count):n0} / {elements.Count:n0}");

            // Yield to allow progress window and main UI to update
            await Task.Delay(1);
        }

        if (!cts.IsCancellationRequested)
        {
            progressWindow.ReportProgress(100);
            await Task.Delay(100); // Brief pause to show 100%
        }
    }

    // Async version that yields to UI thread periodically for responsiveness
    public static async Task MakeTreemapAsync(
        ConcurrentDictionary<string, MapDataItem> dict, 
        Canvas canvas, 
        string parentPath, 
        Rect parentRect, 
        long parentTotalSize, 
        bool horizontal = true,
        IProgress<int>? progress = null)
    {
        // Store state for context menu operations
        CurrentDict = dict;
        CurrentRootPath = parentPath;
        CurrentHorizontal = horizontal;

        // Collect all visual elements first
        var elements = new List<(Control element, double left, double top)>();
        int totalItems = dict.Count;
        int processedItems = 0;

        CollectTreemapElements(dict, canvas, parentPath, parentRect, parentTotalSize, horizontal, elements, ref processedItems, totalItems, progress);

        // Add elements to canvas in batches, yielding to UI thread between batches
        const int batchSize = 200;  // Smaller batches for smoother progress
        canvas.Children.Clear();

        progress?.Report(0);

        for (int i = 0; i < elements.Count; i += batchSize)
        {
            var batch = elements.Skip(i).Take(batchSize);
            foreach (var (element, left, top) in batch)
            {
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, top);
                canvas.Children.Add(element);
            }

            // Report progress
            int pct = (int)((i + batchSize) * 100.0 / elements.Count);
            progress?.Report(Math.Min(pct, 100));

            // Yield to UI thread at Background priority to allow rendering and progress bar updates
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        progress?.Report(100);
    }

    private static void CollectTreemapElements(
        ConcurrentDictionary<string, MapDataItem> dict,
        Canvas canvas,
        string parentPath,
        Rect parentRect,
        long parentTotalSize,
        bool horizontal,
        List<(Control, double, double)> elements,
        ref int processedItems,
        int totalItems,
        IProgress<int>? progress)
    {
        var parentDepth = dict.ContainsKey(parentPath) ? dict[parentPath].Depth : parentPath.Count(c => c == TreeMapConstants.PathSep);
        var childKeys = dict.Keys.Where(k => k.StartsWith(parentPath) && dict[k].Depth == parentDepth + 1).OrderByDescending(k => dict[k].Size).ToList();

        double x = parentRect.X;
        double y = parentRect.Y;
        double w = parentRect.Width;
        double h = parentRect.Height;
        var total = (double)parentTotalSize;
        double offset = 0;

        for (int i = 0; i < childKeys.Count; i++)
        {
            var key = childKeys[i];
            var size = dict[key].Size;
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

            var isCloudItem = dict.ContainsKey(key) && dict[key].IsCloudOnly;
            var strokeBrush = isCloudItem ? Brushes.Cyan : Brushes.Black;
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
            var cloudInfo = isCloudItem ? $"\n☁ Contains {dict[key].CloudFileCount} cloud file(s)" : "";
            ToolTip.SetTip(rect, $"{key}\n{sizeStr}{cloudInfo}");

            // Capture for closure
            var capturedKey = key;
            var capturedSize = size;
            var capturedHorizontal = horizontal;
            rect.PointerPressed += (s, e) =>
            {
                LastClickedPath = capturedKey;
                if (e.GetCurrentPoint(rect).Properties.IsLeftButtonPressed)
                {
                    canvas.Children.Clear();
                    long childTotal = dict.ContainsKey(capturedKey) ? dict[capturedKey].Size : capturedSize;
                    // Use sync version for drill-down (it's typically fast for a subset)
                    MakeTreemap(dict, canvas, capturedKey, new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height), childTotal, !capturedHorizontal);
                    e.Handled = true;
                }
            };

            elements.Add((rect, r.X, r.Y));
            processedItems++;

            // Add text label
            if (r.Width > 20 && r.Height > 14)
            {
                var txt = new TextBlock
                { 
                    Text = key,
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

            // recurse into children if present and rectangle is large enough
            var hasChildren = dict.Keys.Any(k => k.StartsWith(key) && dict[k].Depth == dict[key].Depth + 1);
            if (hasChildren && (r.Width > 40 && r.Height > 20))
            {
                CollectTreemapElements(dict, canvas, key, r, dict[key].Size, !horizontal, elements, ref processedItems, totalItems, progress);
            }
        }
    }

    // Recursive slice-and-dice treemap. Alternates horizontal/vertical splits.
    public static void MakeTreemap(ConcurrentDictionary<string, MapDataItem> dict, Canvas canvas, string parentPath, Rect parentRect, long parentTotalSize, bool horizontal = true)
    {
        // Store state for context menu operations - only on root call (when canvas is empty)
        if (canvas.Children.Count == 0)
        {
            CurrentDict = dict;
            CurrentRootPath = parentPath;
            CurrentHorizontal = horizontal;
        }

        var parentDepth = dict.ContainsKey(parentPath) ? dict[parentPath].Depth : parentPath.Count(c => c == TreeMapConstants.PathSep);
        var childKeys = dict.Keys.Where(k => k.StartsWith(parentPath) && dict[k].Depth == parentDepth + 1).OrderByDescending(k => dict[k].Size).ToList();
        double x = parentRect.X;
        double y = parentRect.Y;
        double w = parentRect.Width;
        double h = parentRect.Height;

        var total = (double)parentTotalSize;
        double offset = 0;

        for (int i = 0; i < childKeys.Count; i++)
        {
            var key = childKeys[i];
            var size = dict[key].Size;
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
            var fillColor = Avalonia.Media.Color.FromArgb(0xFF, (byte)((i * 97) % 255), (byte)((size / 7) % 255), (byte)(((i + 3) * 59) % 255));
            var fillBrush = new SolidColorBrush(fillColor);
            var rectW = r.Width < 0 ? 0 : r.Width;
            var rectH = r.Height < 0 ? 0 : r.Height;

            // Check if this item contains cloud-only files
            var isCloudItem = dict.ContainsKey(key) && dict[key].IsCloudOnly;
            var strokeBrush = isCloudItem ? Brushes.Cyan : Brushes.Black;
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
            // Tooltip shows full path and size (and cloud status)
            var sizeStr = size >= 1_000_000_000 ? $"{size / 1_000_000_000.0:F2} GB" :
                          size >= 1_000_000 ? $"{size / 1_000_000.0:F2} MB" :
                          size >= 1_000 ? $"{size / 1_000.0:F2} KB" : $"{size} bytes";
            var cloudInfo = isCloudItem ? $"\n☁ Contains {dict[key].CloudFileCount} cloud file(s)" : "";
            ToolTip.SetTip(rect, $"{key}\n{sizeStr}{cloudInfo}");
            rect.PointerPressed += (s, e) =>
            {
                // Track last clicked path for context menu operations
                LastClickedPath = key;

                // Only drill down on LEFT click, not right click (which opens context menu)
                if (e.GetCurrentPoint(rect).Properties.IsLeftButtonPressed)
                {
                    // on click, just redraw treemap for this node (drill down)
                    canvas.Children.Clear();
                    long childTotal = dict.ContainsKey(key) ? dict[key].Size : size;
                    MakeTreemap(dict, canvas, key, new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height), childTotal, !horizontal);
                    e.Handled = true;
                }
            };

            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            canvas.Children.Add(rect);

            // Add text label - show full path like WPF version
            // Use vertical text for tall narrow rectangles
            if (r.Width > 20 && r.Height > 14)
            {
                var txt = new TextBlock
                { 
                    Text = key, // Full path
                    Foreground = Brushes.Black,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                };
                txt.DataContext = key;
                
                // Rotate text 90 degrees for tall narrow rectangles (height > width)
                if (r.Height > r.Width * 1.5 && r.Height > 60)
                {
                    txt.RenderTransform = new RotateTransform(90);
                    txt.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    txt.MaxWidth = r.Height - 8; // Use height as max width since rotated
                    Canvas.SetLeft(txt, r.X + 14);
                    Canvas.SetTop(txt, r.Y + 4);
                }
                else
                {
                    txt.MaxWidth = r.Width - 8;
                    Canvas.SetLeft(txt, r.X + 4);
                    Canvas.SetTop(txt, r.Y + 4);
                }
                canvas.Children.Add(txt);
            }

            // recurse into children if present and rectangle is large enough
            var hasChildren = dict.Keys.Any(k => k.StartsWith(key) && dict[k].Depth == dict[key].Depth + 1);
            if (hasChildren && (r.Width > 40 && r.Height > 20))
            {
                MakeTreemap(dict, canvas, key, r, dict[key].Size, !horizontal);
            }
        }
    }
}
