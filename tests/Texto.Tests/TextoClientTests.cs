using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Texto;
using Xunit;

namespace Texto.Tests
{
    public class TextoClientTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public List<HttpRequestMessage> Requests { get; } = new();

            public StubHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        [Fact]
        public void RequiresAnApiKey()
        {
            Assert.Throws<ArgumentException>(() => new TextoClient(""));
        }

        [Fact]
        public async Task SendsBearerTokenAndBody()
        {
            var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"message_id\":\"m_1\"}"));
            using var client = new TextoClient("txt_test", httpClient: new HttpClient(handler));

            var result = await client.SendAsync("+61400000000", "Hello");

            Assert.Equal("m_1", result.GetProperty("message_id").GetString());
            Assert.Equal("Bearer txt_test", handler.Requests[0].Headers.Authorization?.ToString());
            Assert.Equal("https://api.texto.com.au/send", handler.Requests[0].RequestUri!.ToString());
        }

        [Fact]
        public async Task ReadsTheCreditBalance()
        {
            var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"credits\":420}"));
            using var client = new TextoClient("txt_test", httpClient: new HttpClient(handler));

            Assert.Equal(420, await client.BalanceAsync());
        }

        [Fact]
        public async Task RaisesTypedErrors()
        {
            var handler = new StubHandler(Json((HttpStatusCode)402, "{\"error\":\"Not enough credits\",\"credits_required\":5,\"credits_available\":1}"));
            using var client = new TextoClient("txt_test", maxRetries: 0, httpClient: new HttpClient(handler));

            var error = await Assert.ThrowsAsync<TextoInsufficientCreditsException>(() => client.SendAsync("+61400000000", "Hi"));
            Assert.Equal(5, error.CreditsRequired);
            Assert.Equal(1, error.CreditsAvailable);
        }

        [Fact]
        public async Task RetriesIdempotentRequests()
        {
            var handler = new StubHandler(
                Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"try later\"}"),
                Json(HttpStatusCode.OK, "{\"credits\":10}"));
            using var client = new TextoClient("txt_test", httpClient: new HttpClient(handler));

            Assert.Equal(10, await client.BalanceAsync());
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task BuildsQueryStrings()
        {
            var handler = new StubHandler(Json(HttpStatusCode.OK, "{\"messages\":[]}"));
            using var client = new TextoClient("txt_test", httpClient: new HttpClient(handler));

            await client.InboxAsync(limit: 10, from: "+61400000000");

            Assert.Contains("limit=10", handler.Requests[0].RequestUri!.Query);
            Assert.Contains("from=%2B61400000000", handler.Requests[0].RequestUri!.Query);
        }
    }
}
