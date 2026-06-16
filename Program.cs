using System;
using System.Threading.Tasks;
using Avalonia;

namespace FIXSniff;

public class Program {
    public static void Main(string[] args) {
        try {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        } catch (TaskCanceledException) {
            // Ignore D-Bus cleanup exceptions on Linux
        } catch (Exception ex) when (ex.Message.Contains("DBus")) {
            // Ignore D-Bus related exceptions during shutdown
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
    ;
}
