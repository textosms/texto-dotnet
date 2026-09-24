# Texto .NET SDK

Official .NET SDK for the [Texto](https://texto.com.au) SMS API.
Targets .NET 6+ and .NET Standard 2.0.

## Install

```bash
dotnet add package Texto.Sdk
```

## Quick start

```csharp
using Texto;

using var texto = new TextoClient(Environment.GetEnvironmentVariable("TEXTO_API_KEY")!);

var result = await texto.SendAsync("+61400000000", "Hello from Texto");
Console.WriteLine(result.GetProperty("message_id").GetString());

Console.WriteLine($"{await texto.BalanceAsync()} credits remaining");
```

## Sending to many recipients

```csharp
// Tip: include {{OptOutLink}} in the message to add a unique per-recipient
// opt-out link (texto.au/xxxxxx, 15 chars, always shortened) — recommended
// for Sender ID sends, where recipients can't reply STOP.
await texto.SendBatchAsync(
    new object[]
    {
        new { to = "+61400000000", name = "Ada" },
        new { to = "+61400000001", name = "Grace" },
    },
    "Hi {name}, your appointment is tomorrow.");
```

Plain strings work too when you are not using merge fields.

## Errors

```csharp
try
{
    await texto.SendAsync("+61400000000", "Hello");
}
catch (TextoInsufficientCreditsException ex)
{
    Console.WriteLine($"Need {ex.CreditsRequired}, have {ex.CreditsAvailable}");
}
catch (TextoApiException ex)
{
    Console.WriteLine($"{ex.Status}: {ex.Message}");
}
```

## Sub-accounts

```csharp
var created = await texto.CreateAccountAsync("Acme Pty Ltd", new Dictionary<string, object?>
{
    ["email"] = "ops@acme.com.au",
});

var accountId = created.GetProperty("account").GetProperty("id").GetString()!;
await texto.AllocateCreditsAsync(accountId, 500);
```

## Webhooks

```csharp
var payload = await new StreamReader(Request.Body).ReadToEndAsync();
var signature = Request.Headers["X-Texto-Signature"].ToString();

var evt = Webhooks.ParseEvent(payload, signature, Environment.GetEnvironmentVariable("TEXTO_WEBHOOK_SECRET"));

if (Webhooks.IsInboundEvent(evt))
{
    // handle the reply
}
```

## Configuration

```csharp
using var texto = new TextoClient(
    apiKey: apiKey,
    baseUrl: "https://api.texto.com.au", // only change for a mock server or test proxy
    timeout: TimeSpan.FromSeconds(30),
    maxRetries: 2);
```

Idempotent requests are retried automatically with backoff. Pass your own
`HttpClient` if you manage pooling or handlers yourself.

## Support

- Docs: https://texto.com.au/developers
- Email: support@texto.com.au

MIT licensed. Copyright (c) 2026 Floop Pty Ltd trading as Texto.
