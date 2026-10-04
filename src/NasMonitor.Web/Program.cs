using System.Text.Json;
using System.Text.Json.Serialization;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Infrastructure;
using NasMonitor.Core.Services;
using NasMonitor.Modules;
using NasMonitor.Web.Components;
using NasMonitor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("/etc/nas-monitor/nas-monitor.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddNasMonitorModules();
builder.Services.AddScoped<IDashboardCoordinator, DashboardCoordinator>();
builder.Services.AddSingleton<DashboardCollectionGate>();
builder.Services.AddProblemDetails();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseAntiforgery();
app.MapStaticAssets();

app.MapMethods("/health/live", [HttpMethods.Get, HttpMethods.Head], () => Results.Ok(new { status = "ok" }));
app.MapMethods("/api/v1/dashboard", [HttpMethods.Get, HttpMethods.Head], async (
    HttpContext context,
    DashboardCollectionGate gate,
    IDashboardCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!await gate.TryEnterAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
    {
        return Results.Problem(
            title: "Collection already in progress",
            detail: "Another dashboard collection is still running.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    try
    {
        return Results.Ok(await coordinator.CollectAsync(cancellationToken).ConfigureAwait(false));
    }
    finally
    {
        gate.Exit();
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(NasMonitor.Web.Client.Pages.Dashboard).Assembly);

app.Run();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
}

public partial class Program;
