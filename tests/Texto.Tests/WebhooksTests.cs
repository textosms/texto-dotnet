using System;
using System.Security.Cryptography;
using System.Text;
using Texto;
using Xunit;

namespace Texto.Tests
{
    public class WebhooksTests
    {
        private static string Sign(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256=" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Fact]
        public void VerifiesAValidSignature()
        {
            const string payload = "{\"event\":\"message.inbound\"}";
            Assert.True(Webhooks.VerifySignature(payload, Sign(payload, "secret"), "secret"));
        }

        [Fact]
        public void RejectsAnInvalidSignature()
        {
            const string payload = "{\"event\":\"message.inbound\"}";
            Assert.False(Webhooks.VerifySignature(payload, Sign(payload, "other"), "secret"));
            Assert.False(Webhooks.VerifySignature(payload, null, "secret"));
        }

        [Fact]
        public void ParsesAndIdentifiesInboundEvents()
        {
            const string payload = "{\"event\":\"message.inbound\",\"message\":{\"id\":\"m_1\"}}";
            var evt = Webhooks.ParseEvent(payload, Sign(payload, "secret"), "secret");
            Assert.True(Webhooks.IsInboundEvent(evt));
        }

        [Fact]
        public void ParseEventThrowsOnBadSignature()
        {
            Assert.Throws<TextoException>(() => Webhooks.ParseEvent("{}", "sha256=deadbeef", "secret"));
        }
    }
}
