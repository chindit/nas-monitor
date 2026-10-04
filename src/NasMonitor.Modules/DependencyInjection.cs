using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Configuration;
using NasMonitor.Modules.Jellyfin;
using NasMonitor.Modules.Media;
using NasMonitor.Modules.Plex;
using NasMonitor.Modules.Smart;
using NasMonitor.Modules.Storage;
using NasMonitor.Modules.System;
using NasMonitor.Modules.Zfs;

namespace NasMonitor.Modules;

public static class DependencyInjection
{
    public static IServiceCollection AddNasMonitorModules(this IServiceCollection services)
    {
        AddValidatedOptions<SystemModuleOptions, SystemOptionsValidator>(services, SystemModuleOptions.SectionName);
        AddValidatedOptions<StorageOptions, StorageOptionsValidator>(services, StorageOptions.SectionName);
        AddValidatedOptions<ZfsOptions, ZfsOptionsValidator>(services, ZfsOptions.SectionName);
        AddValidatedOptions<SmartOptions, SmartOptionsValidator>(services, SmartOptions.SectionName);
        AddValidatedOptions<PlexOptions, PlexOptionsValidator>(services, PlexOptions.SectionName);
        AddValidatedOptions<JellyfinOptions, JellyfinOptionsValidator>(services, JellyfinOptions.SectionName);

        services.AddSingleton<SystemParser>();
        services.AddSingleton<StorageParser>();
        services.AddSingleton<ZfsParser>();
        services.AddSingleton<SmartParser>();
        services.AddSingleton<PlexParser>();
        services.AddSingleton<JellyfinParser>();
        services.AddSingleton<IServiceStateReader, ServiceStateReader>();

        services.AddHttpClient<PlexApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<PlexOptions>>().Value;
            client.BaseAddress = SafeBaseAddress(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });
        services.AddHttpClient<JellyfinApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<JellyfinOptions>>().Value;
            client.BaseAddress = SafeBaseAddress(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddScoped<ISystemMonitoringModule, SystemModule>();
        services.AddScoped<IStorageMonitoringModule, StorageModule>();
        services.AddScoped<IZfsMonitoringModule, ZfsModule>();
        services.AddScoped<ISmartMonitoringModule, SmartModule>();
        services.AddScoped<IPlexMonitoringModule, PlexModule>();
        services.AddScoped<IJellyfinMonitoringModule, JellyfinModule>();
        return services;
    }

    private static void AddValidatedOptions<TOptions, TValidator>(IServiceCollection services, string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateOnStart();
    }

    private static string AppendSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private static Uri SafeBaseAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback
            ? new Uri(AppendSlash(value))
            : new Uri("http://127.0.0.1/");
}
