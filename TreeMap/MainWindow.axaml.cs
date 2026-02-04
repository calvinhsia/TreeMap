using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

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
        var pathCombo = this.FindControl<ComboBox>("PathCombo");
        var scan = this.FindControl<Button>("ScanBtn");
        var browseBtn = this.FindControl<Button>("BrowseBtn");
        var toggleViewBtn = this.FindControl<Button>("ToggleViewBtn");
        var cloudHandlingCombo = this.FindControl<ComboBox>("CloudHandlingCombo");
        var left = this.FindControl<ContentControl>("LeftHost");
        var treeCanvas = this.FindControl<Canvas>("TreeCanvas");
        var treeCanvasHost = this.FindControl<Avalonia.Controls.Border>("TreeCanvasHost");
        var toggleBrowseMenuItem = this.FindControl<MenuItem>("ToggleBrowseMenuItem");

        // Load user settings (MRU paths, cloud handling preference)
        var settings = UserSettings.Load();

        // Populate path combo with MRU paths
        foreach (var mruPath in settings.MruPaths)
        {
            pathCombo.Items.Add(mruPath);
        }

        // Set cloud handling from saved setting
        if (cloudHandlingCombo != null && settings.CloudHandlingIndex >= 0 && settings.CloudHandlingIndex < 3)
        {
            cloudHandlingCombo.SelectedIndex = settings.CloudHandlingIndex;
        }

        // Helper to get current path from editable combo
        Func<string> getPath = () => 
        {
            // For editable ComboBox, the text is in the SelectedItem or we need to get it differently
            var text = pathCombo?.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(text))
            {
                // Try to get from the text input part of editable combo
                var textBox = pathCombo?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                text = textBox?.Text;
            }
            return text ?? System.IO.Directory.GetCurrentDirectory();
        };

        // Helper to set path in combo
        System.Action<string> setPath = (path) =>
        {
            var textBox = pathCombo?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (textBox != null)
            {
                textBox.Text = path;
            }
        };

        // Helper to add path to MRU and refresh combo
        System.Action<string> addToMru = (path) =>
        {
            settings.AddMruPath(path);
            settings.CloudHandlingIndex = cloudHandlingCombo?.SelectedIndex ?? 0;
            settings.Save();

            // Refresh combo items
            pathCombo.Items.Clear();
            foreach (var mruPath in settings.MruPaths)
            {
                pathCombo.Items.Add(mruPath);
            }
        };

        // BrowseControl created lazily when user requests it via button or context menu
        BrowseControl? browse = null;
        // Store the scan results for creating browse control later
        ScanResult? lastScanResult = null;

        // Helper to get selected cloud handling mode from combo box
        Func<CloudFileHandling> getCloudHandling = () =>
        {
            return cloudHandlingCombo?.SelectedIndex switch
            {
                1 => CloudFileHandling.ExcludeFromSize,
                2 => CloudFileHandling.IncludePlaceholderSize,
                _ => CloudFileHandling.IncludeLogicalSize
            };
        };

        // Helper to toggle between treemap and file list views
        System.Action toggleView = () =>
        {
            bool showFileList = !left.IsVisible;

            // Create BrowseControl on first show, using last scan results
            if (showFileList && browse == null && lastScanResult != null)
            {
                var items = new System.Collections.Generic.List<object>();
                foreach (var kv in lastScanResult.Data)
                    items.Add(new { 
                        Path = kv.Key, 
                        Size = kv.Value.Size, 
                        CloudFiles = kv.Value.CloudFileCount > 0 ? kv.Value.CloudFileCount.ToString() : ""
                    });
                browse = new BrowseControl(items, new[] { 500, 100, 70 }, true);
                left.Content = browse;
            }

            left.IsVisible = showFileList;
            treeCanvasHost.IsVisible = !showFileList;

            // Update button and menu text
            var newText = showFileList ? "Show Treemap" : "Show File List";
            if (toggleViewBtn != null) toggleViewBtn.Content = newText;
            if (toggleBrowseMenuItem != null) toggleBrowseMenuItem.Header = newText;
        };

        // Wire up toggle button
        if (toggleViewBtn != null)
        {
            toggleViewBtn.Click += (s, args) => toggleView();
        }

        // Wire up context menu item
        if (toggleBrowseMenuItem != null)
        {
            toggleBrowseMenuItem.Click += (s, args) => toggleView();
        }

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
                    var initialPath = settings.MruPaths.Count > 0 
                        ? settings.MruPaths[0] 
                        : System.IO.Directory.GetCurrentDirectory();
                    setPath(initialPath);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan?.Invoke(initialPath), Avalonia.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
        };

        // Helper to run a scan asynchronously and draw the treemap for a given path
        runScan = (path) =>
        {
            // Add to MRU before scanning
            addToMru(path);

            // Progress reporter will forward incremental directory updates to the UI
            var progress = new Progress<string>(s =>
            {
                try { this.FindControl<TextBlock>("StatusText").Text = s; } catch { }
            });

            // Cancellation token support (not currently exposed in UI)
            var cts = new System.Threading.CancellationTokenSource();

            // Get the selected cloud handling mode
            var cloudHandling = getCloudHandling();

            // Run the async scan off the UI thread - use new API that reports errors
            _ = DiskScanner.ScanWithErrorsAsync(path, progress, cts.Token, cloudHandling).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    return;

                var scanResult = t.Result;
                var dict = scanResult.Data;
                lastScanResult = scanResult; // Store full result for later browse creation
                browse = null; // Reset so it gets recreated with new data

                // marshal UI updates back to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // Update status with scan summary including any errors
                        var statusText = this.FindControl<TextBlock>("StatusText");
                        var summary = $"Items: {dict.Count:n0}";
                        if (scanResult.SkippedSymlinks > 0)
                            summary += $", Symlinks: {scanResult.SkippedSymlinks}";
                        if (scanResult.CloudFileCount > 0)
                        {
                            var cloudSizeStr = scanResult.CloudFileLogicalSize >= 1_000_000_000 
                                ? $"{scanResult.CloudFileLogicalSize / 1_000_000_000.0:F1}GB" 
                                : scanResult.CloudFileLogicalSize >= 1_000_000 
                                    ? $"{scanResult.CloudFileLogicalSize / 1_000_000.0:F1}MB" 
                                    : $"{scanResult.CloudFileLogicalSize / 1_000.0:F1}KB";
                            summary += $", Cloud: {scanResult.CloudFileCount} ({cloudSizeStr})";
                        }
                        if (scanResult.HasErrors)
                            summary += $", Errors: {scanResult.Errors.Count}";
                        statusText.Text = summary;

                        // Log errors to debug output
                        if (scanResult.HasErrors)
                        {
                            System.Diagnostics.Debug.WriteLine($"Scan completed with {scanResult.Errors.Count} errors:");
                            foreach (var err in scanResult.Errors.Take(10)) // Show first 10
                            {
                                System.Diagnostics.Debug.WriteLine($"  [{err.ExceptionType}] {err.Path}: {err.Message}");
                            }
                            if (scanResult.Errors.Count > 10)
                                System.Diagnostics.Debug.WriteLine($"  ... and {scanResult.Errors.Count - 10} more errors");
                        }

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
            var path = getPath();
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
                        setPath(result);
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
            var initialPath = settings.MruPaths.Count > 0 
                ? settings.MruPaths[0] 
                : System.IO.Directory.GetCurrentDirectory();
            setPath(initialPath);
            // schedule scan after layout so canvas has real bounds
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                runScan?.Invoke(initialPath);
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Also schedule an initial scan immediately after OnOpened to ensure we run
        // when AttachedToVisualTree did not fire early enough.
        var initPathNow = settings.MruPaths.Count > 0 
            ? settings.MruPaths[0] 
            : System.IO.Directory.GetCurrentDirectory();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan?.Invoke(initPathNow), Avalonia.Threading.DispatcherPriority.Background);
    }
}
