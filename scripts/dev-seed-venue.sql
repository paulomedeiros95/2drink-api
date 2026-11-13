-- Bar de teste LOCAL (somente desenvolvimento), para provar o fluxo de check-in sem a chave do Google Places.
-- Rode como dono do banco:
--   docker compose -f docker-compose.dev.yml exec -T postgres psql -U twodrink_owner -d twodrink < scripts/dev-seed-venue.sql
-- Idempotente. Coordenadas: Praca da Se, Sao Paulo (-23.5505, -46.6333).
INSERT INTO venues (id, place_id, name, address, lat, lng, cached_at)
VALUES ('11111111-1111-1111-1111-111111111111', 'dev-place-1', 'Bar de Teste', 'Praca da Se, Sao Paulo', -23.5505, -46.6333, now())
ON CONFLICT (place_id) DO UPDATE SET cached_at = now();
