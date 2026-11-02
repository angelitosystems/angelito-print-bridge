// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Desktop.Services;
using Angelito.PrintBridge.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angelito.PrintBridge.Desktop.Views;

/// <summary>Compact flyout shown from the tray icon. Hides itself when it loses focus (click outside) or on Escape.</summary>
public partial class MiniPanel : Window
{
    public MiniPanel()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!IsVisible) return;
            HiddenByDeactivationAt = Environment.TickCount64;
            HidePanel();
        };
    }

    /// <summary>Opens the full window on a tab. Set by the app.</summary>
    public Action<int>? OpenMain { get; set; }

    /// <summary>When the panel last hid itself because the user clicked elsewhere (used to make a tray click toggle).</summary>
    public long HiddenByDeactivationAt { get; private set; }

    public void ShowNear(PixelPoint cursor)
    {
        Opacity = 0;
        Show();
        // Size is known only after the first layout; position on the monitor under the cursor.
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
        if (screen is not null)
        {
            var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
            Position = PanelPlacement.Compute(screen.Bounds, screen.WorkingArea, cursor, size, screen.Scaling);
        }
        Activate();
        Opacity = 1;
    }

    public void HidePanel()
    {
        Opacity = 0;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HidePanel();
        base.OnKeyDown(e);
    }

    private void Go(int tab)
    {
        HidePanel();
        OpenMain?.Invoke(tab);
    }

    private void OnOpenHome(object? sender, RoutedEventArgs e) => Go(MainViewModel.TabHome);
    // Printing on paper needs a deliberate printer choice: open the printers list instead of guessing the default.
    private void OnTestPrint(object? sender, RoutedEventArgs e) => Go(MainViewModel.TabPrinters);
    private void OnJobs(object? sender, RoutedEventArgs e) => Go(MainViewModel.TabJobs);
    private void OnSettings(object? sender, RoutedEventArgs e) => Go(MainViewModel.TabSettings);
}
