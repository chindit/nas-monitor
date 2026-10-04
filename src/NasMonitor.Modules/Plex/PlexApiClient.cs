using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Media;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Configuration;
using NasMonitor.Modules.Media;

namespace NasMonitor.Modules.Plex;

public sealed class PlexApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PlexOptions _options;
    private readonly PlexParser _parser;

    public PlexApiClient(HttpClient httpClient, IOptions<PlexOptions> options, PlexParser parser)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _parser = parser;
    }

    public async Task<(string? Name, string? Version, IReadOnlyList<MediaSessionSnapshot> Sessions)> ReadAsync(CancellationToken cancellationToken)
    {
        var identityTask = SendAsync("identity", cancellationToken);
        var sessionsTask = SendAsync("status/sessions", cancellationToken);
        await Task.WhenAll(identityTask, sessionsTask).ConfigureAwait(false);
        var identity = _parser.ParseIdentity(identityTask.Result.Content, identityTask.Result.IsJson);
        var sessions = _options.ShowSessionDetails
            ? _parser.ParseSessions(sessionsTask.Result.Content, sessionsTask.Result.IsJson)
            : [];
        return (identity.Name, identity.Version, sessions);
    }

    private async Task<(string Content, bool IsJson)> SendAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Token", _options.ApiToken);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "nas-monitor");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "nas-monitor");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0");

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ModuleCollectionException("api_unauthorized", "Plex rejected the configured API token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ModuleCollectionException("api_unreachable", "Plex did not return a successful response.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                         content.AsSpan().TrimStart().StartsWith("{".AsSpan(), StringComparison.Ordinal);
            return (content, isJson);
        }
        catch (ModuleCollectionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModuleCollectionException("api_unreachable", "Plex did not respond before the timeout.");
        }
        catch (HttpRequestException exception)
        {
            throw new ModuleCollectionException("api_unreachable", "Plex could not be reached.", exception);
        }
    }
}

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed class PlexParser
{
    public (string? Name, string? Version) ParseIdentity(string content, bool isJson)
    {
        try
        {
            if (isJson)
            {
                using var document = JsonDocument.Parse(content);
                var container = MediaJson.Object(document.RootElement, "MediaContainer") ?? document.RootElement;
                return (MediaJson.String(container, "friendlyName") ?? MediaJson.String(container, "name"), MediaJson.String(container, "version"));
            }

            var root = XDocument.Parse(content).Root ?? throw InvalidResponse();
            return ((string?)root.Attribute("friendlyName") ?? (string?)root.Attribute("name"), (string?)root.Attribute("version"));
        }
        catch (Exception exception) when (exception is JsonException or global::System.Xml.XmlException)
        {
            throw InvalidResponse(exception);
        }
    }

    public IReadOnlyList<MediaSessionSnapshot> ParseSessions(string content, bool isJson)
    {
        try
        {
            return isJson ? ParseJsonSessions(content) : ParseXmlSessions(content);
        }
        catch (Exception exception) when (exception is JsonException or global::System.Xml.XmlException)
        {
            throw InvalidResponse(exception);
        }
    }

    private static MediaSessionSnapshot[] ParseJsonSessions(string content)
    {
        using var document = JsonDocument.Parse(content);
        var container = MediaJson.Object(document.RootElement, "MediaContainer") ?? document.RootElement;
        var items = new List<JsonElement>();
        foreach (var type in new[] { "Video", "Track", "Photo" })
        {
            items.AddRange(MediaJson.Array(container, type));
        }

        return items.Select(ParseJsonSession).ToArray();
    }

    private static MediaSessionSnapshot ParseJsonSession(JsonElement item)
    {
        var user = MediaJson.Object(item, "User");
        var player = MediaJson.Object(item, "Player");
        var session = MediaJson.Object(item, "Session");
        var transcode = MediaJson.Object(item, "TranscodeSession");
        var media = MediaJson.Array(item, "Media").FirstOrDefault();
        var duration = MediaJson.Int64(item, "duration");
        var position = MediaJson.Int64(item, "viewOffset");
        var decisionText = transcode is { } transcodeValue
            ? MediaJson.String(transcodeValue, "videoDecision") ?? MediaJson.String(transcodeValue, "audioDecision")
            : MediaJson.String(media, "videoDecision") ?? MediaJson.String(media, "audioDecision");

        return new MediaSessionSnapshot(
            session is { } sessionValue ? MediaJson.String(sessionValue, "id") ?? MediaJson.String(item, "sessionKey") ?? string.Empty : MediaJson.String(item, "sessionKey") ?? string.Empty,
            user is { } userValue ? MediaJson.String(userValue, "title") : null,
            MediaJson.String(item, "type") ?? "unknown",
            MediaJson.String(item, "title") ?? "Unknown",
            MediaJson.String(item, "parentTitle"),
            MediaJson.String(item, "grandparentTitle"),
            ToInt(MediaJson.Int64(item, "parentIndex")),
            ToInt(MediaJson.Int64(item, "index")),
            player is { } playerValue ? MediaJson.String(playerValue, "product") ?? MediaJson.String(playerValue, "title") : null,
            player is { } deviceValue ? MediaJson.String(deviceValue, "title") ?? MediaJson.String(deviceValue, "platform") : null,
            ParseState(player is { } stateValue ? MediaJson.String(stateValue, "state") : null),
            position,
            duration,
            MediaJson.Progress(position, duration),
            ParseDecision(decisionText),
            MediaJson.String(media, "videoResolution"),
            MediaJson.String(media, "videoCodec"),
            MediaJson.String(media, "audioCodec"),
            MediaJson.Int64(media, "bitrate") is { } kilobits ? kilobits * 1000 : null,
            transcode is { } progressValue ? MediaJson.Decimal(progressValue, "progress") : null,
            transcode is { } speedValue ? MediaJson.Decimal(speedValue, "speed") : null);
    }

    private static MediaSessionSnapshot[] ParseXmlSessions(string content)
    {
        var root = XDocument.Parse(content).Root ?? throw InvalidResponse();
        return root.Elements().Where(element => element.Name.LocalName is "Video" or "Track" or "Photo").Select(item =>
        {
            var user = item.Elements().FirstOrDefault(element => element.Name.LocalName == "User");
            var player = item.Elements().FirstOrDefault(element => element.Name.LocalName == "Player");
            var session = item.Elements().FirstOrDefault(element => element.Name.LocalName == "Session");
            var transcode = item.Elements().FirstOrDefault(element => element.Name.LocalName == "TranscodeSession");
            var media = item.Elements().FirstOrDefault(element => element.Name.LocalName == "Media");
            var duration = LongAttribute(item, "duration");
            var position = LongAttribute(item, "viewOffset");
            return new MediaSessionSnapshot(
                (string?)session?.Attribute("id") ?? (string?)item.Attribute("sessionKey") ?? string.Empty,
                (string?)user?.Attribute("title"),
                (string?)item.Attribute("type") ?? "unknown",
                (string?)item.Attribute("title") ?? "Unknown",
                (string?)item.Attribute("parentTitle"),
                (string?)item.Attribute("grandparentTitle"),
                ToInt(LongAttribute(item, "parentIndex")),
                ToInt(LongAttribute(item, "index")),
                (string?)player?.Attribute("product") ?? (string?)player?.Attribute("title"),
                (string?)player?.Attribute("title") ?? (string?)player?.Attribute("platform"),
                ParseState((string?)player?.Attribute("state")),
                position,
                duration,
                MediaJson.Progress(position, duration),
                ParseDecision((string?)transcode?.Attribute("videoDecision") ?? (string?)media?.Attribute("videoDecision")),
                (string?)media?.Attribute("videoResolution"),
                (string?)media?.Attribute("videoCodec"),
                (string?)media?.Attribute("audioCodec"),
                LongAttribute(media, "bitrate") is { } kilobits ? kilobits * 1000 : null,
                DecimalAttribute(transcode, "progress"),
                DecimalAttribute(transcode, "speed"));
        }).ToArray();
    }

    private static PlaybackState ParseState(string? value) => value?.ToLowerInvariant() switch
    {
        "playing" => PlaybackState.Playing,
        "paused" => PlaybackState.Paused,
        "buffering" => PlaybackState.Buffering,
        _ => PlaybackState.Unknown
    };

    private static PlaybackDecision ParseDecision(string? value) => value?.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "directplay" or "copy" => PlaybackDecision.DirectPlay,
        "directstream" or "remux" => PlaybackDecision.DirectStream,
        "transcode" => PlaybackDecision.Transcode,
        _ => PlaybackDecision.Unknown
    };

    private static int? ToInt(long? value) => value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    private static long? LongAttribute(XElement? element, string name) => long.TryParse((string?)element?.Attribute(name), out var value) ? value : null;
    private static decimal? DecimalAttribute(XElement? element, string name) => decimal.TryParse((string?)element?.Attribute(name), global::System.Globalization.NumberStyles.Number, global::System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    private static ModuleCollectionException InvalidResponse(Exception? inner = null) => new("api_invalid_response", "Plex returned an invalid response.", inner);
}
