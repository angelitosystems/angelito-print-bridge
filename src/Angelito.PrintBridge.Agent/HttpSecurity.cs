// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Core.Security;
using Microsoft.Extensions.Primitives;

namespace Angelito.PrintBridge.Agent;

/// <summary>Request-level defences applied before routing: Host (DNS rebinding), Origin allow-list, CORS and preflight.</summary>
public static class HttpSecurity
{
    public const string AdminPrefix = "/v1/admin";

    public static IApplicationBuilder UseBridgeSecurity(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.Headers["Cache-Control"] = "no-store";

        if (!OriginPolicy.IsAllowedHost(req.Host.Host))
        {
            await Error(ctx, 421, "invalid_host", "Host no permitido.");
            return;
        }

        var origin = req.Headers.Origin.ToString();
        var isAdmin = req.Path.StartsWithSegments(AdminPrefix);
        if (origin.Length > 0)
        {
            // Browsers always send Origin on CORS/WebSocket; admin endpoints are never reachable from a page.
            if (isAdmin || OriginPolicy.Normalize(origin) is null)
            {
                await Error(ctx, 403, "origin_not_allowed", "Origen no permitido.");
                return;
            }
            res.Headers.AccessControlAllowOrigin = origin;
            res.Headers.Vary = "Origin";
            if (HttpMethods.IsOptions(req.Method) && req.Headers.ContainsKey("Access-Control-Request-Method"))
            {
                res.Headers.AccessControlAllowMethods = "GET, POST, DELETE";
                res.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Idempotency-Key";
                res.Headers.AccessControlMaxAge = "600";
                if (req.Headers["Access-Control-Request-Private-Network"] == "true")
                    res.Headers["Access-Control-Allow-Private-Network"] = "true";
                res.StatusCode = 204;
                return;
            }
        }
        else if (!isAdmin && req.Path != "/v1/hello")
        {
            // Phase 1 only serves browser clients; native clients will pair with a signed app identity (see docs/SECURITY-MODEL.md).
            await Error(ctx, 403, "origin_required", "Se requiere el encabezado Origin.");
            return;
        }
        await next();
    });

    public static Task Error(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new ApiError(new ApiErrorBody(code, message)));
    }

    public static IResult Problem(int status, string code, string message) => Results.Json(new ApiError(new ApiErrorBody(code, message)), statusCode: status);

    public static string? BearerToken(HttpRequest req, string scheme = "Bearer")
    {
        StringValues h = req.Headers.Authorization;
        var v = h.ToString();
        return v.StartsWith(scheme + " ", StringComparison.Ordinal) ? v[(scheme.Length + 1)..].Trim() : null;
    }
}

public sealed record ApiErrorBody(string Code, string Message);
public sealed record ApiError(ApiErrorBody Error);
