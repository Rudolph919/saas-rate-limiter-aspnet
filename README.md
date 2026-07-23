# SaaS Rate Limiter (ASP.NET Core)

[![Tests](https://github.com/Rudolph919/saas-rate-limiter-aspnet/actions/workflows/tests.yml/badge.svg)](https://github.com/Rudolph919/saas-rate-limiter-aspnet/actions/workflows/tests.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](SaasRateLimiter/SaasRateLimiter.csproj)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Tiered, per-tenant rate-limiting middleware for a multi-tenant API — stops one noisy client from starving everyone else's quota.

This is a .NET 8 / ASP.NET Core port of the original Laravel implementation at [saas-rate-limiter-laravel](https://github.com/Rudolph919/saas-rate-limiter-laravel): same behavior, same config shape, same HTTP contract, reimplemented with idiomatic ASP.NET Core building blocks (the middleware pipeline, the options pattern, `HttpRequest`/`HttpContext`).

## The problem

A single tenant's automated sync job was consuming roughly 40% of a shared API's capacity, driving up latency and causing timeouts for every other client. The fix isn't in the route handlers — it's a middleware layer that enforces per-tenant and per-endpoint limits **before** any application logic runs, so abusive traffic gets stopped at the door instead of after it's already done damage.

## How it works

Incoming `/api/*` requests pass through `RateLimitMiddleware`, which delegates to two services:

1. **`RateLimitResolver`** — reads `X-Org-Id`, maps the org to a tier, matches the HTTP method + path against the bound `RateLimitOptions`, and returns up to two limits: one per-client ceiling and one per-endpoint rule.
2. **`RateLimitCounter`** — a fixed-window store keyed like `client:org_acme` or `endpoint:org_acme:create_item`, tracking count + window start per key.

Both limits are checked on every non-exempt request, **client first, then endpoint** — the first failure returns `429` with which limit tripped and a `Retry-After` header. Missing `X-Org-Id` is a `401` (an identity problem, not a quota problem). Write endpoints are stricter than reads on the same path.

| Method | Path | Auth | Limit (default config) |
|--------|------|------|------------------------|
| GET | `/api/health` | None (exempt) | Skipped |
| GET | `/api/items` | `X-Org-Id` required | 80/min endpoint + tier ceiling |
| POST | `/api/items` | `X-Org-Id` required | 20/min endpoint + tier ceiling |
| DELETE | `/api/items/{id}` | `X-Org-Id` required | 10/min endpoint + tier ceiling |

**Sample orgs** (see `SaasRateLimiter/appsettings.json`, `RateLimits` section):

| `X-Org-Id` | Tier | Client ceiling |
|------------|------|-----------------|
| `org_acme` | premium | 500 / 60s |
| `org_globex` | standard | 100 / 60s |
| `org_initech` | standard | 100 / 60s |
| *(any other id)* | standard (default) | 100 / 60s |

## Requirements

- .NET 8 SDK

## Setup

```bash
dotnet restore
dotnet run --project SaasRateLimiter
```

Server runs at `http://127.0.0.1:5200` (see `SaasRateLimiter/Properties/launchSettings.json`). Swagger UI is available at `/swagger` in Development.

## Manual demo (curl)

```bash
# Health — exempt, no org header
curl -s http://127.0.0.1:5200/api/health | jq

# Missing org — 401
curl -s -w "\nHTTP %{http_code}\n" http://127.0.0.1:5200/api/items

# Normal read — 200
curl -s -H "X-Org-Id: org_acme" http://127.0.0.1:5200/api/items | jq

# Noisy tenant vs other tenants: hammer POST until 429 (default endpoint limit: 20/min)
for i in $(seq 1 22); do
  echo -n "POST $i: "
  curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Org-Id: org_globex" http://127.0.0.1:5200/api/items
done

# Full 429 body + Retry-After
curl -i -X POST -H "X-Org-Id: org_globex" http://127.0.0.1:5200/api/items

# Other tenant unaffected
curl -s -H "X-Org-Id: org_acme" http://127.0.0.1:5200/api/items | jq
```

### Automated demo script

```bash
chmod +x scripts/demo-rate-limits.sh
./scripts/demo-rate-limits.sh
# Or against another host:
./scripts/demo-rate-limits.sh http://127.0.0.1:5200
```

## Error responses

Same shape as the Laravel version, byte-for-byte:

**401 — missing `X-Org-Id`:**

```json
{ "error": "unauthorized", "detail": "X-Org-Id header is required" }
```

**429 — rate limit exceeded:**

```json
{
  "error": "rate_limit_exceeded",
  "limit": "per_endpoint",
  "detail": "Organization org_globex exceeded 20 requests per 60 seconds on create_item endpoint",
  "retry_after_seconds": 42
}
```

Response includes a `Retry-After: 42` header.

## Architecture

Single ASP.NET Core Web API project (mirrors the Laravel app's flat structure rather than a layered/Clean Architecture split — not warranted at this size):

| Laravel | .NET equivalent |
|---|---|
| `config/rate_limits.php` | `appsettings.json` (`RateLimits` section) + `Configuration/RateLimitOptions.cs`, bound via the options pattern (`IOptions<RateLimitOptions>`) |
| `app/Http/Middleware/RateLimitMiddleware.php` | `Middleware/RateLimitMiddleware.cs` (conventional ASP.NET Core middleware, `InvokeAsync`) |
| `app/Services/RateLimiting/*.php` | `Services/RateLimiting/*.cs` — `RateLimitResolver`, `RateLimitCounter`, and `ResolvedLimit`/`RateLimitResult`/`RateLimitResolution` as C# `record`s (the .NET analogue of PHP's `readonly class`) |
| `app/Http/Controllers/ApiController.php` | `Controllers/ApiController.cs` |
| `routes/api.php` | `[Route]`/`[HttpGet]` etc. attributes on `ApiController` |

**One deliberate behavioral upgrade, not just a line-for-line port:** the Laravel counter is a plain in-process array, safe there because PHP's dev server handles one request per process. Kestrel serves requests on a shared thread pool, so `RateLimitCounter` uses a `ConcurrentDictionary` with a per-key lock around the window-check-and-increment to stay race-free under concurrent hits to the same org/endpoint key — same fixed-window algorithm, correct under real concurrency.

**Algorithm:** still fixed window (epoch-aligned boundaries) — same boundary-burst trade-off as the Laravel version: a client can send a full quota at the end of one window and again at the start of the next.

## AI collaboration

The original Laravel design went through a round of AI-assisted iteration before this port existed. The fix worth carrying forward: requests with a missing or unrecognized `X-Org-Id` must not share a single "unknown" counter bucket — that recreates the exact noisy-neighbor bug this middleware exists to prevent. A missing header is a `401` before rate limiting runs; an unrecognized org still gets its own counter key on the default tier. (The concurrency fix that came out of the port itself — the `ConcurrentDictionary` swap — is covered in Architecture above.)

## What I'd change for production

- **Real auth before rate limiting** — the middleware currently trusts whatever `X-Org-Id` a caller sends. In production, resolve the org server-side from an API key/OAuth/JWT credential, never from a client-supplied header.
- **Distributed counters** (e.g. Redis) — the counter is now thread-safe within one instance, but each instance still keeps its own store; behind a load balancer with N instances an org's effective limit is roughly N × configured, since requests split across independent counters.
- **Sliding window (or token bucket)** instead of fixed window, to remove the boundary-burst edge case.
- **Metrics** — 429 rate by org, counter map size, p99 latency before/after the middleware.

## Tests

```bash
dotnet test
```

`SaasRateLimiter.Tests` ports all 36 Laravel test cases 1:1 (same scenarios, same names translated to PascalCase):

| Area | File |
|------|------|
| Resolver (org, tier, paths, exempt) | `Unit/RateLimiting/RateLimitResolverTests.cs` |
| Counter (windows, Retry-After, burst) | `Unit/RateLimiting/RateLimitCounterTests.cs` |
| Middleware (401, 429, tiers) — via `WebApplicationFactory<Program>` | `Feature/RateLimitMiddlewareTests.cs` |
| Noisy-tenant demo scenario | `Feature/RateLimitDemoTests.cs` |

Each integration test spins up its own `WebApplicationFactory`, giving every test a fresh DI container (and therefore an empty `RateLimitCounter`) — the .NET equivalent of Laravel booting a fresh app per test plus an explicit counter reset.

## License

[MIT](LICENSE)
