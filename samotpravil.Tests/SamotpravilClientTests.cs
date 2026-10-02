using System.Net;
using System.Text;
using System.Text.Json;
using Samotpravil;
using Xunit;

namespace Samotpravil.Tests;

public sealed class SamotpravilClientTests
{
    private static readonly Uri TestApiUri = new("https://api.example.test/root/");

    [Fact]
    public async Task SendEmailAsync_SendsExpectedJsonAndAuthorization()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await client.SendEmailAsync(
            "to@example.com",
            "Hello",
            "Message",
            "from@example.com",
            new SendEmailOptions
            {
                NameFrom = "Sam",
                TrackOpen = false,
                XTrackId = "track-1",
                Params = new Dictionary<string, object?> { ["name"] = "Alex" }
            });

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri(TestApiUri, "api/v2/mail/send"), handler.RequestUri);
        Assert.Equal("test-key", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("to@example.com", json.RootElement.GetProperty("email_to").GetString());
        Assert.Equal("Sam <from@example.com>", json.RootElement.GetProperty("email_from").GetString());
        Assert.False(json.RootElement.GetProperty("track_open").GetBoolean());
        Assert.Equal("track-1", json.RootElement.GetProperty("x_track_id").GetString());
        Assert.Equal("Alex", json.RootElement.GetProperty("params").GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetStatusAsync_EncodesParametersAsQueryAndSkipsNullValues()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await client.GetStatusAsync(new Dictionary<string, object?>
        {
            ["email"] = "a+b@example.com",
            ["issue_id"] = 123,
            ["unused"] = null
        });

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            new Uri(TestApiUri, "api/v2/issue/status?email=a%2Bb%40example.com&issue_id=123"),
            handler.RequestUri);
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData("GetStatisticsAsync", "api/v2/issue/statistics")]
    [InlineData("GetNonDeliveryByDateAsync", "api/v2/blist/report/non-delivery")]
    [InlineData("GetFblReportByDateAsync", "api/v2/blist/report/fbl")]
    public async Task DateReportMethods_UseExpectedEndpointAndPagination(string methodName, string endpoint)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        switch (methodName)
        {
            case "GetStatisticsAsync":
                await client.GetStatisticsAsync("2025-01-01", "2025-01-31", 25, 4);
                break;
            case "GetNonDeliveryByDateAsync":
                await client.GetNonDeliveryByDateAsync("2025-01-01", "2025-01-31", 25, 4);
                break;
            case "GetFblReportByDateAsync":
                await client.GetFblReportByDateAsync("2025-01-01", "2025-01-31", 25, 4);
                break;
            default:
                throw new InvalidOperationException($"Unexpected method: {methodName}");
        }

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(new Uri(TestApiUri, $"{endpoint}?date_from=2025-01-01&date_to=2025-01-31&limit=25&cursor_next=4"), handler.RequestUri);
    }

    [Fact]
    public async Task DomainAndStopListMethods_UseExpectedEndpointsAndPayloads()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await client.StopListSearchAsync("blocked@example.com");
        Assert.Equal(new Uri(TestApiUri, "api/v2/stop-list/search?email=blocked%40example.com"), handler.RequestUri);

        await client.StopListAddAsync("blocked@example.com", "example.com");
        AssertPostPayload(handler, "api/v2/stop-list/add", "mail_from", "info@example.com");

        await client.StopListRemoveAsync("blocked@example.com", "example.com");
        AssertPostPayload(handler, "api/v2/stop-list/remove", "mail_from", "info@example.com");

        await client.GetDomainsAsync();
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(new Uri(TestApiUri, "api/v2/blist/domains"), handler.RequestUri);

        await client.DomainAddAsync("example.com");
        AssertPostPayload(handler, "api/v2/blist/domains/add", "domain", "example.com");

        await client.DomainRemoveAsync("example.com");
        AssertPostPayload(handler, "api/v2/blist/domains/remove", "domain", "example.com");

        await client.DomainCheckVerificationAsync("example.com");
        AssertPostPayload(handler, "api/v2/blist/domains/verify", "domain", "example.com");
    }

    [Fact]
    public async Task SendEmailAsync_RejectsInvalidEmailBeforeSending()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendEmailAsync("not-an-email", "Subject", "Body", "from@example.com"));

        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task Request_ThrowsApiExceptionWhenApiStatusIsNotOk()
    {
        using var handler = new RecordingHandler("""{"status":"ERROR","message":"Rejected"}""");
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        var exception = await Assert.ThrowsAsync<SamotpravilApiException>(() => client.GetDomainsAsync());

        Assert.Equal("Rejected", exception.Message);
    }

    [Fact]
    public async Task Request_ThrowsHttpRequestExceptionForHttpFailure()
    {
        using var handler = new RecordingHandler("""{"message":"Unavailable"}""", HttpStatusCode.ServiceUnavailable);
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetDomainsAsync());
    }

    [Fact]
    public async Task Request_ThrowsInvalidDataExceptionForMalformedApiResponseShape()
    {
        using var handler = new RecordingHandler("""{"message":"No status"}""");
        using var httpClient = new HttpClient(handler);
        using var client = new SamotpravilClient("test-key", httpClient, TestApiUri);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetDomainsAsync());
    }

    private static void AssertPostPayload(RecordingHandler handler, string endpoint, string property, string expected)
    {
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri(TestApiUri, endpoint), handler.RequestUri);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(expected, json.RootElement.GetProperty(property).GetString());
    }

    private sealed class RecordingHandler(string responseBody = """{"status":"OK"}""", HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.GetValues("Authorization").Single();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
