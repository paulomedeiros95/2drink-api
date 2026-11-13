-- Login LOCAL da API (somente desenvolvimento). Rode DEPOIS de `dotnet ef database update`, como dono do banco:
--   docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink < scripts/dev-create-api-login.sql
-- Idempotente: pode rodar varias vezes. A API nunca deve conectar como o dono.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'twodrink_api_dev') THEN
        CREATE ROLE twodrink_api_dev LOGIN PASSWORD 'api_dev_pw' IN ROLE twodrink_app;
    ELSE
        ALTER ROLE twodrink_api_dev LOGIN PASSWORD 'api_dev_pw';
        GRANT twodrink_app TO twodrink_api_dev;
    END IF;
END
$$;
