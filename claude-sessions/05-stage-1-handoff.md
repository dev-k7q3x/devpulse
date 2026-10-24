# DevPulse – przekazanie po Etapie 1 (wejście do Etapu 2)

> Sesja 05 · 2026-10-23 · stan po scaleniu Etapu 1 do `main` (PR #1 szkielet, PR #2 fundament, merge `264cbf3`).
> Dołącz ten plik do pracy nad Etapem 2 (plan 04, „Auth i pierwszy deploy”), żeby nic nie umknęło.

## 1. Stan na `main`
| Obszar | Stan |
|---|---|
| Testy backendu | `dotnet test ./tests/DevPulse.Tests`: 141/141 (Unit + Integration na Testcontainers) |
| Testy frontendu | lint, type-check, `npm test` 14/14, `npm run build` zielone |
| CI | `.github/workflows/ci.yml`: backend (build, `dotnet format --verify-no-changes`, Unit, Integration), frontend, obraz Dockera; wszystko zielone na PR #2 |
| Docker | `docker build .` przechodzi; kontener na PostgreSQL z compose: `/health` 200, `/health/ready` 200, SPA serwowane, `/api/*` → 404 ProblemDetails |
| Review | `/devpulse-kit:devpulse-review`: APPROVE (sugestie w sekcji 4) |

## 2. Co jest gotowe (Etap 1, plan 04)
- **Domain** (`src/DevPulse.Domain`)
  - encje: `User`, `UserSettings`, `TrackedRepository`, `RepositorySnapshot` (+ `OpenPullRequest`), `Digest` (+ `DigestPriority`, `DigestRepositorySummary`),
  - enumy: `CiStatus`, `DigestStatus`, `PrioritySeverity`; value objecty: `SendHour`, `RepoFullName`, `DigestAiUsage`,
  - prywatne settery i metody stanu (`User.Create/RecordLogin/MarkTokenInvalid`, `UserSettings.CreateDefault/UpdateSchedule/StartEmailChange/CancelEmailChange/TryConfirmPendingEmail`, `Digest.CreatePending/RecordContent/MarkSent/MarkFailed`, …),
  - czas tylko UTC (offset 0), sprawdzany przez wewnętrzny `Guard.Utc`; id przez `Guid.CreateVersion7()`.
- **Application** (`src/DevPulse.Application`)
  - `Interfaces/`: wszystkie 11 interfejsów z planu 01 (bez implementacji),
  - `DTOs/`: DTO z planu 03 (`Auth`, `Repositories`, `Dashboard`, `Settings`, `Digests`); pola `githubTokenValid` i `githubRepoId` mają `[JsonPropertyName]` zgodnie z kontraktem,
  - `Validators/`: walidator dla każdego Requestu (`AuthLoginRequest`, `AvailableRepositoriesRequest`, `RepositoryAddRequest`, `SettingsUpdateRequest`, `EmailChangeRequest`, `EmailConfirmRequest`),
  - `Errors/`: `ProblemTypes` (slugi z tabeli 0.1) i hierarchia `DevPulseException` (`NotFound`, `Conflict`, `LimitExceeded`, `InvalidToken`, `GitHubTokenInvalid`, `GitHubUnavailable`, `GitHubRateLimited` z `RetryAfter`),
  - `Options/`: `RepositoriesOptions`, `SnapshotOptions`, `AdminOptions`; `Digests/`: modele czystej logiki digestu; `Models/GitHub/`: modele `IGitHubClient`.
- **Infrastructure** (`src/DevPulse.Infrastructure`)
  - `Persistence/`: `DevPulseDbContext` (implementuje też `IDataProtectionKeyContext`), konfiguracje wg planu 02, migracja `InitialCreate`,
  - `Jobs/HangfireHealthCheck`; Hangfire na PostgreSQL (schemat `hangfire`, `PrepareSchemaIfNecessary`), serwer w procesie API,
  - `DependencyInjection/`: plik na moduł – `Persistence`, `Jobs`, `Validation`, `Security`, `GitHub`, `Dashboard`, `Settings`, `Digest`, `DigestHistory`; `AddInfrastructure()` woła je wszystkie.
- **Api** (`src/DevPulse.Api`)
  - `Program.cs`: Serilog, ProblemDetails ze slugami, `TimeProvider`, kontrolery z `JsonStringEnumConverter`, OpenAPI (Development), health checks, SPA fallback,
  - `Errors/DevPulseExceptionHandler`, `Validation/ValidationFilter` (400 `validation`, klucze camelCase),
  - `RateLimiting/`: 7 polityk z planu 03 §7.1, limity w sekcji `RateLimiting` w `appsettings.json`; `default` dodaje `DefaultRateLimitingConvention` tylko akcjom bez własnej polityki,
  - `Auth/DevPulseClaimTypes.UserId` (`devpulse:user_id`) – claim, po którym rate limiter partycjonuje użytkowników.
- **Testy** (`tests/DevPulse.Tests`)
  - `Builders/` (`UserBuilder`, `UserSettingsBuilder`, `TrackedRepositoryBuilder`, `RepositorySnapshotBuilder`, `DigestBuilder`, `TestClock`),
  - `Integration/DevPulseApiFactory` (świeży kontener + `MigrateAsync`, `CreateDbContext()` do seedowania), `UnreachableDatabaseApiFactory`,
  - `Integration/TestSupport/PipelineTestController` – kontroler tylko testowy (`/api/__test/*`) do sprawdzania walidacji, mapowania błędów i rate limitingu.
- **Ops:** `Dockerfile` (port z `PORT`, domyślnie 8080; użytkownik nie-root), `.dockerignore`, `docker-compose.yml` (PostgreSQL 17), lokalny tool `dotnet-ef` w `dotnet-tools.json` (`dotnet tool restore`).

## 3. Odstępstwa od zaakceptowanych planów
| Plan | Odstępstwo | Powód |
|---|---|---|
| 01 | `Application/Digests/` zamiast `Digest/` (CLAUDE.md zaktualizowany) | Namespace `Digest` przesłaniał encję `Digest` w całym Application. |
| 02 | `open_pull_requests`: `jsonb` + CHECK `IS NOT NULL` zamiast `NOT NULL DEFAULT '[]'` | EF nie pozwala oznaczyć owned collection (`OwnsMany().ToJson()`) jako wymaganej. Encja zawsze zapisuje listę. |
| 02 | Kolumny z defaultem w bazie mają `ValueGeneratedNever()` (`HasDatabaseDefault`) | Bez tego EF traktuje wartość CLR-ową domyślną jako „nieustawioną”: godzina 0 zapisałaby się jako 7, `false` jako `true`. Pilnuje tego test `Should_PersistMidnightSendHourAndFalseFlags_When_ValuesEqualClrDefaults`. |
| 02 | FK `repository_snapshots → tracked_repositories` ma nazwę obciętą do 63 znaków | Limit identyfikatorów PostgreSQL. |
| – | `Newtonsoft.Json` 13.0.4 przypięty w Infrastructure | Hangfire wciąga przechodnio 11.0.1 z poważną luką (GHSA-5crp-9r3c-p9vr); przy `TreatWarningsAsErrors` build nie przechodził. |
| 01 | `IDigestComposer` / `IAiPrioritizer` / `IEmailSender` mają sygnatury zaprojektowane w E1 | Plan wymieniał tylko nazwy. Ścieżka E3B może je doprecyzować. |

## 4. Do zrobienia w Etapie 2 – checklista
**Obowiązkowe przed pierwszym deployem (z review PR #2):**
- [ ] `UseForwardedHeaders` (Railway za proxy) – bez tego polityka `anonymous` jest jedna dla całego serwisu, a callback `/signin-github` dostanie zły scheme. Plan 04 miał to w E6; przenieść do E2.
- [ ] `UseAuthentication()` i `UseAuthorization()` **między** `UseRouting()` a `UseRateLimiter()` w `Program.cs` (miejsce oznaczone komentarzem) – inaczej limity liczą się per IP, nie per użytkownik.
- [ ] Przy logowaniu zapisać w cookie claim `DevPulseClaimTypes.UserId` (id DevPulse, nie GitHuba) – z niego korzystają rate limiter i przyszły `CurrentUser`.

**Zakres Etapu 2 wg planu 04, z tym, co już istnieje:**
- [ ] GitHub OAuth (`AspNet.Security.OAuth.GitHub` – nowa paczka, wymaga zgody), scopes `read:user user:email repo`, cookie HttpOnly + SameSite=Lax, `OnRedirectToLogin` → 401 dla `/api/*`, `FallbackPolicy = RequireAuthenticatedUser`. Health checks już mają `.AllowAnonymous()`.
- [ ] Data Protection: `AddDataProtection().PersistKeysToDbContext<DevPulseDbContext>().ProtectKeysWithCertificate(...)` – tabela `data_protection_keys` już jest w `InitialCreate`; rejestracja w `SecurityModule` (Infrastructure).
- [ ] `ITokenProtector` w `Infrastructure/Security` (purpose `DevPulse.GitHubAccessToken.v1`); interfejs już jest.
- [ ] `ICurrentUser` (`CurrentUser` w `Api/Auth`); interfejs już jest – `UserId` rzuca, gdy brak uwierzytelnienia.
- [ ] Upsert `User` + `UserSettings` przy logowaniu: `User.Create` / `RecordLogin`, `UserSettings.CreateDefault(userId, primaryVerifiedEmail, now)`.
- [ ] `AuthController`: `GET /api/auth/login` (walidator `AuthLoginRequestValidator` gotowy, polityka `anonymous`), `POST /api/auth/logout`, `GET /api/auth/me` (`CurrentUserResponse` gotowy; `isAdmin` z `AdminOptions`, już bindowanych w `SecurityModule`), `DELETE /api/auth/me` (polityka `sensitive`; kaskada w bazie gotowa i przetestowana).
- [ ] Antiforgery double-submit (cookie `XSRF-TOKEN`, nagłówek `X-XSRF-TOKEN`) – frontendowy `httpClient` już wysyła nagłówek przy POST/PUT/DELETE. Helper klienta testowego musi go dokładać (tests/CLAUDE.md).
- [ ] Frontend: `LoginPage`, `useCurrentUser`, `ProtectedRoute`, baner „Zaloguj się ponownie”, logout, `/login?error=access_denied`; klucze w `queryKeys.ts`; po dodaniu endpointów regenerować `schema.d.ts` (`npm run gen:api`).
- [ ] Railway: serwis z Dockerfile + PostgreSQL, EF migration bundle jako pre-deploy, healthcheck `/health`.
- [ ] Testy z planu 04 dla E2: Unit `TokenProtector` (round-trip, podmieniony ciphertext), mapowanie claimów, `returnUrl`; Integration: niezalogowany `/api/auth/me` → 401 `unauthenticated` (nie 302), POST bez `X-XSRF-TOKEN` → 400, `DELETE /api/auth/me` kasuje kaskadowo; ręcznie: restart kontenera nie unieważnia sesji.

**Rzeczy do przygotowania po Twojej stronie:**
- GitHub OAuth App: client id i secret, callback `https://<domena>/signin-github` (plus lokalny `http://localhost:5080/signin-github` do developmentu).
- Certyfikat PFX do ochrony kluczy Data Protection (base64 w zmiennej środowiskowej Railway).
- Projekt na Railway z PostgreSQL.

## 5. Uwagi i pułapki dla Etapu 2
- **Railway connection string:** ustaw `ConnectionStrings__Default` w formacie Npgsql (`Host=…;Port=…;Database=…;Username=…;Password=…`). URI `postgresql://…` z `DATABASE_URL` nie zadziała. Brak connection stringa zatrzymuje start aplikacji (fail fast w `PersistenceModule`).
- **Start bez bazy:** aplikacja wstaje, gdy baza nie odpowiada (serwer Hangfire ponawia w tle): `/health` 200, `/health/ready` 503. W testach `UnreachableDatabaseApiFactory` usuwa serwer Hangfire, bo jego zamknięcie czekałoby minutami.
- **`/health/ready` zaraz po starcie:** serwer Hangfire potrzebuje chwili na pierwszy heartbeat – testy odpytują endpoint do 30 s.
- **`/hangfire` nie jest jeszcze zmapowany** (CLAUDE.md wspomina go przy `dotnet run`). Panel z polityką `Admin` to E6; lokalnie można go dodać wcześniej, jeśli będzie potrzebny.
- **Polityki rate limitingu na nowych kontrolerach:** `[EnableRateLimiting(RateLimitPolicies.X)]` na akcji działa; bez atrybutu akcja dostaje `default`. Nie wracać do `MapControllers().RequireRateLimiting(...)` – nadpisuje atrybuty (test `Should_KeepActionPolicyAndDefaultOthers_When_EndpointsBuilt`).
- **Błędy biznesowe:** serwisy rzucają wyjątki z `Application/Errors`, a `DevPulseExceptionHandler` zamienia je na ProblemDetails. `Message` trafia do `detail` – bez danych wrażliwych.

## 6. Sugestie z review (niepilne, do zrobienia przy okazji)
1. `HangfireHealthCheck`: `GetMonitoringApi().Servers()` jest synchroniczne (Hangfire nie ma wersji async) – dopisać komentarz, że to świadomy wyjątek od reguły async.
2. `FluentValidation.DependencyInjectionExtensions` przenieść z Application do Infrastructure (używa go tylko `ValidationModule`).
3. `ValidationFilter`: camelCase zmienia tylko pierwszy segment klucza (`Items[0].Name` → `items[0].Name`) – istotne dopiero przy zagnieżdżonych requestach.
4. `User.RecordLogin`: `TokenScopes` jest po cichu obcinane do 256 znaków – obcięcie może dać fałszywe `missingScopes`; lepiej rzucić wyjątek albo podnieść limit.
5. `DigestDetailResponse.Error` trafia do użytkownika – w E5A `SendDigestJob` powinien zapisywać stały kod błędu, nie surowy `exception.Message`.
6. Komentarz w `DevPulse.Infrastructure.csproj` przy przypiętym `Newtonsoft.Json`.

## 7. Otwarte decyzje (czekają na Twoje „tak”)
- `.gitattributes` z `* text=auto eol=lf` – przy `core.autocrlf=true` lokalny `prettier --check` zgłasza pliki frontendu z CRLF (repo i CI mają LF, więc CI tego nie widzi).
- `libgssapi-krb5-2` w obrazie runtime – usuwa nieszkodliwy komunikat `libgssapi_krb5.so.2: cannot open shared object file` z logów kontenera (Npgsql sprawdza dostępność Kerberos).
- Skill `devpulse-review` w pluginie devpulse-kit opiera się na `git diff` i nie widzi nieśledzonych plików – przed review trzeba robić `git add -A` (zmiana w pluginie, poza tym repo).

## 8. Komendy kontrolne
```
dotnet tool restore
dotnet build ./src/DevPulse.sln
dotnet test ./tests/DevPulse.Tests                      # Docker musi działać (Testcontainers)
cd frontend && npm run lint && npm run type-check && npm test && npm run build
docker compose up -d && docker build -t devpulse:local .
```
