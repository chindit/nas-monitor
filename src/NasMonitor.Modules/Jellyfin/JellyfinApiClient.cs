using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Media;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Configuration;
using NasMonitor.Modules.Media;

namespace NasMonitor.Modules.Jellyfin;

public sealed class JellyfinApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JellyfinOptions _options;
    private readonly JellyfinParser _parser;

    public JellyfinApiClient(HttpClient httpClient, IOptions<JellyfinOptions> options, JellyfinParser parser)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _parser = parser;
    }

    public async Task<(string? Name, string? Version, IReadOnlyList<MediaSessionSnapshot> Sessions)> ReadAsync(CancellationToken cancellationToken)
    {
        var infoTask = SendAsync("System/Info", cancellationToken);
        var sessionsTask = SendAsync("Sessions", cancellationToken);
        await Task.WhenAll(infoTask, sessionsTask).ConfigureAwait(false);
        var identity = _parser.ParseSystemInfo(infoTask.Result);
        return (
            identity.Name,
            identity.Version,
            _options.ShowSessionDetails ? _parser.ParseSessions(sessionsTask.Result) : []);
    }

    private async Task<string> SendAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"MediaBrowser Client=\"nas-monitor\", Device=\"NAS\", DeviceId=\"nas-monitor\", Version=\"{version}\", Token=\"{_options.ApiToken}\"");

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ModuleCollectionException("api_unauthorized", "Jellyfin rejected the configured API token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ModuleCollectionException("api_unreachable", "Jellyfin did not return a successful response.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ModuleCollectionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModuleCollectionException("api_unreachable", "Jellyfin did not respond before the timeout.");
        }
        catch (HttpRequestException exception)
        {
            throw new ModuleCollectionException("api_unreachable", "Jellyfin could not be reached.", exception);
        }
    }
}

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed class JellyfinParser
{
    public (string? Name, string? Version) ParseSystemInfo(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return (MediaJson.String(document.RootElement, "ServerName"), MediaJson.String(document.RootElement, "Version"));
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    public IReadOnlyList<MediaSessionSnapshot> ParseSessions(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse();
            }

            var sessions = new List<MediaSessionSnapshot>();
            foreach (var session in document.RootElement.EnumerateArray())
            {
                var item = MediaJson.Object(session, "NowPlayingItem");
                if (item is null)
                {
                    continue;
                }

                var playState = MediaJson.Object(session, "PlayState");
                var transcode = MediaJson.Object(session, "TranscodingInfo");
                var positionTicks = playState is { } play ? MediaJson.Int64(play, "PositionTicks") : null;
                var durationTicks = MediaJson.Int64(item.Value, "RunTimeTicks");
                var position = TicksToMilliseconds(positionTicks);
                var duration = TicksToMilliseconds(durationTicks);
                var width = transcode is { } video ? MediaJson.Int64(video, "Width") : null;
                var height = transcode is { } videoHeight ? MediaJson.Int64(videoHeight, "Height") : null;
                var resolution = width is > 0 && height is > 0 ? $"{width}x{height}" : null;
                sessions.Add(new MediaSessionSnapshot(
                    MediaJson.String(session, "Id") ?? string.Empty,
                    MediaJson.String(session, "UserName"),
                    MediaJson.String(item.Value, "Type") ?? MediaJson.String(item.Value, "MediaType") ?? "unknown",
                    MediaJson.String(item.Value, "Name") ?? "Unknown",
                    MediaJson.String(item.Value, "SeasonName"),
                    MediaJson.String(item.Value, "SeriesName"),
                    ToInt(MediaJson.Int64(item.Value, "ParentIndexNumber")),
                    ToInt(MediaJson.Int64(item.Value, "IndexNumber")),
                    MediaJson.String(session, "Client"),
                    MediaJson.String(session, "DeviceName"),
                    playState is { } state && MediaJson.Boolean(state, "IsPaused") == true ? PlaybackState.Paused : PlaybackState.Playing,
                    position,
                    duration,
                    MediaJson.Progress(position, duration),
                    ParseDecision(playState is { } methodState ? MediaJson.String(methodState, "PlayMethod") : null),
                    resolution,
                    transcode is { } videoCodec ? MediaJson.String(videoCodec, "VideoCodec") : null,
                    transcode is { } audioCodec ? MediaJson.String(audioCodec, "AudioCodec") : null,
                    transcode is { } bitrate ? MediaJson.Int64(bitrate, "Bitrate") : null,
                    transcode is { } progress ? MediaJson.Decimal(progress, "CompletionPercentage") : null,
                    null));
            }

            return sessions;
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    private static long? TicksToMilliseconds(long? ticks) => ticks is null ? null : ticks / TimeSpan.TicksPerMillisecond;

    private static PlaybackDecision ParseDecision(string? value) => value?.ToLowerInvariant() switch
    {
        "directplay" => PlaybackDecision.DirectPlay,
        "directstream" or "remux" => PlaybackDecision.DirectStream,
        "transcode" => PlaybackDecision.Transcode,
        _ => PlaybackDecision.Unknown
    };

    private static int? ToInt(long? value) => value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;

    private static ModuleCollectionException InvalidResponse(Exception? inner = null) =>
        new("api_invalid_response", "Jellyfin returned an invalid response.", inner);
}
