# Changelog

All notable changes to the Texto .NET SDK are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-01-01

### Added
- First public release, targeting .NET 6 and .NET Standard 2.0.
- Full coverage of the Texto SMS API: status, single and batch sending,
  message and campaign lookup, inbox, opt-outs, credits and allocations,
  accounts and sub-accounts, team members and access settings, API keys,
  numbers, webhooks and reporting.
- Typed exceptions for authentication, permission, validation, insufficient
  credit, not-found, rate-limit and server errors.
- Automatic retries with backoff for idempotent requests.
- HMAC-SHA256 webhook signature verification helpers.
