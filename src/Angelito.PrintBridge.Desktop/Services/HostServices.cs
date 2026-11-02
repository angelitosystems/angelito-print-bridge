// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;

namespace Angelito.PrintBridge.Desktop.Services;

/// <summary>Starts the agent (a separate process, so a UI crash never stops printing) when it is not running.</summary>
public static class AgentProcess
{
    public const string ExeName = "AngelitoPrintBridge.exe";

    /// <summary>Agent next to the desktop app (installed layout) or in the dev build output.</summary>
    public static string? Locate()
    {
        var local = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(local)) return local;
        if (Environment.GetEnvironmentVariable("APB_AGENT_EXE") is { Length: > 0 } env && File.Exists(env)) return env;
        // Dev layout: src/<Desktop>/bin/<Config>/<Tfm>/ → src/<Agent>/bin/<Config>/<Tfm>/
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var src = tfm.Parent?.Parent?.Parent?.Parent;
        if (src is null || tfm.Parent is null) return null;
        var dev = Path.Combine(src.FullName, "Angelito.PrintBridge.Agent", "bin", tfm.Parent.Name, tfm.Name, ExeName);
        return File.Exists(dev) ? dev : null;
    }

    public static async Task<bool> EnsureRunningAsync(AgentClient client, CancellationToken ct = default)
    {
        if (await client.IsAliveAsync(ct)) return true;
        var exe = Locate();
        if (exe is null) return false;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            if (await client.IsAliveAsync(ct)) return true;
        }
        return false;
    }
}

/// <summary>Per-user start with Windows (HKCU Run). Only changed when the user toggles it.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AngelitoPrintBridge";

    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && v.Contains(Path.GetFileName(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Builds a support bundle: agent status, recent jobs/audit and logs. No tokens and no document content.</summary>
public static class DiagnosticsExport
{
    public static async Task<string> ExportAsync(AgentClient client)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Angelito Print Bridge");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        async Task Add(string name, Func<Task<object>> get)
        {
            object content;
            try { content = await get(); }
            catch (Exception ex) { content = new { error = ex.Message }; }
            await using var s = zip.CreateEntry(name).Open();
            await JsonSerializer.SerializeAsync(s, content, json);
        }

        await Add("system.json", () => Task.FromResult<object>(new
        {
            os = Environment.OSVersion.VersionString,
            arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            dotnet = Environment.Version.ToString(),
            desktopVersion = typeof(DiagnosticsExport).Assembly.GetName().Version?.ToString(),
        }));
        await Add("status.json", async () => await client.StatusAsync());
        await Add("printers.json", async () => await client.PrintersAsync());
        await Add("jobs.json", async () => await client.JobsAsync(500));
        await Add("audit.json", async () => await client.AuditAsync(1000));

        var logs = Path.Combine(client.DataDirectory, "logs");
        if (Directory.Exists(logs))
            foreach (var file in Directory.GetFiles(logs, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(7))
            {
                // Log files are open for writing by the agent: copy with shared read access.
                await using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var dst = zip.CreateEntry("logs/" + Path.GetFileName(file)).Open();
                await src.CopyToAsync(dst);
            }
        return path;
    }
}
