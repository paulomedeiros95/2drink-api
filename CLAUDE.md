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
