using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NasMonitor.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DashboardApiClient>();
await builder.Build().RunAsync();

public partial class Program;
