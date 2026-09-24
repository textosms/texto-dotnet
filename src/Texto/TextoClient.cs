using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Texto
{
    /// <summary>
    /// Client for the Texto SMS API.
    /// </summary>
    /// <example>
    /// <code>
    /// var texto = new TextoClient(Environment.GetEnvironmentVariable("TEXTO_API_KEY")!);
    /// await texto.SendAsync("+61400000000", "Hello from Texto");
    /// </code>
    /// </example>
    public class TextoClient : IDisposable
    {
        /// <summary>Production base URL for the Texto API.</summary>
        public const string DefaultBaseUrl = "https://api.texto.com.au";

        private static readonly int[] RetryableStatuses = { 408, 409, 429, 500, 502, 503, 504 };

        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;
        private readonly string _baseUrl;
        private readonly int _maxRetries;

        /// <summary>Creates a client.</summary>
        /// <param name="apiKey">Your Texto API key, beginning <c>txt_</c>.</param>
        /// <param name="baseUrl">Override only to point at a mock server or a corporate proxy during testing.</param>
        /// <param name="timeout">Per-request timeout. Defaults to 30 seconds.</param>
        /// <param name="maxRetries">Retries for idempotent requests. Defaults to 2.</param>
        /// <param name="httpClient">Supply your own <see cref="HttpClient"/> to control pooling or handlers.</param>
        public TextoClient(
            string apiKey,
            string baseUrl = DefaultBaseUrl,
            TimeSpan? timeout = null,
            int maxRetries = 2,
            HttpClient? httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("A Texto API key is required.", nameof(apiKey));
            }

            _baseUrl = baseUrl.TrimEnd('/');
            _maxRetries = Math.Max(0, maxRetries);
            _ownsHttpClient = httpClient == null;
            _http = httpClient ?? new HttpClient();
            _http.Timeout = timeout ?? TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
            _http.DefaultRequestHeaders.Remove("User-Agent");
            _http.DefaultRequestHeaders.Add("User-Agent", "texto-dotnet/1.0.0");
        }

        /// <summary>Performs a raw request. Exposed for endpoints not yet wrapped.</summary>
        public async Task<JsonElement> RequestAsync(
            HttpMethod method,
            string path,
            IDictionary<string, object?>? query = null,
            object? body = null,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var url = _baseUrl + path + BuildQuery(query);
            var idempotent = method != HttpMethod.Post || idempotencyKey != null;
            var attempts = idempotent ? _maxRetries : 0;

            for (var attempt = 0; ; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(Math.Min((int)Math.Pow(2, attempt) * 250, 4000), cancellationToken)
                        .ConfigureAwait(false);
                }

                using var request = new HttpRequestMessage(method, url);
                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json");
                }

                if (idempotencyKey != null)
                {
                    request.Headers.Add("Idempotency-Key", idempotencyKey);
                }

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    if (attempt < attempts)
                    {
                        continue;
                    }

                    throw new TextoConnectionException($"Request to {path} failed: {ex.Message}", ex);
                }

                using (response)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    var parsed = ParseJson(content);

                    if (response.IsSuccessStatusCode)
                    {
                        return parsed;
                    }

                    if (RetryableStatuses.Contains(status) && attempt < attempts)
                    {
                        continue;
                    }

                    var requestId = response.Headers.TryGetValues("x-request-id", out var ids)
                        ? ids.FirstOrDefault()
                        : null;
                    int? retryAfter = response.Headers.RetryAfter?.Delta is TimeSpan delta
                        ? (int)delta.TotalSeconds
                        : null;

                    throw ErrorFor(status, parsed, requestId, retryAfter);
                }
            }
        }

        private Task<JsonElement> GetAsync(string path, IDictionary<string, object?>? query = null, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get, path, query, null, null, ct);

        private Task<JsonElement> PostAsync(string path, object? body = null, string? idempotencyKey = null, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Post, path, null, body, idempotencyKey, ct);

        // ── status ──────────────────────────────────────────────────

        /// <summary>Public health check. Does not consume credits.</summary>
        public Task<JsonElement> StatusAsync(CancellationToken ct = default)
            => GetAsync("/status", null, ct);

        // ── messaging ───────────────────────────────────────────────

        /// <summary>
        /// Sends a single SMS. Include <c>{{OptOutLink}}</c> in the message to
        /// insert a unique per-recipient opt-out link (<c>texto.au/xxxxxx</c>,
        /// 15 characters). It always shortens, independent of link tracking,
        /// and is the recommended opt-out mechanism for Sender ID sends, where
        /// recipients cannot reply STOP.
        /// </summary>
        public Task<JsonElement> SendAsync(
            string to,
            string message,
            string? sender = null,
            bool? linkTracking = null,
            string? campaign = null,
            string? idempotencyKey = null,
            CancellationToken ct = default)
            => PostAsync("/send", Clean(new Dictionary<string, object?>
            {
                ["to"] = to,
                ["message"] = message,
                ["sender"] = sender,
                ["link_tracking"] = linkTracking,
                ["campaign"] = campaign,
            }), idempotencyKey, ct);

        /// <summary>
        /// Sends one message to up to 1,000 recipients, with optional merge
        /// data. Merge fields use <c>{{key}}</c> syntax;
        /// <c>{{SendingNumber}}</c> is always available. Include
        /// <c>{{OptOutLink}}</c> to insert a unique per-recipient opt-out link
        /// (always shortened to 15 characters) — recommended for Sender ID
        /// sends, where recipients cannot reply STOP.
        /// </summary>
        public Task<JsonElement> SendBatchAsync(
            IEnumerable<object> recipients,
            string message,
            string? sender = null,
            bool? linkTracking = null,
            string? campaign = null,
            string? idempotencyKey = null,
            CancellationToken ct = default)
            => PostAsync("/send-batch", Clean(new Dictionary<string, object?>
            {
                ["recipients"] = recipients,
                ["message"] = message,
                ["sender"] = sender,
                ["link_tracking"] = linkTracking,
                ["campaign"] = campaign,
            }), idempotencyKey, ct);

        /// <summary>Fetches a message and its delivery receipt.</summary>
        public Task<JsonElement> GetMessageAsync(string messageId, CancellationToken ct = default)
            => GetAsync("/message/" + Uri.EscapeDataString(messageId), null, ct);

        /// <summary>Fetches a campaign and its per-message results.</summary>
        public Task<JsonElement> GetCampaignAsync(string campaignId, int? limit = null, int? offset = null, CancellationToken ct = default)
            => GetAsync("/campaign/" + Uri.EscapeDataString(campaignId), new Dictionary<string, object?>
            {
                ["limit"] = limit,
                ["offset"] = offset,
            }, ct);

        // ── inbox and opt-outs ──────────────────────────────────────

        /// <summary>Lists inbound (reply) messages.</summary>
        public Task<JsonElement> InboxAsync(
            int? limit = null,
            int? offset = null,
            string? from = null,
            string? dateFrom = null,
            string? dateTo = null,
            CancellationToken ct = default)
            => GetAsync("/inbox", new Dictionary<string, object?>
            {
                ["limit"] = limit,
                ["offset"] = offset,
                ["from"] = from,
                ["date_from"] = dateFrom,
                ["date_to"] = dateTo,
            }, ct);

        /// <summary>Lists numbers that have opted out.</summary>
        public Task<JsonElement> OptOutsAsync(CancellationToken ct = default)
            => GetAsync("/optouts", null, ct);

        // ── credits ─────────────────────────────────────────────────

        /// <summary>Returns the credit balance for the calling account.</summary>
        public async Task<int> BalanceAsync(CancellationToken ct = default)
        {
            var result = await GetAsync("/balance", null, ct).ConfigureAwait(false);
            return result.TryGetProperty("credits", out var credits) && credits.TryGetInt32(out var value) ? value : 0;
        }

        /// <summary>Returns the credit balance for a sub-account.</summary>
        public async Task<int> AccountBalanceAsync(string accountId, CancellationToken ct = default)
        {
            var result = await GetAsync($"/account/{Uri.EscapeDataString(accountId)}/balance", null, ct).ConfigureAwait(false);
            return result.TryGetProperty("credits", out var credits) && credits.TryGetInt32(out var value) ? value : 0;
        }

        /// <summary>Moves credits down to a sub-account.</summary>
        public Task<JsonElement> AllocateCreditsAsync(string accountId, int amount, CancellationToken ct = default)
            => PostAsync($"/account/{Uri.EscapeDataString(accountId)}/credits/allocate", new { amount }, null, ct);

        /// <summary>Pulls credits back from a sub-account.</summary>
        public Task<JsonElement> RecallCreditsAsync(string accountId, int amount, CancellationToken ct = default)
            => PostAsync($"/account/{Uri.EscapeDataString(accountId)}/credits/recall", new { amount }, null, ct);

        // ── accounts ────────────────────────────────────────────────

        /// <summary>Lists the calling account and its sub-accounts.</summary>
        public Task<JsonElement> ListAccountsAsync(CancellationToken ct = default)
            => GetAsync("/accounts", null, ct);

        /// <summary>Creates a sub-account.</summary>
        public Task<JsonElement> CreateAccountAsync(string businessName, IDictionary<string, object?>? options = null, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> { ["business_name"] = businessName };
            if (options != null)
            {
                foreach (var pair in options)
                {
                    body[pair.Key] = pair.Value;
                }
            }

            return PostAsync("/accounts", Clean(body), null, ct);
        }

        /// <summary>Fetches full detail for an account.</summary>
        public Task<JsonElement> GetAccountAsync(string accountId, CancellationToken ct = default)
            => GetAsync("/account/" + Uri.EscapeDataString(accountId), null, ct);

        /// <summary>Updates a sub-account.</summary>
        public Task<JsonElement> UpdateAccountAsync(string accountId, IDictionary<string, object?> fields, CancellationToken ct = default)
            => RequestAsync(new HttpMethod("PATCH"), "/account/" + Uri.EscapeDataString(accountId), null, Clean(fields), null, ct);

        /// <summary>Schedules a sub-account for deletion.</summary>
        public Task<JsonElement> DeleteAccountAsync(string accountId, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Delete, "/account/" + Uri.EscapeDataString(accountId), null, null, null, ct);

        // ── users and access ────────────────────────────────────────

        /// <summary>Lists team members on the calling account.</summary>
        public Task<JsonElement> ListTeamAsync(CancellationToken ct = default)
            => GetAsync("/team", null, ct);

        /// <summary>Lists team members on a specific account.</summary>
        public Task<JsonElement> ListAccountUsersAsync(string accountId, CancellationToken ct = default)
            => GetAsync($"/account/{Uri.EscapeDataString(accountId)}/users", null, ct);

        /// <summary>Invites a person to an account.</summary>
        public Task<JsonElement> InviteUserAsync(string accountId, string email, IDictionary<string, object?>? options = null, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> { ["email"] = email };
            if (options != null)
            {
                foreach (var pair in options)
                {
                    body[pair.Key] = pair.Value;
                }
            }

            return PostAsync($"/account/{Uri.EscapeDataString(accountId)}/users", Clean(body), null, ct);
        }

        /// <summary>Removes a team member.</summary>
        public Task<JsonElement> RemoveUserAsync(string accountId, string memberId, CancellationToken ct = default)
            => RequestAsync(
                HttpMethod.Delete,
                $"/account/{Uri.EscapeDataString(accountId)}/users/{Uri.EscapeDataString(memberId)}",
                null, null, null, ct);

        /// <summary>Reads parent-management and inherited-team settings.</summary>
        public Task<JsonElement> GetAccessAsync(string accountId, CancellationToken ct = default)
            => GetAsync($"/account/{Uri.EscapeDataString(accountId)}/access", null, ct);

        /// <summary>Updates parent-management and inherited-team settings.</summary>
        public Task<JsonElement> SetAccessAsync(string accountId, IDictionary<string, object?> settings, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Put, $"/account/{Uri.EscapeDataString(accountId)}/access", null, Clean(settings), null, ct);

        // ── API keys ────────────────────────────────────────────────

        /// <summary>Lists API keys on an account. Key values are never returned.</summary>
        public Task<JsonElement> ListKeysAsync(string accountId, CancellationToken ct = default)
            => GetAsync($"/account/{Uri.EscapeDataString(accountId)}/keys", null, ct);

        /// <summary>Creates an API key. The value is returned once.</summary>
        public Task<JsonElement> CreateKeyAsync(string accountId, string name, CancellationToken ct = default)
            => PostAsync($"/account/{Uri.EscapeDataString(accountId)}/key", new { name }, null, ct);

        /// <summary>Revokes an API key.</summary>
        public Task<JsonElement> RevokeKeyAsync(string accountId, string keyId, CancellationToken ct = default)
            => RequestAsync(
                HttpMethod.Delete,
                $"/account/{Uri.EscapeDataString(accountId)}/key/{Uri.EscapeDataString(keyId)}",
                null, null, null, ct);

        // ── numbers ─────────────────────────────────────────────────

        /// <summary>Lists numbers owned by the calling account.</summary>
        public Task<JsonElement> ListNumbersAsync(CancellationToken ct = default)
            => GetAsync("/numbers", null, ct);

        /// <summary>Lists numbers across the account and its sub-accounts.</summary>
        public Task<JsonElement> ListGroupNumbersAsync(CancellationToken ct = default)
            => GetAsync("/numbers/group", null, ct);

        /// <summary>Lists numbers assigned to a specific account.</summary>
        public Task<JsonElement> ListAccountNumbersAsync(string accountId, CancellationToken ct = default)
            => GetAsync($"/account/{Uri.EscapeDataString(accountId)}/numbers", null, ct);

        /// <summary>Lists numbers currently available to purchase.</summary>
        public Task<JsonElement> AvailableNumbersAsync(string? country = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => GetAsync("/numbers/available", new Dictionary<string, object?>
            {
                ["country"] = country,
                ["limit"] = limit,
                ["offset"] = offset,
            }, ct);

        /// <summary>Purchases a dedicated number. Charges the saved card.</summary>
        public Task<JsonElement> PurchaseNumberAsync(string? number = null, string? label = null, string? country = null, CancellationToken ct = default)
            => PostAsync("/numbers/purchase", Clean(new Dictionary<string, object?>
            {
                ["number"] = number,
                ["label"] = label,
                ["country"] = country,
            }), null, ct);

        /// <summary>Assigns parent-owned numbers to a sub-account.</summary>
        public Task<JsonElement> AssignNumbersAsync(string accountId, IEnumerable<string> numbers, CancellationToken ct = default)
            => PostAsync($"/account/{Uri.EscapeDataString(accountId)}/numbers/assign", new { numbers }, null, ct);

        /// <summary>Recalls numbers from a sub-account.</summary>
        public Task<JsonElement> RecallNumbersAsync(string accountId, IEnumerable<string> numbers, CancellationToken ct = default)
            => PostAsync($"/account/{Uri.EscapeDataString(accountId)}/numbers/recall", new { numbers }, null, ct);

        // ── webhooks ────────────────────────────────────────────────

        /// <summary>Reads the configured delivery receipt and inbound webhooks.</summary>
        public Task<JsonElement> GetWebhooksAsync(CancellationToken ct = default)
            => GetAsync("/webhooks", null, ct);

        /// <summary>Creates or updates the delivery receipt webhook.</summary>
        public Task<JsonElement> SetDeliveryWebhookAsync(string url, bool? enabled = null, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Put, "/webhooks/delivery", null, Clean(new Dictionary<string, object?>
            {
                ["url"] = url,
                ["enabled"] = enabled,
            }), null, ct);

        /// <summary>Creates or updates the inbound message webhook.</summary>
        public Task<JsonElement> SetInboundWebhookAsync(string url, bool? enabled = null, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Put, "/webhooks/inbound", null, Clean(new Dictionary<string, object?>
            {
                ["url"] = url,
                ["enabled"] = enabled,
            }), null, ct);

        /// <summary>Rotates the delivery receipt signing secret. Returned once.</summary>
        public Task<JsonElement> RotateDeliverySecretAsync(CancellationToken ct = default)
            => PostAsync("/webhooks/delivery", null, null, ct);

        /// <summary>Rotates the inbound signing secret. Returned once.</summary>
        public Task<JsonElement> RotateInboundSecretAsync(CancellationToken ct = default)
            => PostAsync("/webhooks/inbound", null, null, ct);

        // ── reporting ───────────────────────────────────────────────

        /// <summary>Reports on the calling account.</summary>
        public Task<JsonElement> ReportAsync(IDictionary<string, object?>? filters = null, CancellationToken ct = default)
            => GetAsync("/report", filters, ct);

        /// <summary>Reports on a specific account in the hierarchy.</summary>
        public Task<JsonElement> AccountReportAsync(string accountId, IDictionary<string, object?>? filters = null, CancellationToken ct = default)
            => GetAsync($"/account/{Uri.EscapeDataString(accountId)}/report", filters, ct);

        /// <summary>Reports across the calling account and all its sub-accounts.</summary>
        public Task<JsonElement> GroupReportAsync(string? from = null, string? to = null, CancellationToken ct = default)
            => GetAsync("/report/group", new Dictionary<string, object?> { ["from"] = from, ["to"] = to }, ct);

        /// <summary>Releases the underlying <see cref="HttpClient"/> when this client created it.</summary>
        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _http.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private static Dictionary<string, object?> Clean(IDictionary<string, object?> data)
            => data.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value);

        private static string BuildQuery(IDictionary<string, object?>? query)
        {
            if (query == null)
            {
                return string.Empty;
            }

            var parts = query
                .Where(pair => pair.Value != null)
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(Convert.ToString(pair.Value) ?? string.Empty))
                .ToList();

            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        private static JsonElement ParseJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                using var empty = JsonDocument.Parse("{}");
                return empty.RootElement.Clone();
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                using var fallback = JsonDocument.Parse(JsonSerializer.Serialize(new { error = content }));
                return fallback.RootElement.Clone();
            }
        }

        private static TextoApiException ErrorFor(int status, JsonElement body, string? requestId, int? retryAfter)
        {
            var message = body.ValueKind == JsonValueKind.Object &&
                          body.TryGetProperty("error", out var error) &&
                          error.ValueKind == JsonValueKind.String
                ? error.GetString()!
                : $"Texto API request failed with status {status}";

            string? code = body.ValueKind == JsonValueKind.Object &&
                           body.TryGetProperty("code", out var codeValue) &&
                           codeValue.ValueKind == JsonValueKind.String
                ? codeValue.GetString()
                : null;

            return status switch
            {
                401 => new TextoAuthenticationException(message, status, body, code, requestId),
                402 => new TextoInsufficientCreditsException(message, status, body, code, requestId),
                403 => new TextoPermissionException(message, status, body, code, requestId),
                404 => new TextoNotFoundException(message, status, body, code, requestId),
                429 => new TextoRateLimitException(message, status, body, code, requestId, retryAfter),
                >= 500 => new TextoServerException(message, status, body, code, requestId),
                _ => new TextoInvalidRequestException(message, status, body, code, requestId),
            };
        }
    }
}
