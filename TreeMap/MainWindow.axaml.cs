using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        // Select the first item if available
        if (pathCombo.Items.Count > 0)
        {
            pathCombo.SelectedIndex = 0;
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

        // Helper to add path to MRU and refresh combo (without triggering SelectionChanged)
        bool isRefreshingCombo = false;
        System.Action<string> addToMru = (path) =>
        {
            // Prevent reentrant calls during combo refresh
            if (isRefreshingCombo)
                return;

            settings.AddMruPath(path);
            settings.CloudHandlingIndex = cloudHandlingCombo?.SelectedIndex ?? 0;
            settings.Save();

            // Refresh combo items without triggering another scan
            isRefreshingCombo = true;
            pathCombo.SelectedIndex = -1;  // Reset selection before clearing to avoid index issues
            pathCombo.Items.Clear();
            foreach (var mruPath in settings.MruPaths)
            {
                pathCombo.Items.Add(mruPath);
            }
            // Select the first item (the one we just added/moved to front)
            if (pathCombo.Items.Count > 0)
            {
                pathCombo.SelectedIndex = 0;
            }
            // Also update the text box directly to ensure it shows the path
            setPath(path);
            isRefreshingCombo = false;
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

        // Helper to redraw the treemap using existing scan data (without rescanning)
        System.Action? redrawTreemap = null;

        // Wire up cloud handling combo - recalculate sizes without rescanning
        if (cloudHandlingCombo != null)
        {
            cloudHandlingCombo.SelectionChanged += (s, args) =>
            {
                if (lastScanResult != null && lastScanResult.Data.Count > 0)
                {
                    // Recalculate sizes based on new cloud handling option (no rescan needed!)
                    var cloudHandling = getCloudHandling();
                    DiskScanner.RecalculateSizes(lastScanResult, cloudHandling);

                    // Save the preference
                    settings.CloudHandlingIndex = cloudHandlingCombo.SelectedIndex;
                    settings.Save();

                    // Invalidate cached browse control since sizes changed
                    browse = null;

                    // Redraw the treemap with updated sizes
                    redrawTreemap?.Invoke();
                }
            };
        }

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
                        Files = kv.Value.NumFiles > 0 ? kv.Value.NumFiles.ToString() : "",
                        CloudFiles = kv.Value.CloudFileCount > 0 ? kv.Value.CloudFileCount.ToString() : ""
                    });
                browse = new BrowseControl(items, new[] { 500, 100, 60, 70 }, true);
                left.Content = browse;
            }

            left.IsVisible = showFileList;
            treeCanvasHost.IsVisible = !showFileList;

            // Update button and menu text
            var newText = showFileList ? "Show Treemap" : "Show File List";
            if (toggleViewBtn != null) toggleViewBtn.Content = newText;
            if (toggleBrowseMenuItem != null) toggleBrowseMenuItem.Header = newText;
            // Update the file list context menu too
            var toggleTreemapMenuItem = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
            if (toggleTreemapMenuItem != null) toggleTreemapMenuItem.Header = newText;
        };

        // Wire up toggle button
        if (toggleViewBtn != null)
        {
            toggleViewBtn.Click += (s, args) => toggleView();
        }

        // Wire up context menu items (both treemap and file list)
        if (toggleBrowseMenuItem != null)
        {
            toggleBrowseMenuItem.Click += (s, args) => toggleView();
        }
        var toggleTreemapMenuItemInit = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
        if (toggleTreemapMenuItemInit != null)
        {
            toggleTreemapMenuItemInit.Click += (s, args) => toggleView();
        }

        // Wire up swap orientation menu item
        var swapOrientationMenuItem = this.FindControl<MenuItem>("SwapOrientationMenuItem");
        if (swapOrientationMenuItem != null)
        {
            swapOrientationMenuItem.Click += async (s, args) =>
            {
                if (TreemapPort.CurrentDict != null && TreemapPort.CurrentRootPath != null)
                {
                    treeCanvas.Children.Clear();
                    var rootKey = TreemapPort.CurrentRootPath;
                    long total = TreemapPort.CurrentDict.ContainsKey(rootKey) ? TreemapPort.CurrentDict[rootKey].Size : 0;
                    // If total is still 0, sum up child sizes (same fallback as main scan)
                    if (total == 0)
                    {
                        foreach (var v in TreemapPort.CurrentDict.Values)
                            total += v.Size;
                    }
                    var rect = new Rect(0, 0, treeCanvas.Width, treeCanvas.Height);
                    // Swap orientation with progress window
                    await TreemapPort.MakeTreemapAsync(TreemapPort.CurrentDict, treeCanvas, rootKey, rect, total, !TreemapPort.CurrentHorizontal);
                }
            };
        }

        // Wire up open explorer menu item
        var openExplorerMenuItem = this.FindControl<MenuItem>("OpenExplorerMenuItem");
        if (openExplorerMenuItem != null)
        {
            openExplorerMenuItem.Click += (s, args) =>
            {
                var pathToOpen = TreemapPort.LastClickedPath ?? TreemapPort.CurrentRootPath;
                if (!string.IsNullOrEmpty(pathToOpen))
                {
                    try
                    {
                        // Remove trailing separator and data suffix for explorer
                        var cleanPath = pathToOpen.TrimEnd(TreeMapConstants.PathSep);
                        if (cleanPath.EndsWith("*"))
                            cleanPath = cleanPath.TrimEnd('*').TrimEnd(TreeMapConstants.PathSep);

                        // Open folder in explorer
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = cleanPath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to open explorer: {ex.Message}");
                    }
                }
            };
        }

        // Wire up new window menu item
        var newWindowMenuItem = this.FindControl<MenuItem>("NewWindowMenuItem");
        if (newWindowMenuItem != null)
        {
            newWindowMenuItem.Click += (s, args) =>
            {
                var pathForNewWindow = TreemapPort.LastClickedPath ?? TreemapPort.CurrentRootPath;
                if (!string.IsNullOrEmpty(pathForNewWindow))
                {
                    // Clean the path
                    var cleanPath = pathForNewWindow.TrimEnd(TreeMapConstants.PathSep);
                    if (cleanPath.EndsWith("*"))
                        cleanPath = cleanPath.TrimEnd('*').TrimEnd(TreeMapConstants.PathSep);

                    // Create and show a new window
                    var newWindow = new MainWindow();
                    newWindow.Show();

                    // The new window will auto-scan its initial path, but we want it to scan cleanPath
                    // We can set this via a property or by posting to the dispatcher after it opens
                    newWindow.Opened += (ws, we) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            var newPathCombo = newWindow.FindControl<ComboBox>("PathCombo");
                            var newTextBox = newPathCombo?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                            if (newTextBox != null)
                            {
                                newTextBox.Text = cleanPath;
                            }
                            // Trigger scan by clicking the scan button
                            var newScanBtn = newWindow.FindControl<Button>("ScanBtn");
                            newScanBtn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    };
                }
            };
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

            // Switch to treemap view if currently showing file list (file list becomes stale with new scan)
            if (left.IsVisible)
            {
                left.IsVisible = false;
                treeCanvasHost.IsVisible = true;
                var newText = "Show File List";
                if (toggleViewBtn != null) toggleViewBtn.Content = newText;
                if (toggleBrowseMenuItem != null) toggleBrowseMenuItem.Header = newText;
                var toggleTreemapMenuItem = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
                if (toggleTreemapMenuItem != null) toggleTreemapMenuItem.Header = newText;
            }

            // Create unified progress window for both scanning and rendering
            var cts = new System.Threading.CancellationTokenSource();

            // Get the selected cloud handling mode
            var cloudHandling = getCloudHandling();

            // Run the combined scan + render operation
            _ = RunScanAndRenderAsync(path, cloudHandling, cts, treeCanvas, treeCanvasHost, this,
                result =>
                {
                    lastScanResult = result;
                    browse = null;
                });
        };

        // Define redrawTreemap - redraws treemap without rescanning (for cloud handling changes)
        redrawTreemap = () =>
        {
            if (lastScanResult == null || lastScanResult.Data.Count == 0)
                return;

            var dict = lastScanResult.Data;
            treeCanvas.Children.Clear();

            // Find root key
            string? rootKey = null;
            foreach (var k in dict.Keys)
            {
                if (k.EndsWith(TreeMapConstants.PathSep.ToString()))
                {
                    if (rootKey == null || k.Length < rootKey.Length) rootKey = k;
                }
            }
            if (rootKey == null)
                return;

            long total = dict.ContainsKey(rootKey) ? dict[rootKey].Size : 0;
            if (total == 0)
            {
                foreach (var v in dict.Values) total += v.Size;
            }

            var availW = treeCanvasHost.Bounds.Width;
            var availH = treeCanvasHost.Bounds.Height;
            if (availW <= 0) availW = this.Bounds.Width - 360;
            if (availH <= 0) availH = this.Bounds.Height - 120;
            var rect = new Rect(0, 0, availW > 0 ? availW : 800, availH > 0 ? availH : 600);
            treeCanvas.Width = rect.Width;
            treeCanvas.Height = rect.Height;

            // Redraw synchronously (data is already in memory)
            TreemapPort.MakeTreemap(dict, treeCanvas, rootKey, rect, total, TreemapPort.CurrentHorizontal);

            // Update status text with summary
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null)
            {
                var cloudHandling = getCloudHandling();
                var modeStr = cloudHandling switch
                {
                    CloudFileHandling.ExcludeFromSize => "Cloud: Excluded",
                    CloudFileHandling.IncludePlaceholderSize => "Cloud: Placeholder",
                    _ => "Cloud: Logical Size"
                };
                statusText.Text = $"Items: {dict.Count:n0}, {modeStr}";
            }
        };

        // Wire scan button to run the common runScan helper
        scan.Click += (_, __) =>
        {
            var path = getPath();
            runScan(path);
        };

        // Wire up path combo selection changed - scan when user selects from MRU dropdown
        pathCombo.SelectionChanged += (s, args) =>
        {
            // Skip if we're just refreshing the combo programmatically
            if (isRefreshingCombo)
                return;

            if (args.AddedItems.Count > 0 && args.AddedItems[0] is string selectedPath)
            {
                // Only scan if selection actually changed (not just programmatic refresh)
                if (!string.IsNullOrEmpty(selectedPath) && runScan != null)
                {
                    runScan(selectedPath);
                }
            }
        };

        // Allow user to press Enter in the combo box to scan (for manually typed paths)
        pathCombo.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                var path = getPath();
                if (!string.IsNullOrEmpty(path) && runScan != null)
                {
                    runScan(path);
                    e.Handled = true;
                }
            }
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

    /// <summary>
    /// Runs disk scan and treemap rendering with a unified progress window.
    /// Phase 1: Scanning directories (progress shows scan status)
    /// Phase 2: Rendering treemap (progress shows render percentage)
    /// </summary>
    private async Task RunScanAndRenderAsync(
        string path,
        CloudFileHandling cloudHandling,
        System.Threading.CancellationTokenSource cts,
        Canvas treeCanvas,
        Border treeCanvasHost,
        Window parentWindow,
        Action<ScanResult> onScanComplete)
    {
        using var progressWindow = new ProgressWindow($"TreeMap - Scanning: {path}", cts);
        await progressWindow.ShowAsync();

        var statusText = this.FindControl<TextBlock>("StatusText");
        string summary = "";

        try
        {
            // Phase 1: Disk Scanning
            progressWindow.SetPhase($"📁 Scanning: {path}");

            var scanResult = await DiskScanner.ScanWithErrorsAsync(path, progressWindow, cts.Token, cloudHandling);

            if (cts.IsCancellationRequested)
            {
                statusText.Text = "Scan cancelled";
                return;
            }

            var dict = scanResult.Data;
            onScanComplete(scanResult);

            // Build summary
            summary = $"Items: {dict.Count:n0}";
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

            // Log errors to debug output
            if (scanResult.HasErrors)
            {
                System.Diagnostics.Debug.WriteLine($"Scan completed with {scanResult.Errors.Count} errors:");
                foreach (var err in scanResult.Errors.Take(10))
                {
                    System.Diagnostics.Debug.WriteLine($"  [{err.ExceptionType}] {err.Path}: {err.Message}");
                }
                if (scanResult.Errors.Count > 10)
                    System.Diagnostics.Debug.WriteLine($"  ... and {scanResult.Errors.Count - 10} more errors");
            }

            if (dict.Count == 0)
            {
                statusText.Text = "No items found";
                return;
            }

            // Phase 2: Rendering
            progressWindow.SetPhase($"🎨 Rendering {dict.Count:n0} items...");
            progressWindow.ReportProgress(0);

            treeCanvas.Children.Clear();

            // Find root key
            string? rootKey = null;
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

            // Calculate canvas bounds
            var availW = treeCanvasHost.Bounds.Width;
            var availH = treeCanvasHost.Bounds.Height;
            if (availW <= 0) availW = parentWindow.Bounds.Width - 360;
            if (availH <= 0) availH = parentWindow.Bounds.Height - 120;
            var rect = new Rect(0, 0, availW > 0 ? availW : 800, availH > 0 ? availH : 600);
            treeCanvas.Width = rect.Width;
            treeCanvas.Height = rect.Height;

            // Render with progress (pass existing progress window)
            await TreemapPort.MakeTreemapAsync(dict, treeCanvas, rootKey, rect, total, true, cts, progressWindow);

            statusText.Text = summary;
        }
        catch (OperationCanceledException)
        {
            statusText.Text = "Operation cancelled";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"RunScanAndRenderAsync error: {ex}");
        }
    }
}
