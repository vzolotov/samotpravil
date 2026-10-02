# Samotpravil for .NET

An asynchronous C# client for the [Samotpravil](https://samotpravil.ru) email API. API details are available in the [Samotpravil REST API documentation](https://documentation.samotpravil.ru/).

## Requirements

- .NET 8.0 or .NET 10.0
- A Samotpravil API key

## Install

```sh
dotnet add package Zolotov.Samotpravil
```

## Usage

```csharp
using samotpravil;

using var client = new SamotpravilClient("your-api-key");

var result = await client.SendEmailAsync(
    emailTo: "recipient@example.com",
    subject: "Hello",
    messageText: "<p>Your message</p>",
    emailFrom: "info@example.com",
    options: new SendEmailOptions
    {
        NameFrom = "Example",
        TrackOpen = true,
        Params = new Dictionary<string, object?>
        {
            ["first_name"] = "Alex"
        }
    });

Console.WriteLine(result);
```

The client supports cancellation tokens on every API call. You can inject an `HttpClient` and optionally override the API base address for testing or custom environments:

```csharp
using var client = new SamotpravilClient(
    "your-api-key",
    httpClient: httpClient,
    baseAddress: new Uri("https://api.samotpravil.com/"));
```

An injected `HttpClient` remains owned by the caller. A client-created `HttpClient` is disposed when `SamotpravilClient` is disposed.

## Available operations

All methods are asynchronous and return the decoded API response as a `JsonElement`.

| Method | Operation |
| --- | --- |
| `SendEmailAsync` | Send an email, with optional tracking, template and header fields |
| `GetStatusAsync` | Get status for sent email(s) |
| `GetStatisticsAsync` | Get sent-email statistics by date range |
| `GetNonDeliveryByDateAsync` | Get the non-delivery report by date range |
| `GetFblReportByDateAsync` | Get the FBL report by date range |
| `StopListSearchAsync` | Search for an email address in the stop list |
| `StopListAddAsync` | Add an email address to the stop list |
| `StopListRemoveAsync` | Remove an email address from the stop list |
| `GetDomainsAsync` | List allowed sender domains |
| `DomainAddAsync` | Add an allowed sender domain |
| `DomainRemoveAsync` | Remove an allowed sender domain |
| `DomainCheckVerificationAsync` | Check domain verification |

Date-range methods accept `dateFrom` and `dateTo` in `YYYY-MM-DD` format and have optional `limit` (default `100`) and `cursorNext` (default `0`) pagination arguments.

GET arguments are encoded as URL query parameters. POST arguments are sent as JSON. Non-success HTTP responses throw `HttpRequestException`; API responses whose `status` is not `OK` throw `SamotpravilApiException`.

## Build and test

```sh
dotnet test
dotnet pack samotpravil/samotpravil.csproj -c Release
```

## License

MIT
