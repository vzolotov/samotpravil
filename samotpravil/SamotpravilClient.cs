using System.Globalization;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json;

namespace Samotpravil;

/// <summary>
/// Asynchronous client for the Samotpravil REST API.
/// </summary>
public sealed class SamotpravilClient : IDisposable
{
    private const string DefaultApiUrl = "https://api.samotpravil.com/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a Samotpravil API client.
    /// </summary>
    /// <param name="apiKey">API key sent in the Authorization header.</param>
    /// <param name="httpClient">Optional HTTP client, useful for custom transport and testing.</param>
    /// <param name="baseAddress">Optional API base address. Defaults to the Samotpravil production API.</param>
    public SamotpravilClient(string apiKey, HttpClient? httpClient = null, Uri? baseAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var configuredAddress = baseAddress ?? new Uri(DefaultApiUrl);
        if (!configuredAddress.IsAbsoluteUri ||
            (configuredAddress.Scheme != Uri.UriSchemeHttp && configuredAddress.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(configuredAddress.Query) ||
            !string.IsNullOrEmpty(configuredAddress.Fragment))
        {
            throw new ArgumentException("The API base address must be an absolute HTTP or HTTPS URI without a query or fragment.", nameof(baseAddress));
        }

        var address = configuredAddress.AbsoluteUri;
        _baseAddress = new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : address + "/", UriKind.Absolute);
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        ApiKey = apiKey;
    }

    /// <summary>
    /// Gets the API key used by this client.
    /// </summary>
    public string ApiKey { get; }

    /// <summary>
    /// Sends an email using the Samotpravil API.
    /// </summary>
    public Task<JsonElement> SendEmailAsync(
        string emailTo,
        string subject,
        string messageText,
        string emailFrom,
        SendEmailOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEmail(emailTo, nameof(emailTo));
        ValidateEmail(emailFrom, nameof(emailFrom));
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(messageText);

        var data = new Dictionary<string, object?>
        {
            ["email_to"] = emailTo,
            ["subject"] = subject,
            ["message_text"] = messageText,
            ["email_from"] = options?.NameFrom is null ? emailFrom : $"{options.NameFrom} <{emailFrom}>"
        };

        if (options is not null)
        {
            AddIfNotNull(data, "params", options.Params);
            AddIfNotNull(data, "x_track_id", options.XTrackId);
            AddIfNotNull(data, "track_open", options.TrackOpen);
            AddIfNotNull(data, "track_click", options.TrackClick);
            AddIfNotNull(data, "track_domain", options.TrackDomain);
            AddIfNotNull(data, "check_stop_list", options.CheckStopList);
            AddIfNotNull(data, "check_local_stop_list", options.CheckLocalStopList);
            AddIfNotNull(data, "domain_for_dkim", options.DomainForDkim);
            AddIfNotNull(data, "headers", options.Headers);
        }

        return SendAsync(HttpMethod.Post, "api/v2/mail/send", data, cancellationToken);
    }

    /// <summary>
    /// Gets the status of sent emails.
    /// </summary>
    public Task<JsonElement> GetStatusAsync(
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/v2/issue/status", parameters, cancellationToken);

    /// <summary>
    /// Gets sent-email statistics for the specified date range.
    /// </summary>
    public Task<JsonElement> GetStatisticsAsync(
        string dateFrom,
        string dateTo,
        int limit = 100,
        int cursorNext = 0,
        CancellationToken cancellationToken = default) =>
        GetDateReportAsync("api/v2/issue/statistics", dateFrom, dateTo, limit, cursorNext, cancellationToken);

    /// <summary>
    /// Gets the non-delivery report for the specified date range.
    /// </summary>
    public Task<JsonElement> GetNonDeliveryByDateAsync(
        string dateFrom,
        string dateTo,
        int limit = 100,
        int cursorNext = 0,
        CancellationToken cancellationToken = default) =>
        GetDateReportAsync("api/v2/blist/report/non-delivery", dateFrom, dateTo, limit, cursorNext, cancellationToken);

    /// <summary>
    /// Gets the FBL report for the specified date range.
    /// </summary>
    public Task<JsonElement> GetFblReportByDateAsync(
        string dateFrom,
        string dateTo,
        int limit = 100,
        int cursorNext = 0,
        CancellationToken cancellationToken = default) =>
        GetDateReportAsync("api/v2/blist/report/fbl", dateFrom, dateTo, limit, cursorNext, cancellationToken);

    /// <summary>
    /// Searches for an email address in the stop list.
    /// </summary>
    public Task<JsonElement> StopListSearchAsync(string email, CancellationToken cancellationToken = default)
    {
        ValidateEmail(email, nameof(email));
        return SendAsync(HttpMethod.Get, "api/v2/stop-list/search",
            new Dictionary<string, object?> { ["email"] = email }, cancellationToken);
    }

    /// <summary>
    /// Adds an email address to the stop list.
    /// </summary>
    public Task<JsonElement> StopListAddAsync(string email, string domain, CancellationToken cancellationToken = default)
    {
        ValidateEmail(email, nameof(email));
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return SendDomainEmailAsync("api/v2/stop-list/add", email, domain, cancellationToken);
    }

    /// <summary>
    /// Removes an email address from the stop list.
    /// </summary>
    public Task<JsonElement> StopListRemoveAsync(string email, string domain, CancellationToken cancellationToken = default)
    {
        ValidateEmail(email, nameof(email));
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return SendDomainEmailAsync("api/v2/stop-list/remove", email, domain, cancellationToken);
    }

    /// <summary>
    /// Gets all allowed sender domains.
    /// </summary>
    public Task<JsonElement> GetDomainsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/v2/blist/domains", null, cancellationToken);

    /// <summary>
    /// Adds a domain to the allowed sender domains.
    /// </summary>
    public Task<JsonElement> DomainAddAsync(string domain, CancellationToken cancellationToken = default) =>
        SendDomainAsync("api/v2/blist/domains/add", domain, cancellationToken);

    /// <summary>
    /// Removes a domain from the allowed sender domains.
    /// </summary>
    public Task<JsonElement> DomainRemoveAsync(string domain, CancellationToken cancellationToken = default) =>
        SendDomainAsync("api/v2/blist/domains/remove", domain, cancellationToken);

    /// <summary>
    /// Checks verification status for a domain.
    /// </summary>
    public Task<JsonElement> DomainCheckVerificationAsync(string domain, CancellationToken cancellationToken = default) =>
        SendDomainAsync("api/v2/blist/domains/verify", domain, cancellationToken);

    /// <summary>
    /// Disposes the HTTP client only when it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private Task<JsonElement> GetDateReportAsync(
        string endpoint,
        string dateFrom,
        string dateTo,
        int limit,
        int cursorNext,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dateFrom);
        ArgumentException.ThrowIfNullOrWhiteSpace(dateTo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(cursorNext);

        return SendAsync(HttpMethod.Get, endpoint, new Dictionary<string, object?>
        {
            ["date_from"] = dateFrom,
            ["date_to"] = dateTo,
            ["limit"] = limit,
            ["cursor_next"] = cursorNext
        }, cancellationToken);
    }

    private Task<JsonElement> SendDomainEmailAsync(
        string endpoint,
        string email,
        string domain,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, endpoint, new Dictionary<string, object?>
        {
            ["email"] = email,
            ["mail_from"] = $"info@{domain}"
        }, cancellationToken);

    private Task<JsonElement> SendDomainAsync(string endpoint, string domain, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return SendAsync(HttpMethod.Post, endpoint, new Dictionary<string, object?> { ["domain"] = domain }, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string endpoint,
        IReadOnlyDictionary<string, object?>? data,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(_baseAddress, endpoint);
        if (method == HttpMethod.Get && data is { Count: > 0 })
        {
            var query = string.Join("&", data
                .Where(pair => pair.Value is not null)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(ToQueryValue(pair.Value!))}"));
            if (query.Length > 0)
            {
                var builder = new UriBuilder(requestUri) { Query = query };
                requestUri = builder.Uri;
            }
        }

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", ApiKey);
        if (method != HttpMethod.Get && data is not null)
        {
            request.Content = JsonContent.Create(data, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The Samotpravil API response does not contain a string status.");
        }

        if (!string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
        {
            var message = document.RootElement.TryGetProperty("message", out var messageElement) &&
                          messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;
            throw new SamotpravilApiException(message ?? "The Samotpravil API returned an unsuccessful response.");
        }

        return document.RootElement.Clone();
    }

    private static string ToQueryValue(object value) => value switch
    {
        string text => text,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText(),
        _ => JsonSerializer.Serialize(value, JsonOptions)
    };

    private static void ValidateEmail(string email, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid email address.", parameterName);
        }
    }

    private static void AddIfNotNull(IDictionary<string, object?> data, string key, object? value)
    {
        if (value is not null)
        {
            data[key] = value;
        }
    }
}

/// <summary>
/// Optional fields supported when sending an email.
/// </summary>
public sealed class SendEmailOptions
{
    /// <summary>Optional sender display name.</summary>
    public string? NameFrom { get; init; }

    /// <summary>Template parameters.</summary>
    public IReadOnlyDictionary<string, object?>? Params { get; init; }

    /// <summary>Tracking identifier.</summary>
    public string? XTrackId { get; init; }

    /// <summary>Whether to track opens.</summary>
    public bool? TrackOpen { get; init; }

    /// <summary>Whether to track clicks.</summary>
    public bool? TrackClick { get; init; }

    /// <summary>Tracking domain.</summary>
    public string? TrackDomain { get; init; }

    /// <summary>Whether to check the global stop list.</summary>
    public bool? CheckStopList { get; init; }

    /// <summary>Whether to check the local stop list.</summary>
    public bool? CheckLocalStopList { get; init; }

    /// <summary>Domain used for the DKIM signature.</summary>
    public string? DomainForDkim { get; init; }

    /// <summary>Additional email headers.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Thrown when the Samotpravil API returns a non-OK application status.
/// </summary>
public sealed class SamotpravilApiException : Exception
{
    /// <summary>
    /// Creates an exception for an unsuccessful API response.
    /// </summary>
    public SamotpravilApiException(string message) : base(message)
    {
    }
}
