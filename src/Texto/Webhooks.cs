using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Texto
{
    /// <summary>Helpers for verifying and parsing Texto webhooks.</summary>
    public static class Webhooks
    {
        /// <summary>
        /// Verifies the <c>X-Texto-Signature</c> header against the raw request body.
        /// </summary>
        /// <param name="payload">Raw request body, exactly as received.</param>
        /// <param name="signature">Value of the <c>X-Texto-Signature</c> header.</param>
        /// <param name="secret">Signing secret shown when the webhook secret was rotated.</param>
        public static bool VerifySignature(string payload, string? signature, string secret)
        {
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            {
                return false;
            }

            var provided = signature!.StartsWith("sha256=", StringComparison.Ordinal)
                ? signature.Substring(7)
                : signature;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var expected = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

            return FixedTimeEquals(expected, provided);
        }

        /// <summary>
        /// Verifies (when a secret is supplied) and decodes a webhook body.
        /// </summary>
        /// <exception cref="TextoException">Thrown when the signature does not match.</exception>
        public static JsonElement ParseEvent(string payload, string? signature, string? secret)
        {
            if (!string.IsNullOrEmpty(secret) && !VerifySignature(payload, signature, secret!))
            {
                throw new TextoException("Texto webhook signature verification failed");
            }

            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }

        /// <summary>Returns true when the event is an inbound (reply) message.</summary>
        public static bool IsInboundEvent(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty("event", out var name) &&
                   name.ValueKind == JsonValueKind.String &&
                   name.GetString() == "message.inbound";
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }

            return difference == 0;
        }
    }
}
