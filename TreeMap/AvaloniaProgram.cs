using Avalonia;

namespace TreeMap;

public static class AvaloniaProgram
{
    // Minimal Avalonia bootstrap — used when launching the Avalonia UI.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
