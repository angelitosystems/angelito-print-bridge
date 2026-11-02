// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Desktop.Services;
using Angelito.PrintBridge.Desktop.ViewModels;
using Angelito.PrintBridge.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Angelito.PrintBridge.Desktop;

/// <summary>
/// Resident tray app, one per interactive user session. The print agent is a separate per-user process (never a
/// Session 0 service, which could not reach per-user printer connections or show UI); this app starts it, restarts it
/// after a crash and stops it on exit.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);

    private MainWindow? _window;
    private MiniPanel? _panel;
    private MainViewModel? _vm;
    private AgentClient? _client;
    private DispatcherTimer? _timer;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem, _autoStartItem;
    private long _lastTrayClick;
    private DateTimeOffset _lastRestart = DateTimeOffset.MinValue;
    private bool _exiting;
    private readonly CancellationTokenSource _exitToken = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public static void ApplyTheme(int index) =>
        Current!.RequestedThemeVariant = index switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDir = BridgeDefaults.ResolveDataDirectory();
            var settings = DesktopSettings.Load(dataDir);
            ApplyTheme(settings.Theme);
            _client = new AgentClient(BridgeDefaults.ResolvePort(), dataDir);
            _vm = new MainViewModel(_client, settings);
            _vm.PairingRequested += () => ShowMain(MainViewModel.TabHome, bringToFront: true);
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.AgentOnline) or nameof(MainViewModel.AutoStartEnabled)) UpdateTrayMenu();
            };
            _window = new MainWindow { DataContext = _vm };
            _panel = new MiniPanel { DataContext = _vm, OpenMain = ShowMain };
            CreateTray();

            if (desktop.Args?.Contains("--minimized") != true) ShowMain(MainViewModel.TabHome);

            _ = StartAsync();
            ListenForSecondInstance();
            desktop.Exit += (_, _) =>
            {
                // Remove the icon explicitly so no "ghost" icon stays in the notification area.
                if (_tray is not null) _tray.IsVisible = false;
                _tray?.Dispose();
                _client.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        _statusItem = new NativeMenuItem("Agente: comprobando…") { IsEnabled = false };
        _autoStartItem = new NativeMenuItem("Iniciar con Windows") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = _vm!.AutoStartEnabled };
        _autoStartItem.Click += (_, _) => _vm.AutoStartEnabled = !_vm.AutoStartEnabled;

        NativeMenuItem Item(string header, Action action)
        {
            var i = new NativeMenuItem(header);
            i.Click += (_, _) => action();
            return i;
        }

        var menu = new NativeMenu
        {
            Item("Abrir Angelito Print Bridge", () => ShowMain(MainViewModel.TabHome)),
            _statusItem,
            new NativeMenuItemSeparator(),
            Item("Ver impresoras", () => ShowMain(MainViewModel.TabPrinters)),
            Item("Trabajos recientes", () => ShowMain(MainViewModel.TabJobs)),
            Item("Imprimir prueba…", () => ShowMain(MainViewModel.TabPrinters)),
            Item("Configuración", () => ShowMain(MainViewModel.TabSettings)),
            _autoStartItem,
            Item("Acerca de", () => ShowMain(MainViewModel.TabAbout)),
            new NativeMenuItemSeparator(),
            Item("Salir", () => _ = ExitAsync()),
        };

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AngelitoPrintBridge.Desktop/Assets/icon.ico"))),
            ToolTipText = "Angelito Print Bridge",
            Menu = menu,
        };
        _tray.Clicked += (_, _) => OnTrayClicked();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void UpdateTrayMenu()
    {
        if (_statusItem is null || _vm is null) return;
        _statusItem.Header = _vm.AgentOnline ? "Agente: activo" : "Agente: detenido";
        _autoStartItem!.IsChecked = _vm.AutoStartEnabled;
        _tray!.ToolTipText = _vm.AgentOnline ? "Angelito Print Bridge — activo" : "Angelito Print Bridge — detenido";
    }

    /// <summary>Left click toggles the mini panel; a second click within the system double-click time opens the full window.</summary>
    internal void OnTrayClicked()
    {
        var now = Environment.TickCount64;
        var isDouble = now - _lastTrayClick <= PanelPlacement.DoubleClickMs;
        _lastTrayClick = isDouble ? 0 : now;
        if (isDouble)
        {
            _panel!.HidePanel();
            ShowMain(MainViewModel.TabHome);
            return;
        }
        if (_panel!.IsVisible)
        {
            _panel.HidePanel();
            return;
        }
        // Clicking the icon while the panel is open first deactivates (hides) it; that click means "close", not "reopen".
        if (now - _panel.HiddenByDeactivationAt < 400) return;
        _panel.ShowNear(PanelPlacement.Cursor());
        _ = _vm!.RefreshAsync();
    }

    private async Task StartAsync()
    {
        await AgentProcess.EnsureRunningAsync(_client!);
        await _vm!.RefreshAsync();
        _ = Task.Run(() => ListenToAgentAsync(_exitToken.Token));
        UpdateTrayMenu();
        _timer = new DispatcherTimer(ActiveInterval, DispatcherPriority.Background, async (_, _) => await TickAsync());
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_exiting) return;
        await _vm!.RefreshAsync();
        // Watchdog: restart a crashed agent, at most every 10 s.
        if (!_vm.AgentOnline && DateTimeOffset.UtcNow - _lastRestart > TimeSpan.FromSeconds(10))
        {
            _lastRestart = DateTimeOffset.UtcNow;
            if (await AgentProcess.EnsureRunningAsync(_client!) is null) await _vm.RefreshAsync();
        }
        // Poll slowly while nothing is on screen to keep idle CPU near zero.
        _timer!.Interval = _window!.IsVisible || _panel!.IsVisible ? ActiveInterval : IdleInterval;
    }

    /// <summary>Keeps the push channel to the agent open (reconnects with backoff); polling remains as a fallback.</summary>
    private async Task ListenToAgentAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _client!.ListenAsync(type => Dispatcher.UIThread.InvokeAsync(() => _vm!.RefreshAsync()), ct);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { } // agent stopped/restarting: retry below
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(15, delay.TotalSeconds * 2));
        }
    }

    private void ListenForSecondInstance()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.UIThread.Post(() => ShowMain(MainViewModel.TabHome));
        })
        { IsBackground = true, Name = "single-instance" }.Start();
    }

    private void ShowMain(int tab) => ShowMain(tab, bringToFront: false);

    private void ShowMain(int tab, bool bringToFront)
    {
        if (_window is null || _vm is null) return;
        _vm.SelectedTab = tab;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        if (bringToFront)
        {
            // A pairing request needs the user's attention now (like QZ Tray's prompt): briefly topmost to beat focus rules.
            _window.Topmost = true;
            _window.Topmost = false;
        }
        _window.Activate();
    }

    /// <summary>Exit stops the agent too (it is a per-user process, not a service) but never interrupts a job in progress.</summary>
    private async Task ExitAsync()
    {
        try
        {
            if (await _client!.IsAliveAsync()) await _client.ShutdownAsync(ifIdle: true);
        }
        catch (InvalidOperationException ex)
        {
            _vm!.Message = $"{ex.Message} Vuelva a elegir «Salir» cuando terminen.";
            ShowMain(MainViewModel.TabJobs);
            return;
        }
        catch (AgentUnavailableException) { } // already stopped
        _exiting = true;
        _exitToken.Cancel();
        _timer?.Stop();
        _panel?.Close();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
