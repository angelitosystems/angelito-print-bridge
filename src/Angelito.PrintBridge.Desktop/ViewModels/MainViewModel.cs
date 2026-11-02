// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Diagnostics;
using Angelito.PrintBridge.Desktop.Services;
using Angelito.PrintBridge.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angelito.PrintBridge.Desktop.ViewModels;

public sealed class PairRow(PairRequestInfo r)
{
    public PairRequestInfo Request => r;
    public string Title => $"{r.AppName} quiere imprimir en este equipo";
    public string Origin => r.Origin;
    public string Code => r.Code;
    public string AppId => r.AppId;
    public string PermissionsText => string.Join(" · ", r.Permissions.Select(Labels.Permission));
    public bool AsksRaw => r.Permissions.Contains("print:raw");
}

public sealed class GrantRow(GrantInfo g)
{
    public GrantInfo Grant => g;
    public string AppName => g.AppName;
    public string Origin => g.Origin;
    public string AppId => g.AppId;
    public string PermissionsText => string.Join(" · ", g.Permissions.Select(Labels.Permission));
    public string CreatedText => $"Autorizada el {g.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
}

public sealed class CertRow(CertificateInfo c)
{
    public CertificateInfo Certificate => c;
    public string Name => c.Name;
    public string Origin => c.Origin;
    public string PermissionsText => string.Join(" · ", c.Permissions.Select(Labels.Permission));
    public string Details => $"Huella SHA-256: {c.Fingerprint[..16]}…  ·  Vence: {c.NotAfter.ToLocalTime():dd/MM/yyyy}";
    public bool Expired => c.NotAfter < DateTimeOffset.UtcNow;
}

public sealed class PrinterRow(PrinterItem p)
{
    public PrinterItem Printer => p;
    public string Name => p.Name;
    public bool IsDefault => p.IsDefault;
    public bool CanBeDefault => !p.IsDefault && !p.IsVirtual;
    public string StatusText => string.Join(", ", p.Status.Select(Labels.PrinterStatus));
    public bool HasProblem => !p.Status.SequenceEqual(["ready"]);
    public string Badges => string.Join("  ", new[] { p.IsDefault ? "Predeterminada" : null, p.IsVirtual ? "Virtual" : null }.Where(b => b is not null));
    public string CapabilitiesText => p.Capabilities is { } c
        ? $"Dúplex: {(c.Duplex ? "sí" : "no")} · Color: {(c.Color ? "sí" : "no")} · Copias máx.: {c.MaxCopies} · {c.PaperSizes.Length} tamaños de papel"
        : "El driver no informó sus capacidades";
}

public sealed class JobRow(AdminJob j)
{
    public AdminJob Source => j;
    public Guid Id => j.Job.JobId;
    public string Title => $"{Labels.Kind(j.Job.Kind)} → {j.Job.PrinterId}";
    public string AppName => j.AppName;
    public string StateText => Labels.JobState(j.Job.State);
    public bool IsError => j.Job.State is "failed" or "unknown";
    public bool CanCancel => j.Job.State is "queued" or "rendering" or "submitted";
    public string When => j.Job.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string ErrorText => j.Job.Error is { } e ? $"{e.Message} ({e.Code})" : "";
    public string Details =>
        $"ID: {j.Job.JobId}\nAplicación: {j.AppName}\nTipo: {j.Job.Kind}\nImpresora: {j.Job.PrinterId}\nEstado: {j.Job.State}\n" +
        $"Intentos: {j.Job.Attempts}\nConfirmación: {(j.Job.Confirmation == "spooler" ? "salió de la cola de Windows (no confirma el papel)" : "ninguna")}\n" +
        $"Spooler: {j.Job.SpoolerStatus ?? "-"}\nCreado: {j.Job.CreatedAt.ToLocalTime():G}\nActualizado: {j.Job.UpdatedAt.ToLocalTime():G}" +
        (j.Job.Error is { } e2 ? $"\nError: {e2.Code} — {e2.Message}" : "");
}

public static class Labels
{
    public static string Permission(string p) => p switch
    {
        "printers:read" => "Ver impresoras",
        "print:document" => "Imprimir PDF e imágenes",
        "print:raw" => "Comandos directos (ESC/POS, ZPL)",
        _ => p,
    };

    public static string JobState(string s) => s switch
    {
        "queued" => "En cola",
        "rendering" => "Procesando",
        "submitted" => "En la cola de Windows",
        "completed" => "Completado",
        "failed" => "Error",
        "cancelled" => "Cancelado",
        "unknown" => "Resultado desconocido",
        _ => s,
    };

    public static string Kind(string k) => k switch { "pdf" => "PDF", "image" => "Imagen", "raw" => "RAW", _ => k };

    public static string PrinterStatus(string s) => s switch
    {
        "ready" => "Lista",
        "paused" => "En pausa",
        "error" => "Error",
        "paperJam" => "Atasco de papel",
        "paperOut" => "Sin papel",
        "offline" => "Sin conexión",
        "notAvailable" => "No disponible",
        "noToner" => "Sin tóner/tinta",
        "doorOpen" => "Tapa abierta",
        _ => s,
    };
}

public partial class MainViewModel : ObservableObject
{
    private readonly AgentClient _client;
    private readonly DesktopSettings _settings;
    private readonly HashSet<string> _seenPairCodes = [];
    private DateTimeOffset _printersLoadedAt = DateTimeOffset.MinValue;
    private bool _refreshing;

    public MainViewModel(AgentClient client, DesktopSettings settings)
    {
        _client = client;
        _settings = settings;
        _autoStart = AutoStart.IsEnabled();
        _themeIndex = settings.Theme;
    }

    /// <summary>Raised when a new pairing request arrives, so the app can surface its window.</summary>
    public event Action? PairingRequested;

    public ObservableCollection<PairRow> PairRequests { get; } = [];
    public ObservableCollection<GrantRow> Grants { get; } = [];
    public ObservableCollection<PrinterRow> Printers { get; } = [];
    public ObservableCollection<JobRow> Jobs { get; } = [];
    public ObservableCollection<JobRow> RecentJobs { get; } = [];

    [ObservableProperty] public partial bool AgentOnline { get; set; }
    [ObservableProperty] public partial string AgentStatusText { get; set; } = "Conectando con el agente…";
    [ObservableProperty] public partial string Version { get; set; } = "-";
    [ObservableProperty] public partial string Endpoint { get; set; } = "-";
    [ObservableProperty] public partial string StartedText { get; set; } = "-";
    [ObservableProperty] public partial int PrinterCount { get; set; }
    [ObservableProperty] public partial int QueuedJobs { get; set; }
    [ObservableProperty] public partial int FailedJobs { get; set; }
    [ObservableProperty] public partial int SelectedTab { get; set; }
    [ObservableProperty] public partial JobRow? SelectedJob { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial bool ShowOnlyErrors { get; set; }
    [ObservableProperty] public partial string DefaultPrinterName { get; set; } = "—";
    [ObservableProperty] public partial string LastJobText { get; set; } = "Aún no hay trabajos";

    public const int TabHome = 0, TabPrinters = 1, TabJobs = 2, TabApps = 3, TabCertificates = 4, TabSettings = 5, TabAbout = 6;

    public ObservableCollection<CertRow> Certificates { get; } = [];
    public bool HasNoCertificates => Certificates.Count == 0;
    [ObservableProperty] public partial string NewCertOrigin { get; set; } = "https://";
    [ObservableProperty] public partial string NewCertName { get; set; } = "";
    [ObservableProperty] public partial bool NewCertPrinters { get; set; } = true;
    [ObservableProperty] public partial bool NewCertDocument { get; set; } = true;
    [ObservableProperty] public partial bool NewCertRaw { get; set; }

    /// <summary>Supplied by the window: shows a file picker and returns the chosen PEM text (null if cancelled).</summary>
    public Func<Task<string?>>? PickCertificateFile { get; set; }

    private string[] NewCertPermissions() =>
        new[] { NewCertPrinters ? "printers:read" : null, NewCertDocument ? "print:document" : null, NewCertRaw ? "print:raw" : null }
            .OfType<string>().ToArray();

    public bool HasPairRequests => PairRequests.Count > 0;
    public bool HasNoGrants => Grants.Count == 0;
    public bool HasNoJobs => Jobs.Count == 0;
    public string DataDirectory => _client.DataDirectory;
    public string LogsDirectory => Path.Combine(_client.DataDirectory, "logs");
    public static string ProductVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private bool _autoStart;
    public bool AutoStartEnabled
    {
        get => _autoStart;
        set
        {
            if (_autoStart == value) return;
            try
            {
                AutoStart.Set(value);
                _autoStart = value;
                Message = value ? "Se iniciará con Windows." : "Ya no se iniciará con Windows.";
            }
            catch (Exception ex) { Message = $"No se pudo cambiar el inicio automático: {ex.Message}"; }
            OnPropertyChanged();
        }
    }

    private int _themeIndex;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (!SetProperty(ref _themeIndex, value)) return;
            _settings.Theme = value;
            _settings.Save();
            App.ApplyTheme(value);
        }
    }

    partial void OnShowOnlyErrorsChanged(bool value) => _ = RefreshJobsAsync();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!await _client.IsAliveAsync())
            {
                SetOffline("El agente no está en ejecución.");
                return;
            }
            var status = await _client.StatusAsync();
            AgentOnline = true;
            AgentStatusText = "Agente en ejecución";
            Version = status.Version;
            Endpoint = $"127.0.0.1:{status.Port} (solo este equipo)";
            StartedText = status.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            QueuedJobs = status.QueuedJobs;

            var pairs = await _client.PairRequestsAsync();
            Sync(PairRequests, pairs.Select(p => new PairRow(p)), r => r.Code);
            OnPropertyChanged(nameof(HasPairRequests));
            if (pairs.Any(p => _seenPairCodes.Add(p.Code))) PairingRequested?.Invoke();

            Sync(Grants, (await _client.GrantsAsync()).Select(g => new GrantRow(g)), g => g.Grant.Id);
            OnPropertyChanged(nameof(HasNoGrants));
            Sync(Certificates, (await _client.CertificatesAsync()).Select(c => new CertRow(c)), c => c.Certificate.Fingerprint);
            OnPropertyChanged(nameof(HasNoCertificates));
            await RefreshJobsAsync();

            if (DateTimeOffset.UtcNow - _printersLoadedAt > TimeSpan.FromSeconds(30)) await LoadPrintersAsync();
        }
        catch (AgentUnavailableException ex) { SetOffline(ex.Message); }
        catch (Exception ex) { Message = ex.Message; }
        finally { _refreshing = false; }
    }

    private async Task RefreshJobsAsync()
    {
        var all = await _client.JobsAsync();
        FailedJobs = all.Count(j => j.Job.State is "failed" or "unknown");
        var shown = ShowOnlyErrors ? all.Where(j => j.Job.State is "failed" or "unknown") : all;
        var selected = SelectedJob?.Id;
        Sync(Jobs, shown.Select(j => new JobRow(j)), j => $"{j.Id}|{j.Source.Job.UpdatedAt:O}");
        Sync(RecentJobs, all.Take(5).Select(j => new JobRow(j)), j => $"{j.Id}|{j.Source.Job.UpdatedAt:O}");
        LastJobText = all.FirstOrDefault() is { } last
            ? $"{Labels.JobState(last.Job.State)} · {last.Job.CreatedAt.ToLocalTime():dd/MM HH:mm}"
            : "Aún no hay trabajos";
        if (selected is not null) SelectedJob = Jobs.FirstOrDefault(j => j.Id == selected);
        OnPropertyChanged(nameof(HasNoJobs));
    }

    private async Task LoadPrintersAsync()
    {
        var printers = await _client.PrintersAsync();
        Sync(Printers, printers.Select(p => new PrinterRow(p)), p => $"{p.Printer.Id}|{string.Join(',', p.Printer.Status)}|{p.IsDefault}");
        PrinterCount = printers.Count(p => !p.IsVirtual);
        DefaultPrinterName = printers.FirstOrDefault(p => p.IsDefault)?.Name ?? "Ninguna";
        _printersLoadedAt = DateTimeOffset.UtcNow;
    }

    private void SetOffline(string text)
    {
        AgentOnline = false;
        AgentStatusText = text;
    }

    /// <summary>Keyed in-place sync: keeps unchanged items (and the user's selection/focus) stable between refreshes.</summary>
    internal static void Sync<T>(ObservableCollection<T> target, IEnumerable<T> source, Func<T, string> key)
    {
        var desired = source.ToList();
        var keys = desired.Select(key).ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keys.Contains(key(target[i]))) target.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var k = key(desired[i]);
            var existing = -1;
            for (var j = i; j < target.Count; j++)
                if (key(target[j]) == k) { existing = j; break; }
            if (existing == -1) target.Insert(i, desired[i]);
            else if (existing != i) target.Move(existing, i);
        }
    }

    private async Task Run(Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null) Message = success;
        }
        catch (Exception ex) { Message = ex.Message; }
        await RefreshAsync();
    }

    [RelayCommand]
    private Task Approve(PairRow row) => Run(() => _client.ApproveAsync(row.Code), $"{row.Request.AppName} ({row.Origin}) autorizada.");

    [RelayCommand]
    private Task Deny(PairRow row) => Run(() => _client.DenyAsync(row.Code), "Solicitud rechazada.");

    [RelayCommand]
    private Task Revoke(GrantRow row) => Run(() => _client.RevokeAsync(row.Grant.Id), $"Se revocó el acceso de {row.AppName}.");

    [RelayCommand]
    private Task CancelJob(JobRow row) => Run(() => _client.CancelJobAsync(row.Id), "Cancelación solicitada.");

    [RelayCommand]
    private Task TestPrint(PrinterRow row) => Run(async () =>
    {
        await _client.TestPrintAsync(row.Printer.Id);
        SelectedTab = 2;
    }, $"Página de prueba enviada a {row.Name}.");

    [RelayCommand]
    private Task GenerateCertificate() => Run(async () =>
    {
        var generated = await _client.GenerateCertificateAsync(NewCertOrigin.Trim(), NewCertName.Trim(), NewCertPermissions());
        var folder = CertificateFiles.Save(generated);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        Message = $"Certificado creado y autorizado para {generated.Certificate.Origin}. Archivos en {folder}. La clave privada va SOLO en su servidor.";
        NewCertName = "";
    });

    [RelayCommand]
    private Task ImportCertificate() => Run(async () =>
    {
        if (PickCertificateFile is null || await PickCertificateFile() is not { } pem) return;
        var imported = await _client.ImportCertificateAsync(pem, NewCertOrigin.Trim(), NewCertName.Trim(), NewCertPermissions());
        Message = $"Certificado «{imported.Name}» autorizado para {imported.Origin}.";
    });

    [RelayCommand]
    private Task RevokeCertificate(CertRow row) =>
        Run(() => _client.RevokeCertificateAsync(row.Certificate.Fingerprint), $"Certificado «{row.Name}» revocado; sus sesiones se cerraron.");

    [RelayCommand]
    private Task SetDefaultPrinter(PrinterRow row) => Run(async () =>
    {
        WindowsPrinters.SetDefault(row.Printer.Id);
        _printersLoadedAt = DateTimeOffset.MinValue;
        await LoadPrintersAsync();
        Message = WindowsPrinters.WindowsManagesDefault()
            ? $"{row.Name} es ahora la predeterminada. Aviso: Windows está configurado para administrar la impresora predeterminada y podría cambiarla a la última usada (Configuración de Windows › Impresoras)."
            : $"{row.Name} es ahora la impresora predeterminada de Windows.";
    });

    [RelayCommand]
    private Task RefreshPrinters() => Run(async () =>
    {
        _printersLoadedAt = DateTimeOffset.MinValue;
        await LoadPrintersAsync();
    });

    [RelayCommand]
    private Task StartAgent() => Run(async () =>
    {
        if (await AgentProcess.EnsureRunningAsync(_client) is { } problem) throw new InvalidOperationException(problem);
    }, "Agente iniciado.");

    [RelayCommand]
    private Task ExportDiagnostics() => Run(async () =>
    {
        var path = await DiagnosticsExport.ExportAsync(_client);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        Message = $"Diagnóstico guardado en {path}";
    });

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogsDirectory}\"") { UseShellExecute = false });
    }

    [RelayCommand]
    private void DismissMessage() => Message = null;
}
