// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Windows;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Angelito.PrintBridge.Agent;

/// <summary>
/// Local administration from the same Windows user that runs the agent (until the desktop UI ships).
/// Usage: AngelitoPrintBridge requests | approve CODE | deny CODE | grants | revoke GRANT_ID | jobs | audit | shutdown [--if-idle]
///        | certs | cert-generate ORIGIN NAME OUT_DIR [PERMS,...] | cert-revoke FINGERPRINT
/// Exit codes: 0 ok, 1 error, 2 agent never started, 3 agent not running, 4 jobs active (shutdown --if-idle).
/// </summary>
public static class AdminCli
{
    public static readonly HashSet<string> Commands = ["requests", "approve", "deny", "grants", "revoke", "jobs", "audit", "shutdown", "certs", "cert-generate", "cert-revoke"];

    private static Task<HttpResponseMessage> GenerateCertificate(HttpClient http, string[] args)
    {
        var perms = args.Length > 4 ? args[4].Split(',', StringSplitOptions.RemoveEmptyEntries) : ["printers:read", "print:document"];
        return http.PostAsJsonAsync("certificates/generate", new { origin = args[1], name = args[2], permissions = perms });
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var o = config.GetSection(BridgeOptions.Section).Get<BridgeOptions>() ?? new BridgeOptions();
        if (!File.Exists(o.AdminTokenPath))
        {
            Console.Error.WriteLine("El agente aún no se ha iniciado en esta cuenta de Windows (falta el token de administración).");
            return 2;
        }
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{o.Port}/v1/admin/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Admin", AdminToken.Read(o.AdminTokenPath));

        var arg = args.Length > 1 ? Uri.EscapeDataString(args[1]) : null;
        HttpResponseMessage res;
        try
        {
            res = args[0] switch
            {
                "requests" => await http.GetAsync("pair-requests"),
                "approve" when arg is not null => await http.PostAsync($"pair-requests/{arg}/approve", null),
                "deny" when arg is not null => await http.PostAsync($"pair-requests/{arg}/deny", null),
                "grants" => await http.GetAsync("grants"),
                "revoke" when arg is not null => await http.DeleteAsync($"grants/{arg}"),
                "jobs" => await http.GetAsync("jobs"),
                "audit" => await http.GetAsync("audit"),
                "certs" => await http.GetAsync("certificates"),
                "cert-revoke" when arg is not null => await http.DeleteAsync($"certificates/{arg}"),
                "cert-generate" when args.Length >= 4 => await GenerateCertificate(http, args),
                "shutdown" => await http.PostAsync(args.Contains("--if-idle") ? "shutdown?ifIdle=true" : "shutdown", null),
                _ => throw new ArgumentException($"Falta el argumento para '{args[0]}'."),
            };
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine($"No se pudo conectar con el agente en el puerto {o.Port}. ¿Está en ejecución?");
            return 3;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        if (res.RequestMessage?.RequestUri?.AbsolutePath.EndsWith("/generate") == true && res.IsSuccessStatusCode)
        {
            var json = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Directory.CreateDirectory(args[3]);
            File.WriteAllText(Path.Combine(args[3], "certificate.pem"), json.GetProperty("certificatePem").GetString());
            File.WriteAllText(Path.Combine(args[3], "private-key.pem"), json.GetProperty("privateKeyPem").GetString());
            Console.WriteLine(json.GetProperty("certificate").GetRawText());
            Console.Error.WriteLine($"Guardado en {Path.GetFullPath(args[3])}. La clave privada va SOLO en el servidor de su aplicación.");
            return 0;
        }
        Console.WriteLine(await res.Content.ReadAsStringAsync());
        if (res.StatusCode == System.Net.HttpStatusCode.Conflict && args[0] == "shutdown") return 4;
        return res.IsSuccessStatusCode ? 0 : 1;
    }
}
