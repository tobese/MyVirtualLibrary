// Mac Catalyst application entry point.
// UIKit requires a static Main that calls UIApplication.Main, passing the
// App class (which the Uno runtime adapts into a UIApplicationDelegate).
// Uno.Sdk does not auto-generate this for the maccatalyst TFM the way it does
// for WebAssembly (which has its own UnoPlatformHostBuilder-based Program.cs).

namespace VirtualLibrary.Client;

public static class EntryPoint
{
    public static void Main(string[] args)
    {
        App.InitializeLogging();
        global::UIKit.UIApplication.Main(args, null, typeof(App));
    }
}
