using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TreeMap;
using Xunit;
using Xunit.Abstractions;

namespace TreeMap.Tests;

/// <summary>
/// Tests for ProgressWindow - the progress bar that runs on its own UI thread.
/// These tests demonstrate the progress window with cancellation support.
/// Run with: dotnet test --filter "FullyQualifiedName~ProgressWindowTests"
/// Manual tests: dotnet test --filter "Category=Manual"
/// </summary>
public class ProgressWindowTests
{
    private readonly ITestOutputHelper _output;

    public ProgressWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// MANUAL TEST: Shows actual Avalonia window with progressive rectangle rendering.
    /// Uses an in-window progress bar since Avalonia doesn't support multiple Application instances.
    /// Run with: dotnet test --filter "ProgressWindow_Manual_ShowsActualAvaloniaWindowWithProgressiveRendering"
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task ProgressWindow_Manual_ShowsActualAvaloniaWindowWithProgressiveRendering()
    {
        var tcs = new TaskCompletionSource<bool>();

        // Create STA thread for Avalonia
        var uiThread = new Thread(() =>
        {
            try
            {
                // Initialize Avalonia
                var builder = AppBuilder.Configure<Application>()
                    .UsePlatformDetect()
                    .WithInterFont();
                builder.SetupWithoutStarting();

                // Create main window with canvas and embedded progress bar
                var window = new Window
                {
                    Title = "Manual Test - Progressive Rendering with Progress Bar",
                    Width = 1000,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var mainGrid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
                };

                var statusText = new TextBlock
                {
                    Text = "Click 'Start Rendering' to begin...",
                    Margin = new Thickness(10),
                    FontSize = 14
                };
                Grid.SetRow(statusText, 0);

                // Progress panel (initially hidden)
                var progressPanel = new StackPanel
                {
                    Margin = new Thickness(10, 0, 10, 10),
                    IsVisible = false
                };
                var progressBar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Height = 25,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var progressText = new TextBlock
                {
                    Text = "0%",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                var cancelButton = new Button
                {
                    Content = "Cancel",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0),
                    Width = 80
                };
                progressPanel.Children.Add(progressBar);
                progressPanel.Children.Add(progressText);
                progressPanel.Children.Add(cancelButton);
                Grid.SetRow(progressPanel, 1);

                var canvas = new Canvas
                {
                    Background = Brushes.White,
                    ClipToBounds = true
                };
                Grid.SetRow(canvas, 2);

                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10)
                };
                Grid.SetRow(buttonPanel, 3);

                var startButton = new Button { Content = "Start Rendering (10K rectangles)", Margin = new Thickness(5) };
                var clearButton = new Button { Content = "Clear", Margin = new Thickness(5) };
                var closeButton = new Button { Content = "Close", Margin = new Thickness(5) };

                buttonPanel.Children.Add(startButton);
                buttonPanel.Children.Add(clearButton);
                buttonPanel.Children.Add(closeButton);

                mainGrid.Children.Add(statusText);
                mainGrid.Children.Add(progressPanel);
                mainGrid.Children.Add(canvas);
                mainGrid.Children.Add(buttonPanel);

                window.Content = mainGrid;

                // Wire up buttons
                clearButton.Click += (s, e) =>
                {
                    canvas.Children.Clear();
                    statusText.Text = "Canvas cleared. Click 'Start Rendering' to begin...";
                };

                closeButton.Click += (s, e) =>
                {
                    window.Close();
                };

                var cts = new CancellationTokenSource();
                cancelButton.Click += (s, e) =>
                {
                    cts.Cancel();
                    cancelButton.IsEnabled = false;
                    cancelButton.Content = "Cancelling...";
                };

                startButton.Click += async (s, e) =>
                {
                    startButton.IsEnabled = false;
                    canvas.Children.Clear();
                    cts = new CancellationTokenSource();
                    cancelButton.IsEnabled = true;
                    cancelButton.Content = "Cancel";

                    // Show progress panel
                    progressPanel.IsVisible = true;
                    progressBar.Value = 0;

                    // Simulate creating many rectangles like treemap does
                    const int totalRectangles = 10000;
                    const int batchSize = 200;
                    var random = new Random(42);

                    var sw = Stopwatch.StartNew();
                    int rendered = 0;
                    double canvasWidth = 980;
                    double canvasHeight = 550;

                    statusText.Text = "Rendering in progress...";

                    for (int i = 0; i < totalRectangles && !cts.IsCancellationRequested; i += batchSize)
                    {
                        // Create batch of rectangles
                        for (int j = i; j < Math.Min(i + batchSize, totalRectangles); j++)
                        {
                            var rect = new Rectangle
                            {
                                Width = random.Next(5, 50),
                                Height = random.Next(5, 50),
                                Fill = new SolidColorBrush(Color.FromRgb(
                                    (byte)random.Next(256),
                                    (byte)random.Next(256),
                                    (byte)random.Next(256))),
                                Stroke = Brushes.Black,
                                StrokeThickness = 0.5
                            };
                            Canvas.SetLeft(rect, random.NextDouble() * canvasWidth);
                            Canvas.SetTop(rect, random.NextDouble() * canvasHeight);
                            canvas.Children.Add(rect);
                            rendered++;
                        }

                        // Update progress
                        int pct = (int)(rendered * 100.0 / totalRectangles);
                        progressBar.Value = pct;
                        progressText.Text = $"Rendered {rendered:n0} / {totalRectangles:n0} ({pct}%)";
                        statusText.Text = $"Rendering: {rendered:n0} / {totalRectangles:n0} rectangles ({pct}%)";

                        // Yield to UI - THIS IS KEY for progress bar to update
                        await Task.Delay(1);
                    }

                    sw.Stop();
                    progressPanel.IsVisible = false;
                    var finalStatus = cts.IsCancellationRequested 
                        ? $"Cancelled after {rendered:n0} rectangles in {sw.ElapsedMilliseconds}ms"
                        : $"Completed {rendered:n0} rectangles in {sw.ElapsedMilliseconds}ms";
                    statusText.Text = finalStatus;
                    startButton.IsEnabled = true;
                };

                window.Closed += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeShutdown();
                    tcs.TrySetResult(true);
                };

                window.Show();
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error: {ex}");
                tcs.TrySetException(ex);
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Name = "ManualTestUIThread";
        uiThread.Start();

        // Wait for window to close
        await tcs.Task;
        _output.WriteLine("Manual test completed");
    }

    /// <summary>
    /// MANUAL TEST: Shows treemap-like progressive rendering with actual TreemapPort-style layout.
    /// Run with: dotnet test --filter "ProgressWindow_Manual_TreemapStyleRendering"
    /// </summary>
    [Fact]
    [Trait("Category", "Manual")]
    public async Task ProgressWindow_Manual_TreemapStyleRendering()
    {
        var tcs = new TaskCompletionSource<bool>();

        var uiThread = new Thread(() =>
        {
            try
            {
                var builder = AppBuilder.Configure<Application>()
                    .UsePlatformDetect()
                    .WithInterFont();
                builder.SetupWithoutStarting();

                var window = new Window
                {
                    Title = "Manual Test - Treemap Style Progressive Rendering",
                    Width = 1200,
                    Height = 850,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var mainGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

                var statusText = new TextBlock
                {
                    Text = "Click 'Render Treemap' to simulate treemap rendering with progress",
                    Margin = new Thickness(10),
                    FontSize = 14
                };
                Grid.SetRow(statusText, 0);

                // Progress panel
                var progressPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 10), IsVisible = false };
                var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 25 };
                var progressText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                var cancelButton = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, Width = 80, Margin = new Thickness(0, 5, 0, 0) };
                progressPanel.Children.Add(progressBar);
                progressPanel.Children.Add(progressText);
                progressPanel.Children.Add(cancelButton);
                Grid.SetRow(progressPanel, 1);

                var canvas = new Canvas { Background = Brushes.White, ClipToBounds = true };
                Grid.SetRow(canvas, 2);

                var buttonPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10)
                };
                Grid.SetRow(buttonPanel, 3);

                var renderButton = new Button { Content = "Render Treemap (5K items)", Margin = new Thickness(5) };
                var closeButton = new Button { Content = "Close", Margin = new Thickness(5) };
                buttonPanel.Children.Add(renderButton);
                buttonPanel.Children.Add(closeButton);

                mainGrid.Children.Add(statusText);
                mainGrid.Children.Add(progressPanel);
                mainGrid.Children.Add(canvas);
                mainGrid.Children.Add(buttonPanel);
                window.Content = mainGrid;

                closeButton.Click += (s, e) => window.Close();

                var cts = new CancellationTokenSource();
                cancelButton.Click += (s, e) =>
                {
                    cts.Cancel();
                    cancelButton.IsEnabled = false;
                    cancelButton.Content = "Cancelling...";
                };

                renderButton.Click += async (s, e) =>
                {
                    renderButton.IsEnabled = false;
                    canvas.Children.Clear();
                    cts = new CancellationTokenSource();
                    cancelButton.IsEnabled = true;
                    cancelButton.Content = "Cancel";
                    progressPanel.IsVisible = true;
                    progressBar.Value = 0;

                    // Create fake scan data
                    var dict = new ConcurrentDictionary<string, MapDataItem>();
                    var basePath = @"C:\TestDir\";
                    for (int i = 0; i < 5000; i++)
                    {
                        var depth = (i % 5) + 1;
                        var path = basePath + string.Join("\\", Enumerable.Range(0, depth).Select(d => $"Folder{(i / (int)Math.Pow(10, d)) % 10}")) + $"\\Item{i}\\";
                        dict[path] = new MapDataItem
                        {
                            Size = (5000 - i) * 1000 + i,
                            Depth = depth,
                            NumFiles = i % 10 + 1
                        };
                    }

                    var sw = Stopwatch.StartNew();
                    statusText.Text = "Collecting elements...";
                    progressText.Text = "Collecting elements...";

                    // Simulate CollectTreemapElements - create rectangles
                    var elements = new System.Collections.Generic.List<(Control, double, double)>();
                    double canvasW = 1180;
                    double canvasH = 650;
                    var keys = dict.Keys.OrderByDescending(k => dict[k].Size).ToList();

                    double x = 0, y = 0;
                    double rowHeight = canvasH / 50;
                    int col = 0;
                    int maxCols = 100;

                    foreach (var key in keys)
                    {
                        var item = dict[key];
                        double w = Math.Max(5, (item.Size / 1000000.0) * 2);
                        double h = rowHeight - 2;

                        var color = Color.FromRgb(
                            (byte)((item.Depth * 50) % 256),
                            (byte)((item.Size / 10000) % 256),
                            (byte)((item.NumFiles * 25) % 256));

                        var rect = new Rectangle
                        {
                            Width = w,
                            Height = h,
                            Fill = new SolidColorBrush(color),
                            Stroke = Brushes.Black,
                            StrokeThickness = 0.5
                        };

                        elements.Add((rect, x, y));

                        x += w + 1;
                        col++;
                        if (col >= maxCols || x + w > canvasW)
                        {
                            x = 0;
                            y += rowHeight;
                            col = 0;
                        }
                    }

                    statusText.Text = $"Adding {elements.Count} elements to canvas...";
                    progressBar.Value = 0;

                    // Add in batches with progress
                    const int batchSize = 100;
                    for (int i = 0; i < elements.Count && !cts.IsCancellationRequested; i += batchSize)
                    {
                        var batch = elements.Skip(i).Take(batchSize);
                        foreach (var (element, left, top) in batch)
                        {
                            Canvas.SetLeft(element, left);
                            Canvas.SetTop(element, top);
                            canvas.Children.Add(element);
                        }

                        int pct = (int)((i + batchSize) * 100.0 / elements.Count);
                        progressBar.Value = Math.Min(pct, 100);
                        progressText.Text = $"Added {Math.Min(i + batchSize, elements.Count):n0} / {elements.Count:n0}";
                        statusText.Text = $"Rendering: {Math.Min(i + batchSize, elements.Count):n0} / {elements.Count:n0} ({Math.Min(pct, 100)}%)";

                        await Task.Delay(1);
                    }

                    sw.Stop();
                    progressPanel.IsVisible = false;
                    statusText.Text = cts.IsCancellationRequested
                        ? $"Cancelled - rendered {canvas.Children.Count} items in {sw.ElapsedMilliseconds}ms"
                        : $"Complete - rendered {canvas.Children.Count} items in {sw.ElapsedMilliseconds}ms";
                    renderButton.IsEnabled = true;
                };

                window.Closed += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeShutdown();
                    tcs.TrySetResult(true);
                };

                window.Show();
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Name = "ManualTestUIThread";
        uiThread.Start();

        await tcs.Task;
        _output.WriteLine("Treemap style test completed");
    }

    [Fact]
    public async Task ProgressWindow_ShowsAndUpdates()
    {
        // This test shows the progress window and updates it
        using var progressWindow = new ProgressWindow("Test Progress - Updates");
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("Testing Progress");

        for (int i = 0; i <= 100; i += 10)
        {
            progressWindow.ReportProgress(i);
            progressWindow.Report($"Progress: {i}%");
            await Task.Delay(100);
        }

        _output.WriteLine("Progress window test completed");
    }

    [Fact]
    public async Task ProgressWindow_WithCancellation_ShowsCancelButton()
    {
        using var cts = new CancellationTokenSource();
        using var progressWindow = new ProgressWindow("Test with Cancel Button - Click Cancel!", cts);
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("Processing...");

        // Simulate work that can be cancelled
        for (int i = 0; i <= 100; i += 5)
        {
            if (progressWindow.IsCancellationRequested)
            {
                _output.WriteLine($"Cancelled at {i}%");
                break;
            }
            progressWindow.ReportProgress(i);
            progressWindow.Report($"Processing... {i}%");
            await Task.Delay(200); // Slow enough to click Cancel
        }

        _output.WriteLine($"Test completed. Was cancelled: {cts.IsCancellationRequested}");
    }

    [Fact]
    public async Task ProgressWindow_SimulatesLargeTreemapRendering()
    {
        // Simulate what happens when rendering a large treemap
        const int totalItems = 50000;
        const int batchSize = 500;

        using var cts = new CancellationTokenSource();
        using var progressWindow = new ProgressWindow($"Rendering {totalItems:n0} items...", cts);
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("🎨 Rendering");

        var sw = Stopwatch.StartNew();
        int processed = 0;

        // Simulate batched rendering
        for (int i = 0; i < totalItems && !cts.IsCancellationRequested; i += batchSize)
        {
            // Simulate creating UI elements (this would block main UI thread)
            await Task.Delay(50); // Simulates work

            processed = Math.Min(i + batchSize, totalItems);
            int pct = (int)(processed * 100.0 / totalItems);
            progressWindow.ReportProgress(pct);
            progressWindow.Report($"Rendered {processed:n0} / {totalItems:n0} items ({pct}%)");
        }

        sw.Stop();
        _output.WriteLine($"Completed in {sw.ElapsedMilliseconds}ms. Processed: {processed:n0}. Cancelled: {cts.IsCancellationRequested}");
    }

    [Fact]
    public async Task ProgressWindow_StringStatusUpdates()
    {
        using var progressWindow = new ProgressWindow("Status Updates Test");
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("Testing Status Updates");
        var statuses = new[]
        {
            "Initializing...",
            "Scanning directories...",
            "Processing files...",
            "Building treemap...",
            "Rendering rectangles...",
            "Complete!"
        };

        foreach (var status in statuses)
        {
            progressWindow.Report(status);
            await Task.Delay(500);
        }
    }

    [Fact]
    public async Task ProgressWindow_RapidUpdates_StaysResponsive()
    {
        // Test that rapid updates don't overwhelm the progress window
        using var progressWindow = new ProgressWindow("Rapid Updates Test");
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("Rapid Updates");

        var sw = Stopwatch.StartNew();

        // Rapid fire updates
        for (int i = 0; i <= 100; i++)
        {
            progressWindow.ReportProgress(i);
            if (i % 10 == 0)
            {
                progressWindow.Report($"Batch {i / 10} of 10");
                await Task.Delay(1); // Small yield
            }
        }

        sw.Stop();
        _output.WriteLine($"100 rapid updates completed in {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ProgressWindow_CancellationToken_CanBeUsedInLoop()
    {
        using var cts = new CancellationTokenSource();
        using var progressWindow = new ProgressWindow("CancellationToken Test - Click Cancel", cts);
        await progressWindow.ShowAsync();
        progressWindow.SetPhase("Processing...");

        try
        {
            for (int i = 0; i <= 100; i++)
            {
                // This is how you'd use it in real code
                progressWindow.CancellationToken.ThrowIfCancellationRequested();

                progressWindow.ReportProgress(i);
                await Task.Delay(100, progressWindow.CancellationToken);
            }
            _output.WriteLine("Completed without cancellation");
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("Operation was cancelled via CancellationToken");
        }
    }

    /// <summary>
    /// This test simulates the actual treemap rendering scenario with a mock dictionary.
    /// It demonstrates how the progress window works during a long-running UI operation.
    /// </summary>
    [Fact]
    public async Task ProgressWindow_SimulatesActualTreemapScenario()
    {
        // Create a mock dictionary similar to what DiskScanner produces
        var dict = new ConcurrentDictionary<string, MapDataItem>();
        var basePath = @"C:\TestDir\";

        // Add test items
        for (int i = 0; i < 10000; i++)
        {
            var path = $"{basePath}Folder{i / 100}\\File{i}.txt";
            dict[path] = new MapDataItem
            {
                Size = (i + 1) * 1000,
                Depth = 3,
                NumFiles = 1
            };
        }

        using var cts = new CancellationTokenSource();
        using var progressWindow = new ProgressWindow($"Treemap Simulation", cts);
        await progressWindow.ShowAsync();
        progressWindow.SetPhase($"🎨 Rendering {dict.Count:n0} items");

        var sw = Stopwatch.StartNew();
        int processed = 0;
        const int batchSize = 200;
        var keys = dict.Keys.ToList();

        // Simulate batched element creation and canvas adding
        for (int i = 0; i < keys.Count && !cts.IsCancellationRequested; i += batchSize)
        {
            // Simulate creating Rectangle and TextBlock for each item
            // In real code this would be: CollectTreemapElements + canvas.Children.Add
            for (int j = i; j < Math.Min(i + batchSize, keys.Count); j++)
            {
                var key = keys[j];
                var item = dict[key];
                // Simulate work of creating UI elements
                _ = item.Size * 2; // Trivial work to simulate
            }

            processed = Math.Min(i + batchSize, keys.Count);
            int pct = (int)(processed * 100.0 / keys.Count);
            progressWindow.ReportProgress(pct);
            progressWindow.Report($"Rendered {processed:n0} / {keys.Count:n0}");

            // Yield to allow progress window updates (simulates await Task.Delay(1) in real code)
            await Task.Delay(1);
        }

        sw.Stop();
        _output.WriteLine($"Simulated rendering of {dict.Count} items in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Cancelled: {cts.IsCancellationRequested}");
    }
}
