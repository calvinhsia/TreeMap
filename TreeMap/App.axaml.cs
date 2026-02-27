using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TreeMap;

public partial class App : Application
{
    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            // Log full details so the debugger / output window shows the inner cause
            Debug.WriteLine("Avalonia XAML load failed in App.Initialize(): " + ex.ToString());
            if (ex.InnerException != null)
                Debug.WriteLine("Inner exception: " + ex.InnerException.ToString());

            // Rethrow so the usual exception handling / debugger breakpoints still occur
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
