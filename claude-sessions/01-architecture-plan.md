# DevPulse – plan architektury

> Sesja 01 · 2026-10-21 · zaakceptowany plan architektury (wymagania: `CLAUDE.md`).

## 0. Ustalenia doprecyzowujące wymagania
| Temat | Decyzja |
|---|---|
| Dostęp do GitHuba | OAuth App, scopes: `read:user user:email repo`. Repozytoria publiczne i prywatne. |
| Status CI | `statusCheckRollup` ostatniego commita na default branch: GitHub Actions i zewnętrzne CI. Stany: Success / Failure / Pending / None. |
| Godzina digestu | Pełna godzina 0–23 w strefie IANA użytkownika. Domyślna strefa pochodzi z przeglądarki. |
| Deployment | Jeden serwis na Railway (API + SPA w `wwwroot` + Hangfire server) i Railway PostgreSQL. |
| Email | Domyślnie primary verified email z GitHuba. Zmiana adresu wymaga kliknięcia w link potwierdzający, żeby nie wysyłać spamu na cudze adresy. |

## 1. Struktura rozwiązania .NET
```
src/
  DevPulse.sln
  DevPulse.Domain/            # zero zależności
    Entities/                 # User, TrackedRepository, RepositorySnapshot, OpenPullRequest, Digest, UserSettings
    Enums/                    # CiStatus, DigestStatus
    ValueObjects/             # SendHour, RepoFullName
  DevPulse.Application/       # → Domain
    Interfaces/               # IGitHubClient, IRepositoryService, IDashboardService, ISettingsService,
                              # IDigestService, IDigestComposer, IAiPrioritizer, IEmailSender,
                              # ITokenProtector, ICurrentUser, IDevPulseDbContext
    DTOs/                     # [Zasób][Akcja]Request/Response
    Validators/               # FluentValidation
    Digest/                   # czysta logika: budowa modelu digestu, reguły fallback bez AI
  DevPulse.Infrastructure/    # → Application, Domain
    Persistence/              # DevPulseDbContext, Configurations/, Migrations/
    GitHub/                   # typed HttpClient (GraphQL + REST), zapytania, rate-limit handler
    Ai/                       # AnthropicPrioritizer (claude-sonnet-5-5)
    Email/                    # SendGridEmailSender, szablon HTML digestu
    Jobs/                     # SnapshotRefreshJob, DigestSchedulerJob, SendDigestJob, RetentionJob
    Security/                 # szyfrowanie tokenów (ASP.NET Data Protection, klucze w Postgres)
    Services/                 # implementacje serwisów z Application/Interfaces
    DependencyInjection.cs
  DevPulse.Api/               # → Application, Infrastructure (tylko composition root)
    Controllers/              # Auth, Repositories, Dashboard, Settings, Digests
    Auth/                     # konfiguracja cookie + GitHub OAuth, CurrentUser
    Program.cs                # DI, Hangfire, OpenAPI, health checks, SPA fallback
    wwwroot/                  # build frontendu (artefakt, nie w repo)
tests/
  DevPulse.Tests/
    Builders/                 # buildery encji testowych
    Unit/                     # Application + Infrastructure (Moq), Category=Unit
    Integration/              # Testcontainers PostgreSQL, WebApplicationFactory
Dockerfile                    # multi-stage: node build → dotnet publish → runtime
```
Kierunek zależności: Api → Infrastructure → Application → Domain. Kontrolery nie zawierają logiki, tylko wołają interfejsy z Application.

## 2. Struktura frontendu
```
frontend/
  src/
    main.tsx, App.tsx
    app/            # router (React Router), QueryClientProvider, layout, ProtectedRoute
    api/            # httpClient (fetch + credentials), typy wygenerowane z OpenAPI (openapi-typescript)
    features/
      auth/         # useCurrentUser, LoginPage, logout
      dashboard/    # DashboardPage, RepoCard, CiBadge, PullRequestList, useDashboard
      repositories/ # RepoPicker (lista dostępnych repo + zaznaczanie), useRepositories
      settings/     # SettingsPage, formularz email/godzina/strefa, potwierdzenie emaila
      digests/      # DigestHistoryPage, DigestDetail (podgląd HTML w sandbox iframe)
    components/     # współdzielone UI: Button, Card, Spinner, EmptyState, ErrorBoundary
    hooks/, lib/    # formatowanie dat, strefy czasowe
    styles/
  tests/ lub *.test.tsx obok komponentów (Vitest + React Testing Library)
  vite.config.ts    # proxy /api → localhost w dev
```
Stan serwera trzyma TanStack Query (`staleTime`/`refetchInterval` 15 min). Globalny store nie jest potrzebny.

## 3. Moduły funkcjonalne
| # | Moduł | Opis |
|---|---|---|
| M0 | Fundament | Solution, projekty, DbContext, Docker, CI (GitHub Actions: build + testy), konfiguracja i sekrety, health checks, logowanie (Serilog bez tokenów). |
| M1 | Auth i użytkownicy | Logowanie GitHub OAuth, cookie sesyjne, encja `User` (GitHub id, login, avatar), zaszyfrowany access token, `/api/auth/me`, logout, obsługa odwołanego tokenu (wymuszenie ponownego logowania). |
| M2 | Integracja GitHub | Typed HttpClient: GraphQL do snapshotów (wiele repo w jednym zapytaniu przez aliasy), REST z ETag do listy repo użytkownika, odczyt nagłówków rate-limit, backoff, mapowanie błędów (401 → token nieważny). |
| M3 | Śledzone repozytoria | Lista repo dostępnych dla użytkownika (cache ETag), dodawanie i usuwanie `TrackedRepository`. |
| M4 | Synchronizacja (cache) | Hangfire recurring co 15 min. Dla każdego aktywnego użytkownika jedno zapytanie GraphQL (paczki po ok. 20 repo) zapisuje `RepositorySnapshot`: liczba otwartych PR (+ top N z linkami), liczba otwartych issue, CI rollup, `pushedAt`/ostatnia aktywność, `RefreshedAt`. Pierwszy snapshot po dodaniu repo jest enqueue'owany od razu. |
| M5 | Dashboard | `GET /api/dashboard` czyta wyłącznie snapshoty z DB (cel < 2 s, w praktyce < 100 ms). UI z kartami repo i znacznikiem „odświeżono X min temu”. |
| M6 | Ustawienia | Email (z weryfikacją linkiem), godzina wysyłki, strefa IANA, digest włączony/wyłączony, wybór repo (UI korzysta z M3). |
| M7 | Priorytety AI | `IAiPrioritizer`: kompaktowy JSON stanu repo jako wejście, wyjście strukturalne (maks. 5 priorytetów po 1 zdanie, z odwołaniem do repo), `max_tokens` ok. 400, timeout, fallback regułowy (failing CI > stare PR-y > brak aktywności). |
| M8 | Wysyłka email | `IEmailSender` na SendGrid, szablon HTML generowany po stronie serwera (ten sam HTML trafia do historii), obsługa błędów i retry przez Hangfire. |
| M9 | Pipeline digestu | `DigestSchedulerJob` co godzinę wybiera użytkowników, dla których lokalna godzina = `SendHour` i nie ma digestu na lokalną datę, i enqueue'uje `SendDigestJob(userId)`. Job: świeży snapshot (jeśli starszy niż 15 min), compose, AI, render, wysyłka, zapis `Digest`. Idempotencję zapewnia unikalny indeks (UserId, DigestDate). |
| M10 | Historia digestów | `GET /api/digests` (30 dni), `GET /api/digests/{id}`, UI listy i podglądu. `RetentionJob` raz dziennie usuwa digesty starsze niż 30 dni. |
| M11 | Operacje | Dashboard Hangfire tylko dla admina (lista GitHub loginów w konfiguracji), `/health`, EF migration bundle uruchamiany jako pre-deploy command na Railway. |

## 4. Kluczowe decyzje architektoniczne
1. **Clean Architecture w 4 projektach.** Wymagają tego konwencje `devpulse-kit` (ścieżki w skillach i hookach). Rozdziela też logikę digestu (testowalną bez I/O) od GitHuba, SendGrid i Anthropic.
2. **Cookie auth zamiast JWT.** SPA i API mają tę samą domenę, bo działają jako jeden serwis. Cookie HttpOnly + SameSite=Lax jest prostsze i bezpieczniejsze niż przechowywanie JWT w przeglądarce. Handler: `AspNet.Security.OAuth.GitHub`.
3. **Token GitHub szyfrowany przez Data Protection, klucze w PostgreSQL.** Kontenery Railway są efemeryczne. Klucze trzymane na dysku ginęłyby przy restarcie i unieważniały wszystkie sesje i tokeny.
4. **Cache = tabela snapshotów w PostgreSQL, bez Redisa.** Dashboard nigdy nie woła GitHuba. Jedna baza to mniej infrastruktury i kosztów. Przy tej skali Postgres w zupełności wystarczy.
5. **GraphQL do snapshotów, REST z ETag do list.** Jedno zapytanie GraphQL zwraca liczniki PR i issue (issues bez PR-ów), `statusCheckRollup` i `pushedAt` dla wielu repo naraz. Daje to kilka punktów rate limitu na użytkownika co 15 min, daleko od 5000/h. Odpowiedzi 304 przy ETag nie zużywają limitu REST.
6. **Odświeżanie w tle co 15 min tylko dla aktywnych użytkowników.** Aktywny = logował się w ostatnich 14 dniach albo ma włączony digest. Nie marnujemy limitu na porzucone konta, a przed digestem snapshot i tak jest odświeżany.
7. **Hangfire na PostgreSQL (`Hangfire.PostgreSql`) w procesie API.** Zgodne z ograniczeniami technologicznymi. Retry jest za darmo. Osobny worker byłby przesadą przy tej skali.
8. **Harmonogram digestów: jeden job godzinowy zamiast jednego recurring joba na użytkownika.** Zmiana godziny w ustawieniach nie wymaga przepinania jobów. `TimeZoneInfo` z IANA poprawnie obsługuje zmianę czasu. Unikalny indeks chroni przed podwójną wysyłką.
9. **AI w trybie strukturalnym, krótko, z fallbackiem.** Krótkie wejście (tylko liczby i tytuły, bez diffów), JSON na wyjściu, limit tokenów. Koszt digestu to ułamki centa. Awaria Anthropic nie blokuje wysyłki, bo digest wychodzi z priorytetami regułowymi.
10. **Historia przechowuje wyrenderowany HTML i JSON priorytetów.** Podgląd w UI pokazuje dokładnie to, co poszło mailem, bez ponownego renderowania z danych, które mogły się zmienić.
11. **Izolacja użytkowników przez jawny `userId` w serwisach Application.** Joby działają bez HttpContext, więc global query filters oparte na `ICurrentUser` byłyby zawodne. Każde zapytanie filtruje po właścicielu, a testy integracyjne sprawdzają brak wycieku między kontami.
12. **Kontrakt API przez OpenAPI.** Wbudowany `Microsoft.AspNetCore.OpenApi` w .NET 10, typy TS generowane przez `openapi-typescript`. Frontend nie rozjeżdża się z DTO.
13. **Czas przez `TimeProvider`.** Testowalne harmonogramy i retencja.

## 5. Zależności i kolejność realizacji
```
M0 Fundament
 └─ M1 Auth i użytkownicy
     ├─ M2 Integracja GitHub
     │   └─ M3 Śledzone repozytoria
     │       └─ M4 Synchronizacja (cache)
     │           └─ M5 Dashboard
     └─ M6 Ustawienia (wybór repo wymaga M3)
M7 Priorytety AI   ─┐ (niezależne, potrzebują tylko M0 i interfejsów)
M8 Wysyłka email   ─┤
M4 + M6 + M7 + M8 ──┴─ M9 Pipeline digestu
                        └─ M10 Historia digestów
M11 Operacje: częściowo w M0 (health, logi), reszta przed pierwszym deployem produkcyjnym
```
Proponowane etapy:
1. **Etap 1 (szkielet):** M0, M1, deploy „hello world” na Railway od razu, żeby wcześnie wyłapać problemy z OAuth callback i Data Protection.
2. **Etap 2 (dashboard, pierwsza wartość dla użytkownika):** M2, M3, M4, M5.
3. **Etap 3 (konfiguracja):** M6.
4. **Etap 4 (digest):** M7 i M8 równolegle, potem M9.
5. **Etap 5 (domknięcie):** M10, M11.

## 6. Szkic modelu danych
- `User`: Id, GitHubId (unikalne), Login, AvatarUrl, EncryptedAccessToken, TokenInvalid, LastLoginAt
- `UserSettings` (1:1): Email, EmailConfirmed, PendingEmail + token, SendHour, TimeZoneId, DigestEnabled
- `TrackedRepository`: Id, UserId, GitHubRepoId, FullName, HtmlUrl, DefaultBranch. Unikalne (UserId, GitHubRepoId).
- `RepositorySnapshot` (1:1 z TrackedRepository, nadpisywany): OpenPrCount, OpenIssueCount, CiStatus, LastActivityAt, RefreshedAt, `OpenPullRequests` (jsonb, top 10: tytuł, url, autor, data)
- `Digest`: Id, UserId, DigestDate (lokalna data), SentAt, Status, PrioritiesJson (jsonb), HtmlBody, AiUsed. Unikalne (UserId, DigestDate).
- Tabele Hangfire i DataProtectionKeys.

## 7. Poza zakresem (zgodnie z CLAUDE.md)
GitLab/Bitbucket, aplikacja mobilna, WebSocket/real-time, współdzielenie i zespoły, webhooki GitHub. Polling co 15 min wystarczy.
