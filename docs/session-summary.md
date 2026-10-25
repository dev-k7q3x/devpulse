# Podsumowania sesji

## 2026-10-23 · Etap 2 – moduł Auth (`feat/auth-module`, bez commitu)

**Backend**
- Nowa paczka `AspNet.Security.OAuth.GitHub` 10.0.0 w `DevPulse.Api` (za zgodą).
- Application: `IAuthService`, `Models/GitHub/GitHubLogin` (`ToString` bez tokenu), `Models/GitHub/GitHubScopes` (wymagane scopes, `Missing`, `Normalize`).
- Infrastructure: `Security/TokenProtector` (Data Protection, purpose `DevPulse.GitHubAccessToken.v1`), `Security/DataProtectionKeyOptions`, `Services/AuthService`; `SecurityModule` rejestruje Data Protection (klucze w bazie, certyfikat PFX, fail fast bez certyfikatu na produkcji).
- Api: `Auth/AuthServiceCollectionExtensions` (cookie + GitHub OAuth + FallbackPolicy + antiforgery), `Auth/CurrentUser`, `Auth/GitHubClaimsMapper`, `Auth/XsrfToken`, `Auth/AntiforgeryValidationFilter`, `Auth/AuthOptions`, `Controllers/AuthController` (`GET login`, `POST logout`, `GET/DELETE me`), `OpenApi/CamelCaseQueryParametersTransformer`.
- `Program.cs`: `UseForwardedHeaders`, `UseAuthentication`/`UseAuthorization` przed `UseRateLimiter`, filtr antiforgery, anonimowe fallbacki SPA i OpenAPI.
- Konfiguracja: sekcje `Auth` i `DataProtection` w `appsettings.json` (`RequireCertificate = false` w Development), `UserSecretsId` w csproj Api.

**Frontend**
- `schema.d.ts` zregenerowany, `queryKeys.auth.me`, `useCurrentUser`, `useLogout`, `ProtectedRoute`, `LoginPage` (błędy `access_denied` i `login_failed`, `returnUrl`), `ReauthBanner`, użytkownik i przycisk „Wyloguj” w `AppLayout`, `createQueryClient` (`unauthenticated` → odświeżenie sesji), proxy `/signin-github` w Vite.

**Testy**
- Backend: 194/194 (Unit 135, Integration 59). Nowe: `TokenProtectorTests`, `SecurityModuleTests`, `GitHubScopesTests`, `GitHubLoginTests`, `GitHubClaimsMapperTests`, `CurrentUserTests`, `AuthServiceTests`, `AuthEndpointsTests`, `GitHubOAuthFlowTests` (pełny callback na fixture'ach w `Fixtures/GitHub`).
- Frontend: 33/33, lint, type-check, prettier i build zielone.

**Nie zrobione / po stronie człowieka**
- Railway (serwis, env, migration bundle), GitHub OAuth App, certyfikat PFX.
- Lokalna baza nie ma zaaplikowanej migracji `InitialCreate` – `dotnet ef database update` czeka na zgodę.
- Ręczny test: logowanie GitHubem i restart kontenera bez utraty sesji.
- Decyzje do przejrzenia: `docs/questions.md`.

## 2026-10-24 · Etap 2 – domknięcie (`feat/auth-module`, bez commitu)

- Lokalnie: `docker compose up -d`, `dotnet ef database update` (InitialCreate), smoke test API: `/api/auth/me` 401 + cookies XSRF, `/api/auth/login` 302 na GitHub, nielokalny `returnUrl` 400, `/health/ready` 200.
- `DevPulseDbContextDesignTimeFactory` – `dotnet ef` i bundle bez hosta API.
- Dockerfile: `/app/efbundle` (pre-deploy), `libgssapi-krb5-2` w runtime. `railway.json`: builder Dockerfile, `preDeployCommand ./efbundle`, healthcheck `/health`.
- Test obrazu w trybie Production (osobna baza, certyfikat PFX): bundle migruje bazę, fail fast bez certyfikatu i bez OAuth, `/health/ready` 200, SPA `/login` 200, klucz Data Protection w bazie zaszyfrowany certyfikatem, po restarcie kontenera stare cookie nadal się odszyfrowuje, liczba kluczy bez zmian, brak błędów w logach.
- `.gitattributes`, sugestie z review (questions.md, pkt 13), `docs/deploy/railway.md`, CLAUDE.md (user-secrets, efbundle).
- `/devpulse-kit:devpulse-review`: APPROVE. Wdrożone sugestie: porównanie klucza `/me` po hashu (`hashKey`), `toLocalPath` odrzuca znaki kontrolne, design-time factory rzuca wyjątek bez `ConnectionStrings__Default` w Production, cookie `Secure` zawsze poza Development (+ testy).
- Testy: backend 198/198, frontend 35/35, lint/type-check/prettier/build i `dotnet format` – zielone. Wszystko w indeksie (`git add -A`), bez commitu.

## 2026-10-25 · Etap 3 – E3A, E3B, E3C (równolegle w worktree, scalone do `main`)

- Trzy ścieżki w osobnych worktree (subagenci), koordynacja i scalanie w sesji głównej. Postęp i decyzje: `docs/progress/E3A.md`, `E3B.md`, `E3C.md`; rejestracje DI: `docs/di/github.md`, `digest.md`, `settings.md`.
- Incydenty: Docker Desktop padł przy trzech równoległych zestawach Testcontainers (restart w sesji głównej, potem testy integracyjne kolejno przez blokadę `.claude/worktrees/.docker-lock`); limit sesji API przerwał wszystkie ścieżki – wznowione po resecie bez utraty pracy.
- Weryfikacja po złożeniu (przed merge): backend 390/390 (+2 External pominięte), frontend 47/47, `dotnet format`, lint, type-check, prettier, build; jedyny konflikt – generowany `schema.d.ts` (regenerowany).
- Merge: PR #4 (readiness timeout + `.editorconfig`), #5 (E3A), #6 (E3B), E3C (ten PR, z regenerowanym `schema.d.ts` i tą dokumentacją).
- Deploy nadal odłożony (Railway wstrzymał rejestracje); `App__PublicBaseUrl` dopisane do instrukcji deployu.
