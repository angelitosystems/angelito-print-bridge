// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Angelito.PrintBridge.Agent;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Desktop.ViewModels;
using Angelito.PrintBridge.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Angelito.PrintBridge.Tests;

public class AdminApiTests(AgentFactory factory) : IClassFixture<AgentFactory>
{
    private HttpClient Admin()
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Admin", factory.Services.GetRequiredKeyedService<string>("admin-token"));
        return c;
    }

    [Fact]
    public async Task Status_and_printers_are_available_to_the_desktop_app()
    {
        var status = await Admin().GetFromJsonAsync<JsonElement>("/v1/admin/status");
        Assert.Equal(factory.Dir.Path, status.GetProperty("dataDirectory").GetString());
        var printers = await Admin().GetFromJsonAsync<JsonElement>("/v1/admin/printers");
        Assert.Equal("Thermal", printers[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Test_page_is_a_real_job_owned_by_local_admin_and_cancellable()
    {
        var started = new TaskCompletionSource();
        factory.Driver.Submit = async (r, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); };
        try
        {
            var res = await Admin().PostAsJsonAsync("/v1/admin/printers/test", new { printerId = "Thermal" });
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // While a job is rendering, an installer's safe shutdown must refuse.
            Assert.Equal(HttpStatusCode.Conflict, (await Admin().PostAsync("/v1/admin/shutdown?ifIdle=true", null)).StatusCode);

            var jobs = await Admin().GetFromJsonAsync<JsonElement>("/v1/admin/jobs");
            var row = jobs.EnumerateArray().First(j => j.GetProperty("job").GetProperty("jobId").GetString() == id);
            Assert.Equal("Página de prueba", row.GetProperty("appName").GetString());

            Assert.Equal(HttpStatusCode.OK, (await Admin().PostAsync($"/v1/admin/jobs/{id}/cancel", null)).StatusCode);
            var state = "";
            for (var i = 0; i < 50 && state != "cancelled"; i++)
            {
                await Task.Delay(100);
                state = (await Admin().GetFromJsonAsync<JsonElement>("/v1/admin/jobs")).EnumerateArray()
                    .First(j => j.GetProperty("job").GetProperty("jobId").GetString() == id).GetProperty("job").GetProperty("state").GetString()!;
            }
            Assert.Equal("cancelled", state);
        }
        finally
        {
            factory.Driver.Submit = (r, _) => { r.OnSpoolStarting(); return Task.CompletedTask; };
        }
    }

    [Fact]
    public async Task Test_page_rejects_unknown_printer() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Admin().PostAsJsonAsync("/v1/admin/printers/test", new { printerId = "Nope" })).StatusCode);

    [Fact]
    public async Task Local_admin_owner_can_never_be_used_as_a_session()
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("Origin", "https://app.example.com");
        var res = await c.PostAsJsonAsync("/v1/session", new { grantToken = Endpoints.LocalAdminGrant });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

public class TestPageTests
{
    [Fact]
    public async Task Generated_test_page_is_valid_for_the_windows_pdf_engine()
    {
        var pdf = TestPage.Build("Impresora (prueba) \\ ñ 日本", "1.2.3", DateTimeOffset.Now);
        using var source = await PdfPageSource.LoadAsync(pdf);
        Assert.Equal(1, source.Count);
        using var bmp = source.Render(0, 72);
        Assert.True(bmp.Width > 500);
    }
}

public class ViewModelTests
{
    [Fact]
    public void Sync_keeps_existing_items_and_applies_order_inserts_and_removals()
    {
        var target = new ObservableCollection<string> { "a", "b", "c" };
        var original = target[1];
        MainViewModel.Sync(target, ["d", "b", "a"], x => x);
        Assert.Equal(["d", "b", "a"], target);
        Assert.Same(original, target[1]);
    }

    [Theory]
    [InlineData("completed", "Completado")]
    [InlineData("unknown", "Resultado desconocido")]
    [InlineData("print:raw", "Comandos directos (ESC/POS, ZPL)")]
    public void Labels_are_spanish(string value, string expected) =>
        Assert.Equal(expected, value.Contains(':') ? Labels.Permission(value) : Labels.JobState(value));
}
