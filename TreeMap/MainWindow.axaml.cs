using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TreeMap;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

        protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        var pathBox = this.FindControl<TextBox>("PathBox");
        var scan = this.FindControl<Button>("ScanBtn");
        var browseBtn = this.FindControl<Button>("BrowseBtn");
        var left = this.FindControl<ContentControl>("LeftHost");
        var treeCanvas = this.FindControl<Canvas>("TreeCanvas");
        var treeCanvasHost = this.FindControl<Avalonia.Controls.Border>("TreeCanvasHost");

        // Create a sample BrowseControl on the left (empty initially)
        var browse = new BrowseControl(System.Array.Empty<object>());
        left.Content = browse;
        try
        {
            if (browse.ListView != null)
            {
                browse.ListView.SelectionChanged += sel => { };
            }
        }
        catch { }

        // Use the Border (TreeCanvasHost) to detect valid bounds since Canvas doesn't stretch
        bool initialScanDone = false;
        System.Action<string>? runScan = null;
        treeCanvasHost.LayoutUpdated += (s, ev) =>
        {
            try
            {
                if (!initialScanDone && treeCanvasHost.Bounds.Width > 0 && treeCanvasHost.Bounds.Height > 0)
                {
                    initialScanDone = true;
                    var initialPath = System.IO.Directory.GetCurrentDirectory();
                    pathBox.Text = initialPath;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan?.Invoke(initialPath), Avalonia.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
        };

        // Helper to run a scan asynchronously and draw the treemap for a given path
        runScan = (path) =>
        {
            // Progress reporter will forward incremental directory updates to the UI
            var progress = new Progress<string>(s =>
            {
                try { this.FindControl<TextBlock>("StatusText").Text = s; } catch { }
            });

            // Cancellation token support (not currently exposed in UI)
            var cts = new System.Threading.CancellationTokenSource();

            // Run the async scan off the UI thread
            _ = DiskScanner.ScanAsync(path, progress, cts.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    return;

                var dict = t.Result;

                // marshal UI updates back to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // populate browse list with simple items
                        var items = new System.Collections.Generic.List<object>();
                        foreach (var kv in dict)
                            items.Add(new { Path = kv.Key, Size = kv.Value.Size });
                        browse = new BrowseControl(items, null, true);
                        left.Content = browse;

                        // port basic treemap layout: place rectangles proportional to size
                        treeCanvas.Children.Clear();
                        if (dict.Count == 0)
                            return;

                        string rootKey = null;
                        foreach (var k in dict.Keys)
                        {
                            if (k.EndsWith(TreeMapConstants.PathSep.ToString()))
                            {
                                if (rootKey == null || k.Length < rootKey.Length) rootKey = k;
                            }
                        }
                        rootKey ??= path + TreeMapConstants.PathSep;
                        long total = dict.ContainsKey(rootKey) ? dict[rootKey].Size : 0;
                        if (total == 0)
                        {
                            foreach (var v in dict.Values) total += v.Size;
                        }

                        // Use the Border's bounds since Canvas doesn't stretch
                        var availW = treeCanvasHost.Bounds.Width;
                        var availH = treeCanvasHost.Bounds.Height;
                        // If host has no bounds yet, fallback to window size estimate
                        if (availW <= 0) availW = this.Bounds.Width - 360;
                        if (availH <= 0) availH = this.Bounds.Height - 120;
                        var rect = new Rect(0, 0, availW > 0 ? availW : 800, availH > 0 ? availH : 600);
                        // Set canvas size to match the available area
                        treeCanvas.Width = rect.Width;
                        treeCanvas.Height = rect.Height;
                        TreemapPort.MakeTreemap(dict, treeCanvas, rootKey, rect, total);
                    }
                    catch { }
                }, Avalonia.Threading.DispatcherPriority.Background);
            });
        };

        // Wire scan button to run the common runScan helper
        scan.Click += (_, __) =>
        {
            var path = pathBox.Text ?? System.IO.Directory.GetCurrentDirectory();
            runScan(path);
        };

        // Folder picker using Avalonia's OpenFolderDialog
        if (browseBtn != null)
        {
            browseBtn.Click += async (_, __) =>
            {
                try
                {
                    var dlg = new OpenFolderDialog();
                    var result = await dlg.ShowAsync(this);
                    if (!string.IsNullOrEmpty(result))
                    {
                        pathBox.Text = result;
                        // run an immediate scan when a folder is chosen
                        runScan(result);
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Folder picker failed: " + ex);
                }
            };
        }

        // Trigger an automatic initial scan of current directory after layout settles
        this.AttachedToVisualTree += (s, e) =>
        {
            var initialPath = System.IO.Directory.GetCurrentDirectory();
            pathBox.Text = initialPath;
            // schedule scan after layout so canvas has real bounds
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                runScan?.Invoke(initialPath);
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Also schedule an initial scan immediately after OnOpened to ensure we run
        // when AttachedToVisualTree did not fire early enough.
        var initPathNow = System.IO.Directory.GetCurrentDirectory();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan?.Invoke(initPathNow), Avalonia.Threading.DispatcherPriority.Background);
    }
}
