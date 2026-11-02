// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Angelito.PrintBridge.Desktop.Services;

/// <summary>UI-only preferences, stored next to the agent data (per Windows user).</summary>
public sealed class DesktopSettings
{
    private string _path = "";

    /// <summary>0 = sistema, 1 = claro, 2 = oscuro.</summary>
    public int Theme { get; set; }

    public static DesktopSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "desktop.json");
        DesktopSettings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { s = new(); }
        s._path = path;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this));
        }
        catch (IOException) { } // preference only; never block the UI on it
    }
}
