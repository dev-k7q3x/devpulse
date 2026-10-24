# Deploy na Railway

Instrukcja krok po kroku (klikanie w GitHub i Railway, weryfikacja): [`etap-2-kroki-recznie.md`](etap-2-kroki-recznie.md).

Jeden serwis z `Dockerfile` (API + SPA + Hangfire) i Railway PostgreSQL. Konfiguracja buildu i deployu
jest w repo: `railway.json` (builder Dockerfile, pre-deploy `./efbundle`, healthcheck `/health`).

## 1. GitHub OAuth App (github.com → Settings → Developer settings → OAuth Apps → New OAuth App)
Dwie aplikacje, bo OAuth App ma jeden callback URL:

| | Produkcja | Development |
|---|---|---|
| Homepage URL | `https://<domena>.up.railway.app` | `http://localhost:5173` |
| Authorization callback URL | `https://<domena>.up.railway.app/signin-github` | `http://localhost:5173/signin-github` |

Po utworzeniu: skopiuj **Client ID** i wygeneruj **Client secret**.

Development (jednorazowo):
```
dotnet user-secrets set "Auth:GitHub:ClientId" "<dev client id>" --project ./src/DevPulse.Api
dotnet user-secrets set "Auth:GitHub:ClientSecret" "<dev client secret>" --project ./src/DevPulse.Api
```

## 2. Projekt na Railway
1. New Project → Deploy from GitHub repo → `dev-k7q3x/devpulse` (gałąź `main`). Railway sam wykryje `railway.json`.
2. W projekcie: New → Database → PostgreSQL.
3. Serwis aplikacji → Settings → Networking → Generate Domain (tę domenę wpisz w produkcyjnej OAuth App).

## 3. Zmienne środowiskowe serwisu aplikacji (Variables → Raw Editor)
```
ConnectionStrings__Default=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
Auth__GitHub__ClientId=<prod client id>
Auth__GitHub__ClientSecret=<prod client secret>
DataProtection__CertificateBase64=<zawartość C:\Users\pawel\.devpulse\dataprotection-base64.txt>
DataProtection__CertificatePassword=<zawartość C:\Users\pawel\.devpulse\dataprotection-password.txt>
Admin__Logins__0=<twój login GitHub>
```
- Connection string musi być w formacie Npgsql – nie używaj `DATABASE_URL` (URI). Referencje `${{Postgres.…}}` zakładają, że serwis bazy nazywa się `Postgres`.
- `PORT` ustawia Railway – nie dodawaj go.
- Bez `Auth__GitHub__*` albo certyfikatu kontener nie wystartuje (fail fast z czytelnym komunikatem w logach).

**Certyfikat:** `C:\Users\pawel\.devpulse\dataprotection.pfx` (+ hasło i base64 obok) – przenieś do menedżera haseł.
Utrata certyfikatu = nowe klucze, czyli wylogowanie wszystkich i konieczność ponownego logowania (tokeny GitHub zostaną nadpisane).

## 4. Pierwszy deploy – co się dzieje
1. Build obrazu z `Dockerfile` (frontend → `dotnet publish` → `efbundle`).
2. Pre-deploy: `./efbundle` – migracje EF na bazie z `ConnectionStrings__Default` (blokada migracji, więc bezpieczne przy ponowieniu).
3. Start kontenera, healthcheck `GET /health` (liveness, bez sprawdzania bazy).

## 5. Weryfikacja po deployu (kryteria zakończenia etapu 2)
- [ ] `https://<domena>/health` → 200, `https://<domena>/health/ready` → 200.
- [ ] `https://<domena>/` przekierowuje na `/login`, przycisk „Zaloguj przez GitHub” → ekran zgody GitHuba ze scopes `read:user user:email repo`.
- [ ] Po zgodzie wracasz do aplikacji; w nagłówku widać login i avatar; `https://<domena>/api/auth/me` zwraca JSON z `githubTokenValid: true`, `missingScopes: []`, `isAdmin: true`.
- [ ] Odmowa na ekranie zgody → `/login` z komunikatem o anulowaniu.
- [ ] **Restart** (Deployments → ⋮ → Restart): po restarcie odśwież stronę – nadal jesteś zalogowany.
- [ ] „Wyloguj” → ekran logowania; `/api/auth/me` → 401.
- [ ] Logi serwisu: brak `ERR`/`FTL`, w logach requestów widać prawdziwe IP klienta (nie adres proxy) – potwierdza `ForwardedHeaders`.
