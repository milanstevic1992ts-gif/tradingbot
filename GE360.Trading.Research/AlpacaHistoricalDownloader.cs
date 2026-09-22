using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GE360.Trading.Features;

namespace GE360.Trading.Research;

public sealed record AlpacaHistoricalDownloadRequest(
    IReadOnlyList<string> Symbols,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Feed = "iex",
    string Adjustment = "all",
    int PageLimit = 10_000,
    TimeSpan? MinimumDelayBetweenPages = null)
{
    public TimeSpan EffectiveMinimumDelay =>
        MinimumDelayBetweenPages ?? TimeSpan.FromMilliseconds(350);
}

public sealed record AlpacaHistoricalDownloadResult(
    IReadOnlyList<IntradayBar> Bars,
    int PageCount,
    string Feed,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public sealed class AlpacaHistoricalDownloader
{
    public const string ApiKeyEnvironmentVariable = "APCA_API_KEY_ID";
    public const string ApiSecretEnvironmentVariable = "APCA_API_SECRET_KEY";
    private const string Endpoint = "https://data.alpaca.markets/v2/stocks/bars";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _apiSecret;

    public AlpacaHistoricalDownloader(
        HttpClient httpClient,
        string apiKey,
        string apiSecret)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Alpaca API key is required.", nameof(apiKey))
            : apiKey;
        _apiSecret = string.IsNullOrWhiteSpace(apiSecret)
            ? throw new ArgumentException("Alpaca API secret is required.", nameof(apiSecret))
            : apiSecret;
    }

    public static AlpacaHistoricalDownloader FromEnvironment(HttpClient? httpClient = null)
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var secret = Environment.GetEnvironmentVariable(ApiSecretEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"Alpaca credentials are required in {ApiKeyEnvironmentVariable} and {ApiSecretEnvironmentVariable}. " +
                "Credentials must never be committed to the repository.");
        }

        return new AlpacaHistoricalDownloader(
            httpClient ?? new HttpClient(),
            key,
            secret);
    }

    public async Task<AlpacaHistoricalDownloadResult> DownloadAsync(
        AlpacaHistoricalDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var bars = new List<IntradayBar>();
        string? pageToken = null;
        var pages = 0;

        do
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Get,
                BuildRequestUri(request, pageToken));

            message.Headers.Add("APCA-API-KEY-ID", _apiKey);
            message.Headers.Add("APCA-API-SECRET-KEY", _apiSecret);
            message.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Alpaca historical data request failed with HTTP {(int)response.StatusCode}. " +
                    "Response body was not persisted because it may contain account-specific details.");
            }

            var page = AlpacaHistoricalBarPageParser.Parse(payload);
            bars.AddRange(page.Bars);
            pageToken = page.NextPageToken;
            pages++;

            if (!string.IsNullOrWhiteSpace(pageToken) &&
                request.EffectiveMinimumDelay > TimeSpan.Zero)
            {
                await Task.Delay(
                    request.EffectiveMinimumDelay,
                    cancellationToken);
            }
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return new AlpacaHistoricalDownloadResult(
            bars
                .OrderBy(x => x.UtcTime)
                .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            pages,
            request.Feed,
            request.StartUtc,
            request.EndUtc);
    }

    public static void WriteNeutralCsv(
        string path,
        IReadOnlyCollection<IntradayBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("timestamp_utc,symbol,open,high,low,close,volume");

        foreach (var bar in bars
                     .OrderBy(x => x.UtcTime)
                     .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            writer.Write(bar.UtcTime.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Symbol);
            writer.Write(',');
            writer.Write(bar.Open.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.High.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Low.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Close.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(bar.Volume.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static Uri BuildRequestUri(
        AlpacaHistoricalDownloadRequest request,
        string? pageToken = null)
    {
        ValidateRequest(request);

        var query = new Dictionary<string, string>
        {
            ["symbols"] = string.Join(
                ",",
                request.Symbols
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
            ["timeframe"] = "1Min",
            ["start"] = request.StartUtc
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            ["end"] = request.EndUtc
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            ["limit"] = request.PageLimit.ToString(CultureInfo.InvariantCulture),
            ["adjustment"] = request.Adjustment,
            ["feed"] = request.Feed,
            ["sort"] = "asc"
        };

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            query["page_token"] = pageToken;
        }

        var queryString = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new Uri($"{Endpoint}?{queryString}");
    }

    private static void ValidateRequest(AlpacaHistoricalDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Symbols is null ||
            request.Symbols.Count == 0 ||
            request.Symbols.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one valid symbol is required.",
                nameof(request));
        }

        if (request.StartUtc >= request.EndUtc)
        {
            throw new ArgumentException(
                "StartUtc must be earlier than EndUtc.",
                nameof(request));
        }

        if (request.PageLimit is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Alpaca page limit must be between 1 and 10000.");
        }

        if (!new[] { "iex", "sip", "otc", "boats" }
                .Contains(request.Feed, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Unsupported Alpaca stock feed.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Adjustment))
        {
            throw new ArgumentException(
                "Adjustment is required.",
                nameof(request));
        }
    }
}

public sealed record AlpacaParsedPage(
    IReadOnlyList<IntradayBar> Bars,
    string? NextPageToken);

public static class AlpacaHistoricalBarPageParser
{
    public static AlpacaParsedPage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Alpaca response is empty.");
        }

        var response = JsonSerializer.Deserialize<AlpacaMultiBarsResponse>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException(
                "Unable to deserialize Alpaca historical bars response.");

        var eastern = ResolveNewYorkTimeZone();
        var bars = new List<IntradayBar>();

        if (response.Bars is not null)
        {
            foreach (var pair in response.Bars)
            {
                foreach (var raw in pair.Value ?? Array.Empty<AlpacaBar>())
                {
                    if (!DateTimeOffset.TryParse(
                            raw.Timestamp,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var timestamp))
                    {
                        throw new InvalidDataException(
                            $"Alpaca returned an invalid bar timestamp '{raw.Timestamp}'.");
                    }

                    var utc = timestamp.UtcDateTime;
                    var local = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);

                    bars.Add(new IntradayBar(
                        pair.Key.ToUpperInvariant(),
                        utc,
                        DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                        raw.Open,
                        raw.High,
                        raw.Low,
                        raw.Close,
                        raw.Volume));
                }
            }
        }

        return new AlpacaParsedPage(
            bars,
            response.NextPageToken);
    }

    private static TimeZoneInfo ResolveNewYorkTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private sealed class AlpacaMultiBarsResponse
    {
        [JsonPropertyName("bars")]
        public Dictionary<string, AlpacaBar[]?>? Bars { get; init; }

        [JsonPropertyName("next_page_token")]
        public string? NextPageToken { get; init; }
    }

    private sealed class AlpacaBar
    {
        [JsonPropertyName("t")]
        public string Timestamp { get; init; } = string.Empty;

        [JsonPropertyName("o")]
        public decimal Open { get; init; }

        [JsonPropertyName("h")]
        public decimal High { get; init; }

        [JsonPropertyName("l")]
        public decimal Low { get; init; }

        [JsonPropertyName("c")]
        public decimal Close { get; init; }

        [JsonPropertyName("v")]
        public decimal Volume { get; init; }
    }
}
