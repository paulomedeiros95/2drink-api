# 2DRINK API

2DRINK is a social app for tracking drinks, bar check-ins and leagues (MVP for Carnaval 2027). This repo is the .NET 10 API.

## Docs (spec and plans)

The spec and implementation plans live in the docs repo `https://github.com/paulomedeiros95/2drink-app-docs`
(locally at `C:/Source/Projects/2DRINK/docs/2drink-app-docs`):

- Spec: `docs/superpowers/specs/2026-11-13-2drink-mvp-design.md`
- Plans: `docs/superpowers/plans/`

Once the docs repo is pushed it will be added as a git submodule at `docs/` (so the paths become `docs/docs/superpowers/...`).

## Conventions

- Solution `TwoDrink.sln`; namespaces `TwoDrink.*`. Api -> Infrastructure -> Domain.
- Business rules live in `TwoDrink.Domain` (pure, no I/O). All limits come from `RulesOptions` (`Rules` section of `appsettings.json`).
- Rule errors return `ProblemDetails` (HTTP 422/409) with a snake_case `code` extension; the app (PT/EN) translates by `code`.
- Time always comes from the injected `TimeProvider`, never the client. Business time zone: `America/Sao_Paulo`.
- Secrets and connection strings stay out of the repo (user-secrets in dev, Key Vault in prod). Connection string key: `ConnectionStrings:Default`.
- Tests use Testcontainers (PostgreSQL) via `ApiFactory`; Docker must be running.
- Build with `dotnet build -warnaserror`; run tests with `dotnet test`.
