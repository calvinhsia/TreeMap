using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TreeMap;

/// <summary>
/// Progress window for showing progress during long operations.
/// Supports multi-phase operations (e.g., scanning then rendering).
/// 
/// NOTE: Unlike WPF, Avalonia uses a singleton Dispatcher.UIThread, so this cannot
/// truly run on a separate UI thread. Progress updates work because the caller must 
/// yield (await Task.Delay) periodically to allow the dispatcher to process messages.
/// </summary>
public class ProgressWindow : IDisposable, IProgress<string>
{
    private readonly string _title;
    private readonly CancellationTokenSource? _cts;
    private Window? _window;
    private ProgressBar? _progressBar;
    private TextBlock? _phaseText;
    private TextBlock? _statusText;
    private volatile bool _isDisposed;
    private string _currentPhase = "";
    private int _currentPhasePercent = 0;

    /// <summary>
    /// Returns true if cancellation was requested (Cancel button clicked)
    /// </summary>
    public bool IsCancellationRequested => _cts?.IsCancellationRequested ?? false;

    /// <summary>
    /// CancellationToken that can be used to check for cancellation
    /// </summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Create a progress window.
    /// </summary>
    /// <param name="title">Window title</param>
    /// <param name="cts">If provided, shows Cancel button. Click cancels this CTS.</param>
    public ProgressWindow(string title, CancellationTokenSource? cts = null)
    {
        _title = title;
        _cts = cts;
    }

    /// <summary>
    /// Shows the progress window. Must be called from UI thread.
    /// </summary>
    public async Task ShowAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CreateWindow();
            _window?.Show();
        });
    }

    private void CreateWindow()
    {
        _window = new Window
        {
            Title = _title,
            Width = 450,
            Height = _cts != null ? 160 : 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = Brushes.White,
            Topmost = true
        };

        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions(_cts != null ? "Auto,Auto,Auto,Auto,Auto" : "Auto,Auto,Auto,Auto")
        };

        _phaseText = new TextBlock
        {
            Text = "Initializing...",
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_phaseText, 0);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(_progressBar, 1);

        _statusText = new TextBlock
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        };
        Grid.SetRow(_statusText, 2);

        var elapsedText = new TextBlock
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(elapsedText, 3);

        grid.Children.Add(_phaseText);
        grid.Children.Add(_progressBar);
        grid.Children.Add(_statusText);
        grid.Children.Add(elapsedText);

        // Elapsed time timer
        var stopwatch = Stopwatch.StartNew();
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (s, e) =>
        {
            if (_isDisposed) { timer.Stop(); return; }
            Dispatcher.UIThread.Post(() =>
            {
                if (elapsedText != null && !_isDisposed)
                    elapsedText.Text = $"Elapsed: {stopwatch.Elapsed:mm\\:ss}";
            });
        };
        timer.Start();

        // Cancel button
        if (_cts != null)
        {
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            cancelButton.Click += (s, e) =>
            {
                _cts.Cancel();
                cancelButton.IsEnabled = false;
                cancelButton.Content = "Cancelling...";
            };
            Grid.SetRow(cancelButton, 4);
            grid.Children.Add(cancelButton);
        }

        _window.Content = grid;
        _window.Closed += (s, e) => 
        { 
            _isDisposed = true;
            timer.Stop();
        };
    }

    /// <summary>
    /// Set the current phase name (e.g., "Scanning", "Rendering")
    /// </summary>
    public void SetPhase(string phaseName)
    {
        _currentPhase = phaseName;
        _currentPhasePercent = 0;
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_phaseText != null)
                _phaseText.Text = phaseName;
            if (_progressBar != null)
                _progressBar.Value = 0;
        }, DispatcherPriority.Send);
    }

    /// <summary>
    /// Report progress as a percentage (0-100) within current phase.
    /// </summary>
    public void ReportProgress(int percentage)
    {
        _currentPhasePercent = percentage;
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_progressBar != null)
                _progressBar.Value = percentage;
        }, DispatcherPriority.Send);
    }

    /// <summary>
    /// Report status text (shown below progress bar).
    /// Implements IProgress&lt;string&gt; for compatibility with DiskScanner.
    /// </summary>
    public void Report(string status)
    {
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_statusText != null)
                _statusText.Text = status;
        }, DispatcherPriority.Send);
    }

    /// <summary>
    /// Combined update: phase percentage and status text.
    /// </summary>
    public void Report(int percentage, string status)
    {
        _currentPhasePercent = percentage;
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_progressBar != null)
                _progressBar.Value = percentage;
            if (_statusText != null)
                _statusText.Text = status;
        }, DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Dispatcher.UIThread.Post(() =>
        {
            _window?.Close();
        }, DispatcherPriority.Send);
    }
}
