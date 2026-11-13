# 2drink-api

API .NET 10 do 2DRINK (app social de check-ins de bares e ligas). Documentacao completa, convencoes e checklist de deploy em [`CLAUDE.md`](CLAUDE.md).

## Testar localmente com Swagger

Resumo (passo a passo detalhado na secao "Testar localmente (Swagger)" do [`CLAUDE.md`](CLAUDE.md)):

```
docker compose -f docker-compose.dev.yml up -d
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=twodrink;Username=twodrink_api_dev;Password=api_dev_pw" --project src/TwoDrink.Api
dotnet user-secrets set "Jwt:SigningKey" "<texto aleatorio com 64 caracteres>" --project src/TwoDrink.Api
dotnet user-secrets set "Storage:ConnectionString" "UseDevelopmentStorage=true" --project src/TwoDrink.Api
dotnet tool restore
dotnet ef database update --project src/TwoDrink.Infrastructure --startup-project src/TwoDrink.Api --connection "Host=localhost;Port=5432;Database=twodrink;Username=twodrink_owner;Password=owner_dev_pw"
docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink < scripts/dev-create-api-login.sql
dotnet run --project src/TwoDrink.Api
```

Abra <http://localhost:5088/swagger>, chame `POST /dev/token` com `{ "subject": "ana", "completeProfile": true }`, clique em **Authorize** e cole o `accessToken` (sem `Bearer`).
`GET /venues/nearby` precisa de `Google:PlacesApiKey` (chave da dona do produto); sem ela responde 503 `places_unavailable`.
Swagger, OpenAPI e `/dev/token` existem somente em Development.
