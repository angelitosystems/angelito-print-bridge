// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

namespace Angelito.PrintBridge.Core.Domain;

/// <summary>Defaults shared by the agent, the desktop app and the CLI. Overridable with Bridge__Port / Bridge__DataDirectory.</summary>
public static class BridgeDefaults
{
    public const int Port = 41730;

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AngelitoSystems", "PrintBridge");

    public static int ResolvePort() => int.TryParse(Environment.GetEnvironmentVariable("Bridge__Port"), out var p) ? p : Port;

    public static string ResolveDataDirectory() => Environment.GetEnvironmentVariable("Bridge__DataDirectory") is { Length: > 0 } d ? d : DataDirectory;

    public static string AdminTokenPath(string dataDirectory) => Path.Combine(dataDirectory, "admin.token");
}
