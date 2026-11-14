# 2DRINK API

2DRINK is a social app for tracking drinks, bar check-ins and leagues (MVP for Carnaval 2027). This repo is the .NET 10 API.

## Docs (spec and plans)

The docs repo `https://github.com/paulomedeiros95/2drink-app-docs` is a git submodule at `docs/`.
After cloning, run `git submodule update --init`.

- Spec: `docs/docs/superpowers/specs/2026-11-13-2drink-mvp-design.md`
- Plans: `docs/docs/superpowers/plans/`
- Setup guide: `docs/docs/guias/`

## Local setup

If `dotnet ef` fails with "Uma política de Controle de Aplicativo bloqueou este arquivo" (Smart App Control), add `--configuration Release`.

Docker must be able to pull `postgres:17` and `testcontainers/ryuk` from Docker Hub. If a pull says
"authentication required", run `docker logout` then `docker login`.

## Conventions

- Solution `TwoDrink.sln`; namespaces `TwoDrink.*`. Api -> Infrastructure -> Domain.
- Business rules live in `TwoDrink.Domain` (pure, no I/O). All limits come from `RulesOptions` (`Rules` section of `appsettings.json`).
- Rule errors return `ProblemDetails` (HTTP 422) with a snake_case `code` extension; the app (PT/EN) translates by `code`.
- Time always comes from the injected `TimeProvider`, never the client. Business time zone: `America/Sao_Paulo`.
- Secrets and connection strings stay out of the repo (user-secrets in dev, Key Vault in prod). Connection string key: `ConnectionStrings:Default`.
- Tests use Testcontainers (PostgreSQL) via `ApiFactory`; Docker must be running.
- Build with `dotnet build -warnaserror`; run tests with `dotnet test`.

## Auth configuration

- The API issues its own HS256 JWTs (issuer `2drink-api`, audience `2drink-app`, access 1h, refresh 60 days). The signing key is never committed: in dev run
  `dotnet user-secrets set "Jwt:SigningKey" "<random 64 chars>" --project src/TwoDrink.Api` (min 32 bytes; Key Vault in prod). Tests set their own key in `ApiFactory`.
- Google/Apple audiences come from config: `Auth:Google:ClientIds` (array) and `Auth:Apple:Audiences` (array: bundle ID / Service ID). Tests replace `IExternalTokenValidator` with a fake (`valid:<subject>`).
- Endpoints for players use the `"Player"` authorization policy (complete profile required; otherwise 403 `profile_incomplete`). Tests get a ready player with `ApiFactory.CreatePlayerClientAsync(subject)`.
- The fallback authorization policy is "authenticated user": a new endpoint is private unless it says otherwise. `/auth/*` and `/health` are `AllowAnonymous`.
- Login: a bad token is 401 `invalid_token` (`SecurityTokenException` / `InvalidJwtException`); any other validator failure (missing audiences, key endpoint down) is logged at Error and returns 503 `auth_unavailable`. Apple: an unknown `kid` refreshes the cached JWKS and retries once. The fake validator: `valid:<subject>` ok, `unavailable:…` throws `InvalidOperationException`, anything else `SecurityTokenException`.
- Startup validation (`RequiredConfigValidator`, `ValidateOnStart`): `Jwt:SigningKey` >= 32 bytes, `Jwt:Issuer`, `Jwt:Audience`, `ConnectionStrings:Default`, and outside Development at least one `Auth:Google:ClientIds` / `Auth:Apple:Audiences` entry. `ApiFactory` runs in the `Testing` environment with dummy audiences.
- Rate limiting: global fixed window of `RateLimiting:PermitLimit` (default 120) per minute per `sub`, else per client IP; `/health` is exempt. Pipeline order: `UseForwardedHeaders` -> `UseAuthentication` -> `UseRateLimiter` -> `UseAuthorization`.
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
  (stored in `CheckIn.PhotoBlobName`) before committing, then deletes the upload blob. The upload's size/content type is checked
  before the copy (`photo_invalid`) and the frozen copy is checked again. Storage failures surface as
  `PhotoStorageUnavailableException` → 503 `storage_unavailable`.
- `IntentCleanup` (+ `IntentCleanupJob` hourly, `CheckIns:IntentCleanupJobEnabled=false` in tests): deletes the upload blob and the row of
  intents expired for more than 1h and never confirmed; best effort per intent (a failure keeps the row for the next run).
- Advisory locks use the two-key form `pg_advisory_xact_lock(namespace, hashtext(id::text))`; namespaces live in `AdvisoryLocks`
  (1 ledger, 2 league slots, 3 check-ins).
- Check-in tests derive from `CheckInTestBase` (fresh player `Me` and venue `Bar` per test; `Confirm`, `Sponsor`, `EndSponsorship`,
  `SeedSeason`, `LedgerTotal`, `CheckInCount`). `ApiFactory.PhotoStorage.FailCopy` / `FailDelete` inject a storage outage
  (`CopyCalls` counts copies);
  `ApiFactory.ReauthenticateAsync` renews the 1h token after clock jumps.

## Complements, closing and hydration

- `POST /checkins/{id}/drinks` (Player policy, `CheckInService.ComplementAsync`) takes the same per-user lock as confirm plus `FOR UPDATE` on the
  check-in; 404 `checkin_not_found` (unknown or someone else's), 422 `checkin_closed` (closed or past `ComplementsUntil`), `drink_limit` counts the total.
  Complement ledger entries carry `Reason = "complement"`, which is how a confirm replay returns only its own original points.
- `HydrationReconciler` runs inside the confirm and complement transactions (+bonus when met, `Reversal` with `hydration_goal_lost` when lost).
- `CheckInCloser` (`CloseAsync` idempotent, `SweepAsync`) only sets `ClosedAt`; `CheckInCloserJob` sweeps every 5 min (`CheckIns:CloserJobEnabled=false` in tests).
- `GET /me/ledger?before=&limit=` (Player policy): own statement, newest first, `before` = Sequence cursor, limit 1..200 (422 `invalid_paging`).

## Leagues

- `POST /leagues`, `POST /leagues/{id}/invites` (creator only, 403 otherwise; 422 `league_ended` after the end date), `POST /invites/{token}/accept`, `POST /leagues/{id}/leave`, `GET /me/leagues` (all Player policy, `LeagueService`).
- A membership occupies a slot while `LeftAt == null` and today (Sao Paulo) <= league end date; limit `Rules:MaxActiveLeaguesPerUser`. Create and accept run in one transaction under `pg_advisory_xact_lock(2, hashtext(userId))`; a failed accept check rolls the invite claim back.
- `POST /leagues/{id}/renew` `{name?, startDate, endDate}` (creator only; 404 `league_not_found`, 403 `not_league_creator`, 422 `league_not_ended`): new league with the active members and the cover; the creator is always included (even after leaving the ended league); slot locks for all of them taken in ascending id order; creator without slot -> `league_limit` (nothing created), other members without slot listed in `skippedUserIds`. A league is renewed at most once: the old row is locked `FOR UPDATE` and `League.RenewedAsLeagueId` makes a repeated renew return the existing renewal (same shape, `skippedUserIds` empty).
- Invite tokens: 32 random bytes base64url; only the SHA-256 hex hash is stored. Link base from `Invites:BaseUrl`.

## Standings and trophies

- `GET /leagues/{id}/standings` (Player policy, `StandingsQuery`): one SQL query over the ledger; window per member is `[max(JoinedAt, start), end+1 00:00 SP)`, points = sum of all entry types, but a `Reversal` is windowed by the `CreatedAt` of the entry it reverses (PK lookup in the same query), not its own, so it counts in a league only if the original did; distinct venues from `CheckIn` entries; members ranked are those with `LeftAt == null || LeftAt >= end` (membership at the league end). Competition ranking (1,1,3); unknown league or non-member -> 404 `not_member`.
- Consequence: a reversal created after a league ended can still change that ended league's displayed standings (it follows its original entry); the Golden Trophy already awarded is never revoked, so standings and trophy may diverge (accepted).
- `TrophyAwarder.AwardDueAsync` (+ `TrophyAwarderJob` every 5 min, `Trophies:AwarderJobEnabled=false` in tests): per ended league, own transaction, `FOR UPDATE` on the league row, re-checks `TrophiesAwardedAt`; no champion still marks the league. `GET /me/trophies` lists a player's trophies newest first.

## Ledger

- `ledger_entries` is append-only: a trigger rejects UPDATE, DELETE and TRUNCATE for everyone (owner included), and the
  `twodrink_app` role only has SELECT/INSERT on it. The API connects as a LOGIN role that is a member of `twodrink_app`,
  never as the owner (tests: `ApiFactory.AppConnectionString`; owner only for migrations, seeding and tamper tests).
- `LedgerWriter.AppendAsync` requires an ambient READ COMMITTED transaction (throws when there is none; higher isolation levels are not supported) and serialises writers per
  account with `pg_advisory_xact_lock(1, hashtext(accountId))`. Advisory-lock namespaces (`AdvisoryLocks`): 1 ledger,
  2 league slots, 3 check-ins. Each entry is hash-chained (`PrevHash` -> `Hash`, timestamps truncated to microseconds).
- `AppendAsync(..., at)` stamps the caller's business time (confirm/complement pass the `now` they validated with); it
  defaults to the clock at append time.
- Ledger lookups by check-in or reversal must also filter by `AccountId` (partial indexes on `check_in_id` and
  `reverses_entry_id` exist for them).
- `LedgerVerifier` / `LedgerVerificationJob` (daily, `Ledger:VerificationJobEnabled=false` in tests) logs Critical on a
  broken chain and Information "Ledger integrity check passed" on success.

## Deploy checklist

- Run `dotnet ef database update` as the database owner role. `ALTER DEFAULT PRIVILEGES` only applies to tables created by
  the role that ran it, so always migrate with that same owner role (otherwise new tables are invisible to `twodrink_app`).
- Create the API login as a member of `twodrink_app` (`CREATE ROLE <api_login> LOGIN PASSWORD '...' IN ROLE twodrink_app`)
  and put it in `ConnectionStrings:Default`; never connect the API as the owner.
- The Linux image must include `tzdata` and ICU (`BusinessTime` uses `America/Sao_Paulo`; no invariant globalization).
- Behind a reverse proxy / load balancer (mandatory for correct per-IP rate limiting): set `ForwardedHeaders:Enabled=true`
  and list the proxy in `ForwardedHeaders:KnownProxies` (IPs) or `ForwardedHeaders:KnownNetworks` (CIDR). Alternative when the
  platform's front-end IPs are not fixed and the app is only reachable through it: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
  (trusts any proxy). Use one or the other, never both.
- Azure Storage lifecycle rule on the `selfies` container: delete blobs with prefix `checkins/` 1 day after last modification
  (safety net behind `IntentCleanupJob`; never apply it to `final/`).
- Required configuration (Key Vault / app settings): `ConnectionStrings:Default`, `Jwt:SigningKey` (>= 32 bytes), `Jwt:Issuer`,
  `Jwt:Audience`, `Auth:Google:ClientIds`, `Auth:Apple:Audiences`, `Google:PlacesApiKey`, `Storage:ConnectionString`,
  `Invites:BaseUrl`. The host refuses to start without the JWT keys, the connection string, the storage connection string and
  (outside Development) at least one Google/Apple audience.
- Alerts: on any Critical log from `LedgerVerificationJob`, AND on "no `Ledger integrity check passed` in the last 48h"
  (the job may be silently failing or not running).
- Seasons are seeded only up to 2027-06-30; later seasons need the admin panel or a manual insert into `seasons` (as owner),
  or league creation fails with `dates_outside_season` and check-ins get no badges.

## Testar localmente (Swagger)

Passo a passo para quem nao e programador, no Windows. Tudo roda na sua maquina; nada disso existe em producao
(Swagger, OpenAPI e `POST /dev/token` so existem em `Development`). Pre-requisitos: Docker Desktop aberto, .NET 10 SDK, Git Bash ou PowerShell na pasta do repo.

1. **Subir Postgres e Azurite** (armazenamento das selfies):
   `docker compose -f docker-compose.dev.yml up -d`
2. **Segredos locais** (ficam no seu usuario do Windows, nunca no Git). Troque o que estiver entre `<...>`:
   ```
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=twodrink;Username=twodrink_api_dev;Password=api_dev_pw" --project src/TwoDrink.Api
   dotnet user-secrets set "Jwt:SigningKey" "<texto aleatorio com 64 caracteres>" --project src/TwoDrink.Api
   dotnet user-secrets set "Storage:ConnectionString" "UseDevelopmentStorage=true" --project src/TwoDrink.Api
   dotnet user-secrets set "Google:PlacesApiKey" "<chave do Google Places da dona do produto>" --project src/TwoDrink.Api
   ```
   Apenas `Google:PlacesApiKey` e opcional (a API inicia sem ela); `Storage:ConnectionString` e obrigatoria. Sem `Google:PlacesApiKey`, `GET /venues/nearby` responde 503 `places_unavailable`
   (veja o passo 9). Em Development nao precisa de `Auth:Google:ClientIds` / `Auth:Apple:Audiences`
   (so seriam necessarios para login real com Google/Apple).
   O que nao e segredo ja vem em `src/TwoDrink.Api/appsettings.Development.json` (`DevAuth:Enabled`, `Storage:AllowHttpSas`, `Invites:BaseUrl`...). Os servicos do docker compose escutam so em 127.0.0.1.
3. **Criar as tabelas** (como DONO do banco, `twodrink_owner`):
   `dotnet tool restore`
   `dotnet ef database update --project src/TwoDrink.Infrastructure --startup-project src/TwoDrink.Api --connection "Host=localhost;Port=5432;Database=twodrink;Username=twodrink_owner;Password=owner_dev_pw"`
   (se o Windows bloquear com "Controle de Aplicativo", acrescente `--configuration Release`).
4. **Criar o login da API** (a API nunca conecta como dono; rode depois do passo 3, pode repetir sem problema):
   `docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink < scripts/dev-create-api-login.sql`
   (no PowerShell, use: `Get-Content scripts/dev-create-api-login.sql | docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink`)
5. **Rodar a API**: `dotnet run --project src/TwoDrink.Api` (perfil `http`, porta **5088**, abre o navegador em `/swagger`).
   Se o navegador nao abrir: `http://localhost:5088/swagger`. O documento OpenAPI fica em `http://localhost:5088/openapi/v1.json`.
6. **Entrar sem Google/Apple**: em `POST /dev/token` clique em "Try it out" e envie
   `{ "subject": "ana", "displayName": "Ana", "completeProfile": true }`. A resposta traz `accessToken`.
   `completeProfile: true` deixa o perfil adulto completo (necessario para quase todos os endpoints).
   Use outro `subject` (`bia`, `caio`...) para simular outros jogadores.
7. **Autorizar**: clique no botao **Authorize** (cadeado, no topo), cole **somente** o `accessToken` (sem a palavra `Bearer`; o Swagger a coloca)
   e confirme. O token vale 1h; depois repita o passo 6 (ou use `POST /auth/refresh`).
8. **Testar**: `GET /me` (200 com seu nome), `GET /me/ledger`, `GET /me/leagues`.
9. **Bares** — `GET /venues/nearby?lat=-23.55&lng=-46.63` precisa de uma chave real do Google Places (`Google:PlacesApiKey`, e a dona do produto quem tem);
   sem ela volta 503 `places_unavailable`. Para testar check-in sem a chave, crie um bar de teste (como dono):
   `docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink < scripts/dev-seed-venue.sql`
   (no PowerShell: `Get-Content scripts/dev-seed-venue.sql | docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink`)
   Ele cria o "Bar de Teste", id `11111111-1111-1111-1111-111111111111`, em lat `-23.5505`, lng `-46.6333`.
10. **Check-in** (3 passos):
    1. `POST /checkins/intents` com `{ "venueId": "11111111-1111-1111-1111-111111111111", "lat": -23.5505, "lng": -46.6333, "accuracyM": 10, "isMocked": false }`.
       Copie `intentId` e `uploadUrl` (valem 10 min). Longe do bar (mais de 70 m) dara 422 `out_of_range`.
    2. Envie a foto JPEG direto ao armazenamento (nao passa pelo Swagger; terminal, colando o `uploadUrl` entre aspas):
       `curl -X PUT "<uploadUrl>" -H "x-ms-blob-type: BlockBlob" -H "Content-Type: image/jpeg" --data-binary @selfie.jpg` (deve responder 201).
    3. `POST /checkins/intents/{intentId}/confirm` com `{ "lat": -23.5505, "lng": -46.6333, "accuracyM": 10, "isMocked": false, "drinks": [ { "type": "Beer", "quantity": 1 } ] }`.
       Tipos de bebida: Beer, Drink, Gin, Shot, Wine, Water, Juice, Soda, Whey. Depois veja `GET /me/ledger`; `POST /checkins/{checkInId}/drinks` adiciona bebidas.
    Regras de tempo (1h entre check-ins em bares diferentes, 15h no mesmo bar) valem de verdade: um segundo check-in logo em seguida da 422 `cooldown_same_venue` ou `cooldown_any_venue`.
11. **Ligas**: `POST /leagues` com `{ "name": "Liga Teste", "startDate": "2026-11-20", "endDate": "2026-12-20" }` (as datas precisam caber numa temporada;
    hoje existem 2026 2o semestre e 2027 1o semestre); `POST /leagues/{id}/invites` gera o link; com **outro** `subject` (novo token + Authorize),
    `POST /invites/{token}/accept` entra na liga (cole so o `token`, o final do link); `GET /leagues/{id}/standings` mostra o ranking.
12. **Parar tudo**: `Ctrl+C` na janela da API e `docker compose -f docker-compose.dev.yml down` (os dados do banco e das fotos sao apagados; nao ha volumes).

Notas: `POST /dev/token` (alem disso, so aceita chamadas vindas de localhost) so existe com `ASPNETCORE_ENVIRONMENT=Development` **e** `DevAuth:Enabled=true`; fora disso a rota nao existe (usuarios `dev:<subject>` sao criados como Google). Chamadas anonimas a rotas que
nao existem respondem 401 (politica padrao exige login), nao 404.
