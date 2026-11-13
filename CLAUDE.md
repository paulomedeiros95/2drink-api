# 2DRINK API

2DRINK is a social app for tracking drinks, bar check-ins and leagues (MVP for Carnaval 2027). This repo is the .NET 10 API.

## Docs (spec and plans)

The docs repo `https://github.com/paulomedeiros95/2drink-app-docs` is a git submodule at `docs/`.
After cloning, run `git submodule update --init`.

- Spec: `docs/docs/superpowers/specs/2026-11-13-2drink-mvp-design.md`
- Plans: `docs/docs/superpowers/plans/`
- Setup guide: `docs/docs/guias/`

## Local setup

Docker must be able to pull `postgres:17` and `testcontainers/ryuk` from Docker Hub. If a pull says
"authentication required", run `docker logout` then `docker login`.

## Conventions

- Solution `TwoDrink.sln`; namespaces `TwoDrink.*`. Api -> Infrastructure -> Domain.
- Business rules live in `TwoDrink.Domain` (pure, no I/O). All limits come from `RulesOptions` (`Rules` section of `appsettings.json`).
- Rule errors return `ProblemDetails` (HTTP 422/409) with a snake_case `code` extension; the app (PT/EN) translates by `code`.
- Time always comes from the injected `TimeProvider`, never the client. Business time zone: `America/Sao_Paulo`.
- Secrets and connection strings stay out of the repo (user-secrets in dev, Key Vault in prod). Connection string key: `ConnectionStrings:Default`.
- Tests use Testcontainers (PostgreSQL) via `ApiFactory`; Docker must be running.
- Build with `dotnet build -warnaserror`; run tests with `dotnet test`.

## Auth configuration

- The API issues its own HS256 JWTs (issuer `2drink-api`, audience `2drink-app`, access 1h, refresh 60 days). The signing key is never committed: in dev run
  `dotnet user-secrets set "Jwt:SigningKey" "<random 64 chars>" --project src/TwoDrink.Api` (min 32 bytes; Key Vault in prod). Tests set their own key in `ApiFactory`.
- Google/Apple audiences come from config: `Auth:Google:ClientIds` (array) and `Auth:Apple:Audiences` (array: bundle ID / Service ID). Tests replace `IExternalTokenValidator` with a fake (`valid:<subject>`).
- Endpoints for players use the `"Player"` authorization policy (complete profile required). Tests get a ready player with `ApiFactory.CreatePlayerClientAsync(subject)`.
- Test clock (`ApiFactory.Clock`, a settable `TestClock`): `SetUtcNow`/`Advance`, may move backwards; keep it in UTC (Npgsql rejects non-zero offsets). Default start 2027-02-06T23:00Z.

## Venues (Google Places)

- `GET /venues/nearby?lat=&lng=` (Player policy) calls Google Places (New) `searchNearby` through `IPlacesClient`; results are upserted into `venues` by `place_id` and cached in `IMemoryCache` for 1h per ~110 m cell (coordinates rounded to 3 decimals). Google failures return 503 `places_unavailable`.
- The API key is never committed: in dev run `dotnet user-secrets set "Google:PlacesApiKey" "<key>" --project src/TwoDrink.Api` (Key Vault in prod). Tests replace `IPlacesClient` with `ApiFactory.Places` (`FakePlacesClient`: `NearbyCalls`, `DetailsCalls`, `Reset()`); call `Reset()` at the start of each test.
- Sponsorship windows are `[StartsAt, EndsAt)`; `VenueTierService` picks the highest tier if several overlap. Seasons are seeded in the migration and never overlap (`SeasonService`, DB exclusion constraint); there is no public seasons endpoint yet.

## Check-in intents and selfie storage

- `POST /checkins/intents` (Player policy) validates presence (`Geofence`) and cooldowns (`CheckInCooldown`, from the `check_ins` table) and returns `{ intentId, uploadUrl, expiresAt }`; the intent lives 10 min. The app PUTs the JPEG straight to Blob Storage (`x-ms-blob-type: BlockBlob`) using the write-only SAS (blob `checkins/{intentId}.jpg` in private container `selfies`; HTTPS-only unless `Storage:AllowHttpSas=true`).
- Storage connection string is never committed: in dev run `dotnet user-secrets set "Storage:ConnectionString" "<azure storage connection string or UseDevelopmentStorage=true>" --project src/TwoDrink.Api` (Key Vault in prod). The API creates the `selfies` container at startup if missing and refuses to start without the setting.
- Tests start Azurite (`mcr.microsoft.com/azure-storage/azurite`, on MCR) through `ApiFactory`, which sets `Storage:ConnectionString` and `Storage:AllowHttpSas`.
- `RulesOptions` is registered once, in `Program.cs`, from the `Rules` section of `appsettings.json` (spec defaults).
- `POST /checkins/intents/{intentId}/confirm` (Player policy, `CheckInService`) runs in one transaction under a per-user advisory lock and
  is idempotent (a confirmed intent returns its stored result). It freezes the selfie with a server-side copy to `final/{checkInId}.jpg`
  (stored in `CheckIn.PhotoBlobName`) before committing, then deletes the upload blob. Storage failures surface as
  `PhotoStorageUnavailableException` → 503 `storage_unavailable`.
- Advisory locks use the two-key form `pg_advisory_xact_lock(namespace, hashtext(id::text))`; namespaces live in `AdvisoryLocks`
  (1 ledger, 2 league slots, 3 check-ins).
- Check-in tests derive from `CheckInTestBase` (fresh player `Me` and venue `Bar` per test; `Confirm`, `Sponsor`, `EndSponsorship`,
  `SeedSeason`, `LedgerTotal`, `CheckInCount`). `ApiFactory.PhotoStorage.FailCopy` injects a storage outage;
  `ApiFactory.ReauthenticateAsync` renews the 1h token after clock jumps.

## Complements, closing and hydration

- `POST /checkins/{id}/drinks` (Player policy, `CheckInService.ComplementAsync`) takes the same per-user lock as confirm plus `FOR UPDATE` on the
  check-in; 404 `checkin_not_found` (unknown or someone else's), 422 `checkin_closed` (closed or past `ComplementsUntil`), `drink_limit` counts the total.
  Complement ledger entries carry `Reason = "complement"`, which is how a confirm replay returns only its own original points.
- `HydrationReconciler` runs inside the confirm and complement transactions (+bonus when met, `Reversal` with `hydration_goal_lost` when lost).
- `CheckInCloser` (`CloseAsync` idempotent, `SweepAsync`) only sets `ClosedAt`; `CheckInCloserJob` sweeps every 5 min (`CheckIns:CloserJobEnabled=false` in tests).
- `GET /me/ledger?before=&limit=` (Player policy): own statement, newest first, `before` = Sequence cursor, limit 1..200 (422 `invalid_paging`).

## Leagues

- `POST /leagues`, `POST /leagues/{id}/invites` (creator only, 403 otherwise), `POST /invites/{token}/accept`, `POST /leagues/{id}/leave`, `GET /me/leagues` (all Player policy, `LeagueService`).
- A membership occupies a slot while `LeftAt == null` and today (Sao Paulo) <= league end date; limit `Rules:MaxActiveLeaguesPerUser`. Create and accept run in one transaction under `pg_advisory_xact_lock(2, hashtext(userId))`; a failed accept check rolls the invite claim back.
- Invite tokens: 32 random bytes base64url; only the SHA-256 hex hash is stored. Link base from `Invites:BaseUrl`.

## Standings and trophies

- `GET /leagues/{id}/standings` (Player policy, `StandingsQuery`): one SQL query over the ledger; window per member is `[max(JoinedAt, start), end+1 00:00 SP)`, points = sum of all entry types, distinct venues from `CheckIn` entries; only `LeftAt == null` members. Competition ranking (1,1,3); unknown league or non-member -> 404 `not_member`.
- `TrophyAwarder.AwardDueAsync` (+ `TrophyAwarderJob` every 5 min, `Trophies:AwarderJobEnabled=false` in tests): per ended league, own transaction, `FOR UPDATE` on the league row, re-checks `TrophiesAwardedAt`; no champion still marks the league. `GET /me/trophies` lists a player's trophies newest first.
