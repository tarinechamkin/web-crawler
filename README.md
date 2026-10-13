# Web Crawler

A job-based web crawler. A user submits a URL; an API creates a crawl job and publishes a message; a separate
worker crawls the site breadth-first, stores pages and links in PostgreSQL, and computes a **Domain Link Ratio** per
page; a React UI shows live progress, the discovered-page tree and the job history.

**Stack:** .NET 8 (ASP.NET Core minimal APIs, EF Core), RabbitMQ, PostgreSQL, React 18 + TypeScript (Vite),
Docker Compose. **Tests:** 79 (xUnit; unit, fixture-site integration, and real-PostgreSQL via Testcontainers).

---

## Run it

Requires Docker Desktop (or Docker Engine with Compose v2).

```bash
docker compose up --build
```

| What | URL |
|---|---|
| **UI** | http://localhost:3000 |
| API | http://localhost:5080 — e.g. `GET /api/jobs` |
| Health | http://localhost:5080/health (API). The worker's is on a port from 5081–5085 (a range, so it can be scaled); `docker compose port worker 8080` shows which |
| RabbitMQ management (queues, DLQ) | http://localhost:15672 — `crawler` / `crawler` |
| PostgreSQL | `localhost:5432`, database/user/password `crawler` |

Useful extras: `docker compose up --scale worker=3` (competing consumers), `docker compose down -v` (wipe data).
Sample requests: [`src/Crawler.Api/Crawler.Api.http`](src/Crawler.Api/Crawler.Api.http).

Good sites to try (built for crawling): `https://books.toscrape.com` (hits the 200-page limit),
`https://quotes.toscrape.com` (mixed ratios, HTTPS→HTTP redirects), `https://crawler-test.com` (edge cases),
`https://example.com` (one external link → ratio 0), `https://httpbin.org/status/503` (retries, then Failed).

### Developing without containers for the apps

```bash
docker compose up -d postgres rabbitmq          # infrastructure only
dotnet run --project src/Crawler.Api            # http://localhost:5080 (applies migrations)
dotnet run --project src/Crawler.Worker         # http://localhost:5081
cd web && npm install && npm run dev            # http://localhost:5173, proxies /api to :5080
dotnet test                                     # needs Docker running (Testcontainers)
```

---

## Architecture

```
 React UI ──HTTP──► Crawl API (Service A) ──publish──► RabbitMQ ──consume──► Crawl Worker (Service B)
 (nginx :3000)      create / status / tree /            crawl-jobs            fetch → parse links → ratio
    ▲ polls         history / cancel                    (+ retry, DLQ)        → save page + links → progress
    │                       │                                                         │
    └───────────────────────┴──────────────────► PostgreSQL ◄─────────────────────────┘
                                 API: reads, creates jobs        Worker: writes pages, links, progress
```

1. `POST /api/jobs` validates and normalizes the URL, inserts a `Pending` job, publishes `CrawlJobRequested`
   (with publisher confirms) and returns `202 { jobId }`.
2. The worker consumes the message (manual ack, prefetch 1), marks the job `Running`, and crawls breadth-first up to
   `maxDepth` and `maxPages`. After each page it saves the page and its links in one transaction and refreshes the
   progress counter, which also detects cancellation.
3. The worker marks the job `Completed`, or `Failed` with a reason, and acks the message.
4. The UI polls `GET /api/jobs/{id}` every 1.5 s while the job is active, then loads the tree.

### Projects (clean architecture: dependencies point inward)

| Project | Responsibility |
|---|---|
| `src/Crawler.Domain` | Pure logic, no I/O: URL normalization, link extraction, Domain Link Ratio, entities, the breadth-first `CrawlEngine`, and the ports (`IPageFetcher`, `ICrawlJobStore`) it depends on |
| `src/Crawler.Infrastructure` | Adapters: EF Core/PostgreSQL store and migrations, resilient `HttpPageFetcher`, RabbitMQ topology/publisher, logging and health checks |
| `src/Crawler.Api` | Service A: REST endpoints, validation, job creation and cancel, read queries. Applies DB migrations on startup |
| `src/Crawler.Worker` | Service B: hosts `CrawlJobConsumer` (RabbitMQ → `CrawlEngine`) and `/health` |
| `web/` | React UI, served by nginx, which also proxies `/api` (same origin, no CORS) |
| `tests/Crawler.Tests` | Unit, integration and persistence tests |

### Key architectural choices

- **RabbitMQ over Kafka.** This is a work queue ("process each job once"), not an event log that needs replay.
  RabbitMQ gives per-message acks, dead-lettering, TTLs and competing consumers natively, with a one-container setup.
- **One message per job; the worker crawls the whole job.** Simple, correct progress, cancellation and completion.
  Fanning out one message per page would scale a single large crawl across workers, but then "is the job done?"
  needs distributed counting. That is the documented next step.
- **Raw `RabbitMQ.Client` instead of MassTransit**, so the retry/DLQ topology is explicit, reviewable code
  ([`RabbitMqTopology.cs`](src/Crawler.Infrastructure/Messaging/RabbitMqTopology.cs)) rather than library convention.
- **Shared database.** The worker writes pages directly instead of sending results back to the API. Fewer moving
  parts; the cost is that both services depend on one schema (owned and migrated by the API).
- **The database row is the source of truth; the message only carries the `jobId`** (plus context for debugging).
  Cancellation is a status change the worker observes, not a second message.

---

## API

| Method & path | Purpose | Responses |
|---|---|---|
| `POST /api/jobs` | Create a job. Body `{ "url": string, "maxDepth"?: number }` | `202 { jobId }` + `Location`; `400` validation problem; `503` if it could not be queued |
| `GET /api/jobs/{jobId}` | Status and summary (timestamps, pagesCrawled, failureReason) | `200`, `404` |
| `GET /api/jobs/{jobId}/tree` | Hierarchical result: each page with depth, HTTP status/error, link counts, ratio, children | `200`, `404` |
| `GET /api/jobs/{jobId}/pages/{pageId}/links` | One page's outgoing links, each with `isInternal` (loaded on demand) | `200`, `404` |
| `GET /api/jobs?limit=20&cursor=…` | History, newest first, keyset-paginated (`nextCursor`) | `200`, `400` bad cursor |
| `POST /api/jobs/{jobId}/cancel` | Cancel a Pending/Running job (optional feature) | `202`, `404`, `409` already finished |

Errors use RFC 7807 `ProblemDetails`. Statuses serialize as strings (`"Running"`).

---

## Crawling rules and assumptions

| Rule | Implementation |
|---|---|
| HTML only | Decided by the response `Content-Type` (`text/html`, `application/xhtml+xml`). Other responses are recorded with "Not HTML (…)". Links ending in `.pdf`, `.jpg`, `.css`, … are recorded but never fetched |
| Depth | Default **2**; accepted range **0–5** (our cap, configurable `Crawl:MaxDepthLimit`). Depth 0 = only the start page |
| Max pages | **200** per job (`Crawl:MaxPagesPerJob`), counting every stored page including failed ones |
| Scope | Only links on the **starting host** are followed; external links are recorded and counted in the ratio but never crawled |
| Normalization | Resolve relative links against the final (post-redirect) URL and `<base href>`; lowercase scheme and host; drop fragment, user-info and default port; keep path case and query string; reject URLs over 2048 chars |
| Duplicates | A URL is queued at most once per job (in-memory visited set), and `(job_id, url)` is unique in the database |
| HTTP | 10 s per attempt, 3 retries with exponential backoff + jitter on transient errors, 30 s total, up to 5 redirects, 2 MB body cap, unknown charsets fall back to UTF-8 |
| Failures | A failed **start** page fails the job. Any other failed page is stored with its error and the crawl continues. Unexpected per-page exceptions are also contained to the page (bulkhead) |
| Tree | Each page hangs under the page that **first discovered** it (a breadth-first spanning tree of the link graph). All edges are kept in `page_links` |

### Domain Link Ratio

`internal links / total outgoing links` per page, where:

- outgoing links are the page's **distinct** normalized `http(s)` links from `<a href>`;
- `mailto:`, `tel:`, `javascript:`, other non-http(s) schemes and pure in-page anchors (`href="#top"`) are ignored;
- **internal** means the host equals the start URL's host, case-insensitively. Subdomains (`blog.example.com`,
  `www.example.com`) count as external, because the spec defines the domain as *the host* of the initial URL;
- zero outgoing links → ratio `0` (the UI shows "no links").

---

## Messaging

### Message schema

Queue `crawl-jobs`, JSON (camelCase), `content-type: application/json`, persistent delivery:

```json
{
  "schemaVersion": 1,
  "jobId": "3f2a4c1e-8b7d-4c1a-9a51-2b6f0e9d1c33",
  "url": "https://books.toscrape.com/",
  "maxDepth": 2,
  "requestedAt": "2026-10-12T21:30:00+00:00"
}
```

AMQP properties: `messageId` (unique per publish), `correlationId` = `jobId` (ties API and worker logs together),
`type` = `CrawlJobRequested`, header `x-retry-count`. `schemaVersion` lets the format evolve: an unknown version is
dead-lettered instead of being misread.

### Topology

```
exchange "crawler" (direct) ──► crawl-jobs  (quorum queue, x-delivery-limit 5, dead-letters to crawl-jobs.dlq)
                                   ▲
crawl-jobs.retry.5s  ──TTL 5s──────┤   no consumers: a message waits for its TTL,
crawl-jobs.retry.30s ──TTL 30s─────┤   then RabbitMQ dead-letters it back to crawl-jobs
crawl-jobs.retry.2m  ──TTL 2m──────┘
crawl-jobs.dlq       (inspect in the RabbitMQ UI)
```

### Idempotency strategy (at-least-once delivery)

A message is acked only after its job is fully processed, so a crash means redelivery. Processing the same message
twice is safe because:

1. **Unique keys + `INSERT … ON CONFLICT DO NOTHING`.** `crawled_pages (job_id, url)` is unique and `page_links` has
   primary key `(page_id, target_url)`. A replayed page insert returns the existing row's id and the crawl continues.
   A page and its links are written in one transaction.
2. **Guarded status transitions.** `Pending|Running → Running`, `Running → Completed`; `Failed` and `Canceled` are
   never overwritten by `Completed`. A redelivery for a finished job is detected and skipped.
3. **Recomputed, not incremented, progress.** `pages_crawled = (SELECT count(*) …)`, so a replay cannot double-count.

**Verified:** killing the worker container mid-crawl (at 80 of 200 pages) led to redelivery (`"Redelivered": true` in
the log) and completion with 200 page rows / 200 distinct URLs and 5,010 link rows / 5,010 distinct.
`CrawlJobStoreTests` proves the same against real PostgreSQL.

### Retry policy: two levels

| Level | Where | Handles | Policy |
|---|---|---|---|
| **HTTP** (per page) | `AddCrawlerHttp` resilience pipeline | Network errors, timeouts, HTTP 408, 429 (honors `Retry-After`), 5xx | 3 retries, exponential backoff 0.5 s → 1 s → 2 s with jitter; 10 s per attempt, 30 s total. Then the page gets an error and the crawl goes on |
| **Message** (whole job) | `MessageFailurePolicy` + retry queues | Anything that escapes the crawl, e.g. the database is unreachable | 3 retries after 5 s, 30 s, 2 min, then DLQ. The engine resumes idempotently each time |

Transient means *might succeed later*. Permanent errors (an unusable message, a job that does not exist) are never
retried. Unknown exceptions are treated as possibly transient, but bounded by the retry count. Not retried at the
HTTP level: 4xx other than 408/429 (a 404 will not change).

### Dead-letter queue: what goes there

| Cause | Detected by |
|---|---|
| Malformed JSON, missing `jobId`, unsupported `schemaVersion` | `CrawlJobRequested.Deserialize` → straight to DLQ |
| Message for a job that does not exist | `JobNotFoundException` → straight to DLQ |
| Still failing after 3 message-level retries | `MessageFailurePolicy` |
| Delivered 5 times without the worker deciding (e.g. it crashes on that message every time) | RabbitMQ quorum queue `x-delivery-limit` |

When the **worker** dead-letters a message, it adds `x-error` (reason) and `x-dead-lettered-at` headers and marks the
job `Failed` with the reason, so the UI shows why. The exception is the delivery-limit case: RabbitMQ moves the message
itself, our code never runs, and the job stays `Running` (see known limitations). Verified by publishing garbage JSON
and an unknown `jobId` to the queue: both arrived in `crawl-jobs.dlq` with their reasons. Replaying is manual: open
`crawl-jobs.dlq` in the RabbitMQ UI, "Get messages", and re-publish the body to the `crawler` exchange. A replay
endpoint is listed under next steps.

---

## Data model and performance

```
crawl_jobs     id uuid PK, url, status, max_depth, max_pages, pages_crawled,
               created_at, started_at, completed_at, failure_reason
               INDEX (created_at DESC, id DESC)                  -- history: keyset pagination
crawled_pages  id bigint PK, job_id FK → crawl_jobs ON DELETE CASCADE, url, depth,
               parent_page_id FK → crawled_pages, http_status, error,
               outgoing_link_count, internal_link_count, domain_link_ratio, crawled_at
               UNIQUE (job_id, url)                              -- idempotency + "pages of a job"
               INDEX (parent_page_id)                            -- self-FK
page_links     page_id FK → crawled_pages ON DELETE CASCADE, target_url, job_id, is_internal
               PRIMARY KEY (page_id, target_url)                 -- idempotency (edges parent → child)
               INDEX (job_id)
```

- **Keyset pagination** for history: `WHERE (created_at, id) < (@c, @id) ORDER BY created_at DESC, id DESC LIMIT n+1`
  is an index seek at any depth and does not skip or repeat rows when new jobs arrive, unlike `OFFSET`. The cursor
  is opaque (base64url).
- **One round trip per page's links**: `INSERT … SELECT FROM unnest(@urls, @isInternal) ON CONFLICT DO NOTHING`.
- **Hot-path SQL is hand-written** (EF Core cannot express upserts). Reads use EF Core with no tracking. The worker
  fetches pages sequentially, so per-page writes are small and frequent rather than batched.
- **Link counts and the ratio are denormalized onto `crawled_pages`**, so the tree needs no aggregation over `page_links`.
- **The tree payload omits links** (up to 200 pages × N links). `GET …/pages/{id}/links` loads them on demand.
- **`text` URLs under a B-tree unique index** are capped at 2048 chars (entries over ~2.7 KB would fail to index).

---

## Observability

- **Structured JSON logs** (Serilog, compact JSON to stdout). Every line inside message processing carries `JobId`,
  `MessageId`, `RetryCount` and `Redelivered`; API lines carry `JobId` and the request trace id. The same `JobId`
  appears in API and worker logs: `docker compose logs api worker | grep <jobId>`.
- **Health checks** `/health` on both services check PostgreSQL and RabbitMQ. Compose uses them for startup order.
- RabbitMQ management UI for queue depth, retry queues and DLQ contents.

## Tests

`dotnet test` runs **79 tests** (Docker must be running for the PostgreSQL ones):

| Area | Covers |
|---|---|
| `Urls/*` (unit) | Normalization (relative, `../`, protocol-relative, fragments, case, ports, schemes, length), link extraction (`<base href>`, entities, duplicates), Domain Link Ratio (0 links, mixed, subdomains, case) |
| `Crawling/CrawlEngineIntegrationTests` | The real engine and real `HttpPageFetcher` (with retries) against **local HTML fixtures** served over HTTP: depth limit, de-duplication, ratios, tree parents, 404 / non-HTML pages, page cap, root failure, transient 503 retried, cancel, duplicate delivery, bad charset, per-page bulkhead |
| `Persistence/CrawlJobStoreTests` | Real PostgreSQL (Testcontainers): replayed page/link writes create no duplicates, progress is recomputed, `Completed` never overwrites `Canceled` |
| `Messaging/*`, `Http/*`, `Api/*` | Retry/DLQ decisions, message schema validation, redirect and charset handling, tree building, history cursor |

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds and tests the backend, type-checks and builds the
UI, and builds all Docker images.

---

## Priorities and trade-offs

### What I implemented first, and why

1. **Domain logic with unit tests** (normalization, link extraction, ratio, the BFS engine). Correctness is the
   largest part of the rubric and the trickiest logic, and it has no infrastructure dependencies, so it could be
   finished and tested first.
2. **Persistence with idempotent writes, proven against real PostgreSQL**, then **HTTP resilience** and the
   **fixture-site integration test**.
3. **Messaging: retry queues, DLQ, delivery limit.** This is the core of the event-driven requirements.
4. **API + worker hosts, end-to-end run, Docker Compose**, then the **UI** last. The spec asks for "clarity over design".
5. **Manual testing against real sites and a code review** before submitting. Testing `crawler-test.com` exposed an
   invalid-charset page that failed a whole job, which led to the per-page bulkhead. The review caught nginx DNS
   caching, double navigation in History and a Linux-only redirect parsing quirk.

### What I cut, and why

- **Per-page message fan-out**: see "Key architectural choices". One message per job keeps completion and
  cancellation trivially correct.
- **Transactional outbox.** If the job row is saved but the publish fails, the API marks the job `Failed` and
  returns `503` instead of leaving it `Pending` forever. An outbox would make create-and-publish atomic.
- **SSE/SignalR** (stretch goal): 1.5 s polling, which stops automatically when the job finishes, meets the requirement.
- **robots.txt, per-host politeness, parallel fetching** (see limitations).
- **UI tests and authentication.** Time was spent on backend robustness, where most of the rubric is.

### What I would do next

1. **Transactional outbox** (job row + outbox row in one transaction; a relay publishes) for exactly-once publishing intent.
2. **Bounded parallel fetching within a job**, and/or **per-page messages**, so a large crawl can use several workers.
3. **robots.txt and per-host rate limiting**; `<link rel="canonical">` awareness.
4. **Push progress** with SSE instead of polling; show "Retrying (attempt n of 3)" and "waiting for a worker".
5. **DLQ tooling**: list and replay dead-lettered messages from an admin endpoint.
6. **Metrics and tracing**: OpenTelemetry (pages/sec, fetch latency, queue depth, DLQ count) with alerts.
7. **API tests with `WebApplicationFactory`** and UI component tests; split migrations into a separate deploy step.

---

## Known limitations

- **Start URL that redirects to another host.** The "starting domain" is the host of the URL the user typed
  (per the spec). If that URL redirects to a different host, e.g. `example.com` → `www.example.com`, every link
  on the landing page counts as external: the crawl stops after the first page and ratios are skewed toward 0.
  *Possible fix:* after fetching the root page, adopt the final (post-redirect) host as the starting domain, and
  record that choice on the job so it is visible in the UI.
- **HTTPS → HTTP redirects are not followed.** .NET refuses to downgrade a secure request, and so do we: such
  pages are recorded with the error "Redirect not followed: HTTPS to HTTP downgrade (target)". Example:
  `quotes.toscrape.com/author/...` redirects to `http://...`. Following them would mean silently giving up
  transport security for a misconfigured site.
- **robots.txt is not honored**, and there is no per-host rate limit beyond fetching one page at a time.
  A production crawler must do both. Only crawl sites you own or that invite it (`*.toscrape.com`, `crawler-test.com`).
- **No SSRF protection.** The API fetches any http(s) URL a user submits, including private addresses such as
  `localhost` or `169.254.169.254`. A public deployment must block private and link-local ranges (after DNS
  resolution) and add authentication.
- **A job waiting for a message-level retry still shows "Running"** (it can sit for up to 2 minutes between
  attempts), which looks frozen. *Possible improvement:* store the attempt number and next-retry time on the job
  and show "Retrying (attempt 2 of 3)" in the UI.
- **No worker running → jobs wait in Pending.** Nothing is lost (the message waits in RabbitMQ and is processed
  as soon as a worker starts), but the UI cannot tell the user why. *Possible improvement:* show "waiting for a
  worker" when a job has been Pending for more than a few seconds, and alert on queue depth.
- **A job dead-lettered by RabbitMQ's delivery limit stays "Running".** That path bypasses the worker, so nothing
  marks the job `Failed`. *Fix:* a periodic sweeper that fails jobs `Running` with no progress for N minutes, or a
  small DLQ consumer that marks the referenced job `Failed`.
- **Very long crawls can exceed RabbitMQ's consumer ack timeout (30 min by default).** The worker acks only when
  the job finishes. A pathological site where every page hits the 30 s fetch budget could take ~100 min for 200
  pages; RabbitMQ would then close the channel and redeliver, looping until the delivery limit dead-letters it.
  Typical crawls take seconds. *Fix:* a job-level time budget, or per-page messages (each acked in seconds).
- **Sequential fetching.** One page at a time per job (~10 pages/s on a fast site). Simple and polite, but slow for big sites.
- **A redelivered job re-fetches its pages.** Writes are idempotent, but the replay repeats the HTTP requests
  instead of resuming from the stored frontier.
- **Credentials in `docker-compose.yml` and `appsettings.json`** are for local development only.

---

## How this was built

Built with an AI pair-programmer (Claude Code), as the brief encourages: every change was reviewed, tested and run
end to end before it was committed, and the commit history shows the order of work.
