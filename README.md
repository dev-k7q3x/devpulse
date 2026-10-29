# DevPulse

Dashboard kondycji repozytoriów GitHub i codzienny digest email z priorytetami AI.
Jeden ekran z otwartymi PR-ami, issue, statusem CI i ostatnią aktywnością każdego repo, a o wybranej godzinie mail
z podsumowaniem i maks. 5 priorytetami „co wymaga uwagi dziś” (Claude, z regułowym fallbackiem).

Projekt powstał od zera z [Claude Code](https://code.claude.com/docs) w 16-częściowej serii
[Claude Code na plukasiewicz.net](https://www.plukasiewicz.net/ClaudeCode/Introduction). Skille i hooki z serii są
w osobnym repozytorium: [plukasiewicz/devpulse-tools](https://github.com/plukasiewicz/devpulse-tools).

Zakres produktu: [`docs/requirements.md`](docs/requirements.md). Architektura i decyzje: [`claude-sessions/`](claude-sessions/)
(plany 01–04) oraz [`docs/questions.md`](docs/questions.md).

## Stack
.NET 10 Web API · React 19 + TypeScript + Vite · PostgreSQL + EF Core · Hangfire (PostgreSQL) ·
GitHub OAuth (cookie + Data Protection) · Anthropic API (`claude-sonnet-5-5`) · SendGrid · jeden obraz Docker (API + SPA + joby).

```
src/DevPulse.Domain          encje, value objects – bez zależności
src/DevPulse.Application     interfejsy, DTO, walidatory, czysta logika digestu i harmonogramu
src/DevPulse.Infrastructure  EF Core, GitHub (REST + GraphQL), Anthropic, SendGrid, joby Hangfire, Data Protection
src/DevPulse.Api             kontrolery, auth, rate limiting, nagłówki bezpieczeństwa, panel /hangfire, SPA
frontend/                    SPA (React Router, TanStack Query, typy z OpenAPI)
tests/DevPulse.Tests         xUnit: Unit, Integration (Testcontainers), External (prawdziwe API, ręcznie)
```

## Uruchomienie lokalne

Wymagania: .NET SDK 10 (wersja w `global.json`), Node 24, Docker Desktop.

```powershell
docker compose up -d                                   # PostgreSQL :5432
dotnet tool restore
dotnet ef database update --project ./src/DevPulse.Infrastructure --startup-project ./src/DevPulse.Api

# jednorazowo – deweloperska GitHub OAuth App (callback http://localhost:5173/signin-github)
dotnet user-secrets set "Auth:GitHub:ClientId" "<client id>" --project ./src/DevPulse.Api
dotnet user-secrets set "Auth:GitHub:ClientSecret" "<client secret>" --project ./src/DevPulse.Api

dotnet run --project ./src/DevPulse.Api                # API :5080 (OpenAPI: /openapi/v1.json)
cd frontend; npm ci; npm run dev                       # SPA :5173 – wejdź tutaj, nie na :5080
```

Bez `Auth:GitHub:*` aplikacja celowo nie startuje (fail fast). Wysyłka maili i priorytety AI wymagają kluczy
(`SendGrid:ApiKey`, `SendGrid:FromEmail`, `ANTHROPIC_API_KEY`) – bez nich mail kończy się błędem konfiguracji,
a digest używa priorytetów regułowych. Panel jobów: `/hangfire` (tylko loginy z `Admin:Logins`).

## Testy i jakość

```powershell
dotnet test ./tests/DevPulse.Tests --filter Category=Unit
dotnet test ./tests/DevPulse.Tests --filter Category=Integration   # Docker (Testcontainers)
dotnet format src/DevPulse.sln --verify-no-changes
cd frontend; npm run lint; npm run type-check; npm test; npm run build
```

Testy `Category=External` (prawdziwe Anthropic i SendGrid, płatne) uruchamiają się tylko z `DEVPULSE_RUN_EXTERNAL=1`.
CI (`.github/workflows/ci.yml`): backend (build, format, Unit, Integration), frontend, obraz Docker.

Po każdej zmianie DTO: uruchom API i `cd frontend; npm run gen:api` (regeneruje `src/api/schema.d.ts`).

## Deploy i operacje
- Pierwsze uruchomienie produkcji krok po kroku: [`docs/deploy/kroki-reczne.md`](docs/deploy/kroki-reczne.md)
- Konfiguracja i zmienne środowiskowe: [`docs/deploy/railway.md`](docs/deploy/railway.md)
- Alternatywa bez Railway: [`docs/deploy/render.md`](docs/deploy/render.md) (`render.yaml`, plan Free)
- Runbook (deploy, rollback, rotacja sekretów, incydenty): [`docs/deploy/runbook.md`](docs/deploy/runbook.md)

## Licencja

[MIT](LICENSE)
