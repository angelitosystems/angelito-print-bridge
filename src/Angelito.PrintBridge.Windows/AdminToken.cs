// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using Angelito.PrintBridge.Core.Security;

namespace Angelito.PrintBridge.Windows;

/// <summary>
/// Local admin credential (desktop UI / CLI). Stored DPAPI-protected for the current Windows user, so web pages
/// and other Windows users cannot obtain it.
/// </summary>
public static class AdminToken
{
    private static readonly byte[] Entropy = "Angelito.PrintBridge.Admin.v1"u8.ToArray();

    public static string LoadOrCreate(string path)
    {
        if (File.Exists(path)) return Read(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var token = Tokens.New();
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            f.Write(blob);
            return token;
        }
        catch (IOException) when (File.Exists(path)) { return Read(path); } // created concurrently
    }

    public static string Read(string path) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
}
