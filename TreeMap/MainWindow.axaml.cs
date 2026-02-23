using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TreeMap;

public partial class MainWindow : Window
{
    // promoted shared state to instance fields to simplify extraction of helpers
    private ScanResult? _lastScanResult;
    private bool _isRefreshingCombo;
    private UserSettings? _userSettings;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        // Load user settings (MRU paths, cloud handling preference)
        _userSettings = TreeMap.UserSettings.Load();

        // Populate path combo with MRU paths
        foreach (var mruPath in _userSettings.MruPaths)
        {
            this.PathCombo.Items.Add(mruPath);
        }
        // Select the first item if available
        if (PathCombo.Items.Count > 0)
        {
            this.PathCombo.SelectedIndex = 0;
        }

        // Set cloud handling from saved setting
        if (CloudHandlingCombo != null && _userSettings.CloudHandlingIndex >= 0 && _userSettings.CloudHandlingIndex < 3)
        {
            CloudHandlingCombo.SelectedIndex = _userSettings.CloudHandlingIndex;
        }

        _isRefreshingCombo = false;
        // BrowseControl and last scan stored on instance fields
        _lastScanResult = null;


        // Helper to redraw the treemap using existing scan data (without rescanning)
        //System.Action? redrawTreemap = () => RedrawTreemap(TreeCanvas, TreeCanvasHost, CloudHandlingCombo);

        // Wire up cloud handling combo - recalculate sizes without rescanning
        if (CloudHandlingCombo != null)
        {
            CloudHandlingCombo.SelectionChanged += (s, args) =>
            {
                if (_lastScanResult != null && _lastScanResult.Data.Count > 0)
                {
                    var cloudHandling = GetCloudHandling(CloudHandlingCombo);
                    DiskScanner.RecalculateSizes(_lastScanResult, cloudHandling);
                    _userSettings.CloudHandlingIndex = CloudHandlingCombo.SelectedIndex;
                    _userSettings.Save();
                    this.RedrawTreemap();
                    //RedrawTreemap(TreeCanvas, TreeCanvasHost, CloudHandlingCombo);
                }
            };
        }
        this.ShowFileListBtn.Click += (o, e) =>
        {
            this.ShowBrowseList();
        };

        //        // Helper to toggle between treemap and file list views
        //        System.Action toggleView = () =>
        //        {
        //            bool showFileList = !LeftHost.IsVisible;

        //            // Create BrowseControl on first show, using last scan results
        //            if (showFileList && _browse == null && _lastScanResult != null)
        //            {
        //                //var items = new System.Collections.Generic.List<object>();
        //                //foreach (var kv in lastScanResult.Data)
        //                //    items.Add(new { 
        //                //        Path = kv.Key, 
        //                //        Size = kv.Value.Size,
        //                //        Files = kv.Value.NumFiles > 0 ? kv.Value.NumFiles.ToString() : "",
        //                //        CloudFiles = kv.Value.CloudFileCount > 0 ? kv.Value.CloudFileCount.ToString() : ""
        //                //    });
        //                /*
        //•	LocalSize = bytes actually stored locally on disk (sum of file lengths for files that are present locally).
        //•	CloudLogicalSize = bytes that cloud-only files would take if downloaded (logical size reported by metadata/reparse point).
        //•	Size = the displayed/used size after applying the chosen cloud-handling policy:
        //•	IncludeLogicalSize (default): Size = LocalSize + CloudLogicalSize
        //•	ExcludeFromSize: Size = LocalSize
        //•	IncludePlaceholderSize: Size = LocalSize + (~1 KB per cloud file) (DiskScanner uses a 1024 byte placeholder estimate)
        //Implementation notes (from the code)
        //•	DiskScanner.ScanInternal tracks LocalSize and CloudLogicalSize separately and stores them on each MapDataItem.
        //•	DiskScanner.CalculateSize(localSize, cloudLogicalSize, cloudFileCount, cloudHandling) computes Size according to the policy.
        //•	DiskScanner.RecalculateSizes(result, cloudHandling) recomputes each MapDataItem.Size from LocalSize and CloudLogicalSize without rescanning.
        //•	Diff (what you added) = Size - LocalSize shows the extra bytes coming from cloud logical size or placeholder estimates. Example: if LocalSize=100MB, CloudLogicalSize=900MB:
        //•	IncludeLogicalSize -> Size=100+900=1000MB, Diff=900MB
        //•	ExcludeFromSize -> Size=100MB, Diff=0
        //•	Includ                 */
        //                // Use the recorded scan root if available to save horizontal space; otherwise fall back to inferring
        //                var rootPrefix = _lastScanResult.RootPath ?? string.Empty;
        //                var rootLength = rootPrefix.Length > 0 ? rootPrefix.Length - 1 : 0;

        //                var items = from kv in _lastScanResult.Data
        //                            select new
        //                            {
        //                                // Remove the root prefix to save horizontal space in the list
        //                                Path = kv.Key.Substring(rootLength),
        //                                Size = kv.Value.Size,
        //                                LocalSize = kv.Value.LocalSize,
        //                                kv.Value.CloudLogicalSize,
        //                                Files = kv.Value.NumFiles > 0 ? kv.Value.NumFiles.ToString() : "",
        //                                CloudFiles = kv.Value.CloudFileCount > 0 ? kv.Value.CloudFileCount.ToString() : "",
        //                                kv.Value.IsCloudOnly,
        //                                kv.Value.Depth,
        //                            };
        //                _browse = new BrowseControl(items, new[] { 500, 100, 100, 100, 100, 70 }, true);
        //                LeftHost.Content = _browse;
        //            }

        //            LeftHost.IsVisible = showFileList;
        //            TreeCanvasHost.IsVisible = !showFileList;

        //            // Update button and menu text
        //            var newText = showFileList ? "Show Treemap" : "Show File List";
        //            if (ToggleViewBtn != null) ToggleViewBtn.Content = newText;
        //            if (ToggleBrowseMenuItem != null) ToggleBrowseMenuItem.Header = newText;
        //            // Update the file list context menu too
        //            var toggleTreemapMenuItem = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
        //            if (toggleTreemapMenuItem != null) toggleTreemapMenuItem.Header = newText;
        //        };


        //// Wire up context menu items (both treemap and file list)
        //if (ToggleBrowseMenuItem != null)
        //{
        //    ToggleBrowseMenuItem.Click += (s, args) => toggleView();
        //}
        //var toggleTreemapMenuItemInit = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
        //if (toggleTreemapMenuItemInit != null)
        //{
        //    toggleTreemapMenuItemInit.Click += (s, args) => toggleView();
        //}

        // Wire up swap orientation menu item
        var swapOrientationMenuItem = this.FindControl<MenuItem>("SwapOrientationMenuItem");
        if (swapOrientationMenuItem != null)
        {
            swapOrientationMenuItem.Click += async (s, args) =>
            {
                if (TreemapPort.CurrentDict != null && TreemapPort.CurrentRootPath != null)
                {
                    TreeCanvas.Children.Clear();
                    var rootKey = TreemapPort.CurrentRootPath;
                    long total = TreemapPort.CurrentDict.ContainsKey(rootKey) ? TreemapPort.CurrentDict[rootKey].Size : 0;
                    // If total is still 0, sum up child sizes (same fallback as main scan)
                    if (total == 0)
                    {
                        foreach (var v in TreemapPort.CurrentDict.Values)
                            total += v.Size;
                    }
                    var rect = new Rect(0, 0, TreeCanvas.Width, TreeCanvas.Height);
                    // Swap orientation with progress window
                    await TreemapPort.MakeTreemapAsync(TreemapPort.CurrentDict, TreeCanvas, rootKey, rect, total, !TreemapPort.CurrentHorizontal);
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
        //System.Action<string>? runScan = null;
        TreeCanvasHost.LayoutUpdated += (s, ev) =>
        {
            try
            {
                if (!initialScanDone && TreeCanvasHost.Bounds.Width > 0 && TreeCanvasHost.Bounds.Height > 0)
                {
                    initialScanDone = true;
                    var initialPath = _userSettings.MruPaths.Count > 0
                        ? _userSettings.MruPaths[0]
                        : System.IO.Directory.GetCurrentDirectory();
                    SetPath(this.PathCombo, initialPath);
                    //Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan?.Invoke(initialPath), Avalonia.Threading.DispatcherPriority.Background);
                    runScan(initialPath);
                }
            }
            catch { }
        };

        // Helper to run a scan asynchronously and draw the treemap for a given path
        //runScan = (path) =>
        //{
        //};

        // Define redrawTreemap - redraws treemap without rescanning (for cloud handling changes)

        // Wire scan button to run the common runScan helper
        this.ScanBtn.Click += (_, __) =>
        {
            var path = GetPath(this.PathCombo);
            runScan(path);
        };

        // Wire up path combo selection changed - scan when user selects from MRU dropdown
        this.PathCombo.SelectionChanged += (s, args) =>
        {
            // Skip if we're just refreshing the combo programmatically
            if (_isRefreshingCombo)
                return;

            if (args.AddedItems.Count > 0 && args.AddedItems[0] is string selectedPath)
            {
                // Only scan if selection actually changed (not just programmatic refresh)
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    runScan(selectedPath);
                }
            }
        };

        // Allow user to press Enter in the combo box to scan (for manually typed paths)
        this.PathCombo.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                var path = GetPath(this.PathCombo);
                if (!string.IsNullOrEmpty(path) && runScan != null)
                {
                    runScan(path);
                    e.Handled = true;
                }
            }
        };

        // Folder picker using Avalonia's OpenFolderDialog
        if (BrowseBtn != null)
        {
            BrowseBtn.Click += async (_, __) =>
            {
                try
                {
                    var dlg = new OpenFolderDialog();
                    var result = await dlg.ShowAsync(this);
                    if (!string.IsNullOrEmpty(result))
                    {
                        SetPath(this.PathCombo, result);
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
            var initialPath = _userSettings.MruPaths.Count > 0
                ? _userSettings.MruPaths[0]
                : System.IO.Directory.GetCurrentDirectory();
            SetPath(this.PathCombo, initialPath);
            // schedule scan after layout so canvas has real bounds
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                runScan(initialPath);
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Also schedule an initial scan immediately after OnOpened to ensure we run
        // when AttachedToVisualTree did not fire early enough.
        var initPathNow = _userSettings.MruPaths.Count > 0
            ? _userSettings.MruPaths[0]
            : System.IO.Directory.GetCurrentDirectory();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => runScan(initPathNow), Avalonia.Threading.DispatcherPriority.Background);
    }

    void ShowBrowseList()
    {
        try
        {
            /*
•	LocalSize = bytes actually stored locally on disk (sum of file lengths for files that are present locally).
•	CloudLogicalSize = bytes that cloud-only files would take if downloaded (logical size reported by metadata/reparse point).
•	Size = the displayed/used size after applying the chosen cloud-handling policy:
•	IncludeLogicalSize (default): Size = LocalSize + CloudLogicalSize
•	ExcludeFromSize: Size = LocalSize
•	IncludePlaceholderSize: Size = LocalSize + (~1 KB per cloud file) (DiskScanner uses a 1024 byte placeholder estimate)
Implementation notes (from the code)
•	DiskScanner.ScanInternal tracks LocalSize and CloudLogicalSize separately and stores them on each MapDataItem.
•	DiskScanner.CalculateSize(localSize, cloudLogicalSize, cloudFileCount, cloudHandling) computes Size according to the policy.
•	DiskScanner.RecalculateSizes(result, cloudHandling) recomputes each MapDataItem.Size from LocalSize and CloudLogicalSize without rescanning.
•	Diff (what you added) = Size - LocalSize shows the extra bytes coming from cloud logical size or placeholder estimates. Example: if LocalSize=100MB, CloudLogicalSize=900MB:
•	IncludeLogicalSize -> Size=100+900=1000MB, Diff=900MB
•	ExcludeFromSize -> Size=100MB, Diff=0
•	Includ                 */
            // Use the recorded scan root if available to save horizontal space; otherwise fall back to inferring
            if (_lastScanResult == null)
            {
                return;
            }
            var rootPrefix = _lastScanResult.RootPath ?? string.Empty;
            var rootLength = rootPrefix.Length > 0 ? rootPrefix.Length - 1 : 0;

            var items = from kv in _lastScanResult.Data
                        select new
                        {
                            // Remove the root prefix to save horizontal space in the list
                            Path = kv.Key.Substring(rootLength),
                            Size = kv.Value.Size,
                            LocalSize = kv.Value.LocalSize,
                            kv.Value.CloudLogicalSize,
                            Files = kv.Value.NumFiles > 0 ? kv.Value.NumFiles.ToString() : "",
                            CloudFiles = kv.Value.CloudFileCount > 0 ? kv.Value.CloudFileCount.ToString() : "",
                            kv.Value.IsCloudOnly,
                            kv.Value.Depth,
                        };
            var browse = new BrowseControl(items, new[] { 500, 100, 100, 100, 100, 70 }, true);
            var fileListWIndow = new Window()
            {
                WindowState = WindowState.Maximized,
                ShowInTaskbar = false
            };
            fileListWIndow.Content = browse;
            fileListWIndow.Show(this);

        }
        catch (System.Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            switch (e.Key)
            {
                case Key.F:
                    ShowBrowseList();
                    break;
                case Key.Q:
                    Close();
                    break;
            }

        }
    }

    void runScan(string path)
    {
        // Add to MRU before scanning
        AddToMruList(path, CloudHandlingCombo, this.PathCombo);
        // Switch to treemap view if currently showing file list (file list becomes stale with new scan)
        //if (LeftHost.IsVisible)
        //{
        //    LeftHost.IsVisible = false;
        //    TreeCanvasHost.IsVisible = true;
        //    var newText = "Show File List";
        //    if (ToggleViewBtn != null) ToggleViewBtn.Content = newText;
        //    if (ToggleBrowseMenuItem != null) ToggleBrowseMenuItem.Header = newText;
        //    var toggleTreemapMenuItem = this.FindControl<MenuItem>("ToggleTreemapMenuItem");
        //    if (toggleTreemapMenuItem != null) toggleTreemapMenuItem.Header = newText;
        //}

        // Create unified progress window for both scanning and rendering
        var cts = new System.Threading.CancellationTokenSource();

        // Get the selected cloud handling mode
        var cloudHandling = GetCloudHandling(CloudHandlingCombo);

        // Run the combined scan + render operation
        _ = RunScanAndRenderAsync(path, cloudHandling, cts, TreeCanvas, TreeCanvasHost, this,
            result =>
            {
                _lastScanResult = result;
                try
                {
                    // Ensure the UI shows the path that was scanned
                    SetPath(this.PathCombo, path);
                }
                catch { }
            });

    }

    private void RedrawTreemap()
    {
        if (_lastScanResult == null || _lastScanResult.Data.Count == 0)
            return;

        var dict = _lastScanResult.Data;
        TreeCanvas.Children.Clear();

        // Use the recorded scan root if available; otherwise fall back to inferring the root key
        string? rootKey = _lastScanResult?.RootPath;
        if (rootKey == null)
        {
            foreach (var k in dict.Keys)
            {
                if (k.EndsWith(TreeMapConstants.PathSep.ToString()))
                {
                    if (rootKey == null || k.Length < rootKey.Length) rootKey = k;
                }
            }
        }
        if (rootKey == null)
            return;

        long total = dict.ContainsKey(rootKey) ? dict[rootKey].Size : 0;
        if (total == 0)
        {
            foreach (var v in dict.Values) total += v.Size;
        }

        var availW = TreeCanvasHost.Bounds.Width;
        var availH = TreeCanvasHost.Bounds.Height;
        if (availW <= 0) availW = this.Bounds.Width - 360;
        if (availH <= 0) availH = this.Bounds.Height - 120;
        var rect = new Rect(0, 0, availW > 0 ? availW : 800, availH > 0 ? availH : 600);
        TreeCanvas.Width = rect.Width;
        TreeCanvas.Height = rect.Height;

        // Redraw synchronously (data is already in memory)
        TreemapPort.MakeTreemap(dict, TreeCanvas, rootKey, rect, total, TreemapPort.CurrentHorizontal);

        // Update status text with summary
        if (StatusText != null)
        {
            var cloudHandling = GetCloudHandling(CloudHandlingCombo);
            var modeStr = cloudHandling switch
            {
                CloudFileHandling.ExcludeFromSize => "Cloud: Excluded",
                CloudFileHandling.IncludePlaceholderSize => "Cloud: Placeholder",
                _ => "Cloud: Logical Size"
            };
            StatusText.Text = $"Items: {dict.Count:n0}, {modeStr}";
        }

    }

    // Extracted focused helpers used by InitializeAfterOpened
    private string GetPath(ComboBox pathCombo)
    {
        var text = pathCombo?.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(text))
        {
            var textBox = pathCombo?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            text = textBox?.Text;
        }
        return text ?? System.IO.Directory.GetCurrentDirectory();
    }

    private void SetPath(ComboBox pathCombo, string path)
    {
        var textBox = pathCombo?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (textBox != null)
        {
            textBox.Text = path;
        }
    }

    private void AddToMruList(string path, ComboBox? cloudHandlingCombo, ComboBox pathCombo)
    {
        if (_isRefreshingCombo || _userSettings == null)
            return;

        _userSettings.AddMruPath(path);
        _userSettings.CloudHandlingIndex = cloudHandlingCombo?.SelectedIndex ?? 0;
        _userSettings.Save();

        _isRefreshingCombo = true;
        pathCombo.SelectedIndex = -1;
        pathCombo.Items.Clear();
        foreach (var mruPath in _userSettings.MruPaths)
            pathCombo.Items.Add(mruPath);
        if (pathCombo.Items.Count > 0)
            pathCombo.SelectedIndex = 0;
        SetPath(pathCombo, path);
        _isRefreshingCombo = false;
    }

    private CloudFileHandling GetCloudHandling(ComboBox? cloudHandlingCombo)
    {
        return cloudHandlingCombo?.SelectedIndex switch
        {
            1 => CloudFileHandling.ExcludeFromSize,
            2 => CloudFileHandling.IncludePlaceholderSize,
            _ => CloudFileHandling.IncludeLogicalSize
        };
    }

    private void RedrawTreemap(Canvas treeCanvas, Border treeCanvasHost, ComboBox? cloudHandlingCombo)
    {
        if (_lastScanResult == null || _lastScanResult.Data.Count == 0)
            return;

        var dict = _lastScanResult.Data;
        treeCanvas.Children.Clear();

        string? rootKey = _lastScanResult?.RootPath;
        if (rootKey == null)
        {
            foreach (var k in dict.Keys)
            {
                if (k.EndsWith(TreeMapConstants.PathSep.ToString()))
                {
                    if (rootKey == null || k.Length < rootKey.Length) rootKey = k;
                }
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

        TreemapPort.MakeTreemap(dict, treeCanvas, rootKey, rect, total, TreemapPort.CurrentHorizontal);

        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null)
        {
            var cloudHandling = GetCloudHandling(cloudHandlingCombo);
            var modeStr = cloudHandling switch
            {
                CloudFileHandling.ExcludeFromSize => "Cloud: Excluded",
                CloudFileHandling.IncludePlaceholderSize => "Cloud: Placeholder",
                _ => "Cloud: Logical Size"
            };
            statusText.Text = $"Items: {dict.Count:n0}, {modeStr}";
        }
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

        string summary = "";

        try
        {
            // Phase 1: Disk Scanning
            progressWindow.SetPhase($"📁 Scanning: {path}");

            var scanResult = await DiskScanner.ScanWithErrorsAsync(path, progressWindow, cts.Token, cloudHandling);

            if (cts.IsCancellationRequested)
            {
                StatusText.Text = "Scan cancelled";
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
                StatusText.Text = "No items found";
                return;
            }

            // Phase 2: Rendering
            progressWindow.SetPhase($"🎨 Rendering {dict.Count:n0} items...");
            progressWindow.ReportProgress(0);

            treeCanvas.Children.Clear();

            // Prefer the root path recorded on the scan result; otherwise infer from keys
            string? rootKey = scanResult.RootPath;
            if (rootKey == null)
            {
                foreach (var k in dict.Keys)
                {
                    if (k.EndsWith(TreeMapConstants.PathSep.ToString()))
                    {
                        if (rootKey == null || k.Length < rootKey.Length) rootKey = k;
                    }
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

            StatusText.Text = summary;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"RunScanAndRenderAsync error: {ex}");
        }
    }
}
