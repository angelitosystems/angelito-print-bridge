// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Avalonia;

namespace Angelito.PrintBridge.Desktop;

internal static class Program
{
    internal const string ShowEventName = @"Local\AngelitoPrintBridge.Desktop.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        // Single instance per Windows session: a second launch just brings the existing window forward.
        using var mutex = new Mutex(true, @"Local\AngelitoPrintBridge.Desktop", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
