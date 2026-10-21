# DevPulse – plan implementacji (etapy)

> Sesja 04 · 2026-10-21 · zaakceptowany plan implementacji (bazuje na `01-architecture-plan.md`, `02-database-schema.md`, `03-api-contracts.md`).

## 0. Zasady podziału
| Zasada | Konsekwencja |
|---|---|
| Każdy etap kończy się działającym i przetestowanym kawałkiem | Definition of Done (sekcja 4) obowiązuje dla każdego etapu i każdej ścieżki równoległej. |
| Wspólne „punkty styku” powstają **przed** zrównolegleniem | Etap 1 tworzy cały schemat (jedna migracja `InitialCreate`), wszystkie interfejsy z `Application/Interfaces`, DTO z planu 03 i szkielet DI/routera. Dzięki temu ścieżki w etapie 3 nie dotykają tych samych plików. |
| Ścieżka krytyczna = dashboard | Pierwsza wartość dla użytkownika (karty repo) wychodzi na produkcję możliwie wcześnie, digest dochodzi później. |
| Deploy na Railway od etapu 2 | Problemy z OAuth callback, Data Protection i certyfikatem wychodzą na początku, nie na końcu (plan 01, sekcja 5). |

Szacunki to **godziny skupionej pracy jednej osoby z Claude Code** (łącznie z testami i review), nie dni kalendarzowe.

---

## 1. Graf zależności
```
E1 Fundament + schemat + kontrakty
 └─ E2 Auth + pierwszy deploy
     ├─ E3A GitHub + repozytoria + synchronizacja (M2, M3, M4) ──┐   ← ścieżka krytyczna
     ├─ E3B Klocki digestu: AI, email, composer (M7, M8)         │   ┐
     └─ E3C Ustawienia (M6)                                      │   ├ równolegle z E3A
                                                                 │   ┘
     E4 Dashboard + wybór repo w UI (M5, UI M3) ←────────────────┘   → MVP 1 na produkcji
     E5A Pipeline digestu (M9)  ← E3A + E3B + E3C                 ┐
     E5B Historia digestów (M10) ← tylko E1/E2 (dane z seedów)    ┘ równolegle
 E6 Operacje i hardening (M11) ← wszystko                          → MVP 2 (pełny zakres CLAUDE.md)
```

---

## 2. Etapy

### Etap 1 – Fundament, schemat i kontrakty (M0) · sekwencyjnie · 8–10 h
**Co powstaje**
- `src/DevPulse.sln` z 4 projektami (Domain, Application, Infrastructure, Api) i `tests/DevPulse.Tests` – dokładnie te ścieżki, których oczekują hooki devpulse-kit.
- Domain: wszystkie encje, enumy i value objecty z planu 01/02 (`User`, `UserSettings`, `TrackedRepository`, `RepositorySnapshot` + `OpenPullRequest`, `Digest`, `CiStatus`, `DigestStatus`, `SendHour`, `RepoFullName`).
- Infrastructure/Persistence: `DevPulseDbContext`, wszystkie `*Configuration.cs` (snake_case, CHECK-i, indeksy, jsonb przez `ToJson()`), `data_protection_keys` i **jedna migracja `InitialCreate`** (`/devpulse-kit:new-migration InitialCreate`) obejmująca cały schemat z planu 02.
- Application: wszystkie interfejsy (`IGitHubClient`, `IRepositoryService`, `IDashboardService`, `ISettingsService`, `IDigestService`, `IDigestComposer`, `IAiPrioritizer`, `IEmailSender`, `ITokenProtector`, `ICurrentUser`, `IDevPulseDbContext`) i wszystkie DTO z planu 03 (bez implementacji).
- Szkielet DI pod zrównoleglenie: `Infrastructure/DependencyInjection/` z osobnym, pustym plikiem na moduł (`GitHubModule.cs`, `DigestModule.cs`, `SettingsModule.cs`, …), wołanym z `AddInfrastructure()`. `Program.cs`: ProblemDetails, Serilog (bez tokenów), Hangfire na PostgreSQL (schemat `hangfire`), `TimeProvider`, OpenAPI (Development), `/health` + `/health/ready`, rejestracja rate limitera (polityki z planu 03, sekcja 7.1), SPA fallback.
- Testy: `Builders/`, baza testów integracyjnych (Testcontainers PostgreSQL + `WebApplicationFactory`), pierwsze testy konfiguracji EF (unikalne indeksy, CHECK-i).
- Frontend: Vite + React 19 + TS, React Router, TanStack Query, `api/httpClient` (credentials, XSRF), skrypt `openapi-typescript`, Vitest + RTL, router z pustymi stronami per feature (każdy feature eksportuje własne `routes.tsx`).
- `Dockerfile` (multi-stage), `docker-compose.yml` z lokalnym PostgreSQL, GitHub Actions: build + testy backendu i frontendu.

**Jak testować**
- `dotnet test ./tests/DevPulse.Tests` – zielone (Unit + Integration, migracja aplikuje się na czystym Postgresie w Testcontainers).
- `docker build .` przechodzi, kontener odpowiada `200` na `/health`, `/health/ready` widzi bazę i Hangfire.
- `npm run build && npm test` we `frontend/`, CI na GitHub Actions zielone.

### Etap 2 – Auth i pierwszy deploy (M1 + część M11) · sekwencyjnie · 8–10 h
**Co powstaje**
- GitHub OAuth (`AspNet.Security.OAuth.GitHub`, scopes `read:user user:email repo`), cookie HttpOnly + SameSite=Lax, `OnRedirectToLogin` → 401 dla `/api/*`, `FallbackPolicy`.
- Upsert `User` + `UserSettings` przy logowaniu (primary verified email jako `email`, `email_confirmed = true`), `ITokenProtector` (Data Protection, purpose `DevPulse.GitHubAccessToken.v1`, klucze w PostgreSQL, `ProtectKeysWithCertificate`).
- `AuthController`: `login`, `logout`, `GET/DELETE /api/auth/me`, `CurrentUser`, antiforgery double-submit.
- Frontend: `LoginPage`, `useCurrentUser`, `ProtectedRoute`, baner „Zaloguj się ponownie” (`githubTokenValid`, `missingScopes`), logout, obsługa `/login?error=access_denied`.
- Railway: serwis + PostgreSQL, zmienne środowiskowe (OAuth client, PFX w base64), EF migration bundle jako pre-deploy command, healthcheck `/health`.

**Jak testować**
- Unit: `TokenProtector` (round-trip, podmieniony ciphertext nie daje się odszyfrować), mapowanie claimów, walidacja `returnUrl`.
- Integration: niezalogowany `GET /api/auth/me` → 401 `unauthenticated` (nie 302); POST bez `X-XSRF-TOKEN` → 400; `DELETE /api/auth/me` kasuje kaskadowo.
- Ręcznie na Railway: logowanie GitHubem → `/api/auth/me` zwraca login; **restart kontenera → sesja i token nadal działają** (klucze DP w bazie); `/devpulse-kit:pre-deploy` przed deployem.

### Etap 3 – trzy ścieżki równoległe (worktrees)
Wszystkie startują z `main` po scaleniu etapu 2. Każda ścieżka edytuje tylko „swój” moduł DI, swoje foldery i swoje testy.

#### E3A – Integracja GitHub, śledzone repo, synchronizacja (M2, M3, M4) · 12–14 h · **ścieżka krytyczna**
**Co powstaje**
- `Infrastructure/GitHub`: typed HttpClient (GraphQL `nodes(ids:)` w paczkach po 20 + REST `/user/repos` z ETag), `GitHubRateLimitHandler` (budżet per `userId`, rezerwa 500), mapowanie błędów (401 → `token_invalid`, 5xx → `github-unavailable`, secondary limit → `github-rate-limited`).
- `RepositoriesController` + `RepositoryService` (`available` z `IMemoryCache` 10 min, paginacja i search w pamięci, `POST` z metadanymi z cache, limit 50, `409` na unikalnym indeksie, `DELETE`). Szkielety przez `/devpulse-kit:new-endpoint`.
- `SnapshotRefreshJob` (recurring co 15 min, aktywni użytkownicy, upsert `repository_snapshots`, przy błędzie tylko `last_attempt_at`/`last_error`) + natychmiastowy enqueue po `POST /api/repositories`.

**Jak testować**
- Unit z nagranymi odpowiedziami GitHuba (fixture JSON, `HttpMessageHandler` fake): mapowanie `statusCheckRollup` → `CiStatus`, ETag 304, odczyt nagłówków limitu, rezerwa → 503.
- Integration: `POST` dwa razy → `409`; 51. repo → `422`; repo innego użytkownika → `404`; job z fake klientem zapisuje snapshot, a przy błędzie zachowuje stare dane.
- Ręcznie: `GET /api/repositories/available` na prawdziwym koncie; w `/hangfire` (lokalnie) widać job co 15 min.

#### E3B – Klocki digestu: composer, AI, email (M7, M8) · 8–10 h
**Co powstaje**
- `Application/Digest`: budowa modelu digestu ze snapshotów, **fallback regułowy** (failing CI > stare PR-y > brak aktywności), maks. 5 priorytetów.
- `Infrastructure/Ai/AnthropicPrioritizer`: `claude-sonnet-5-5`, kompaktowy JSON na wejściu, wyjście strukturalne, `max_tokens ≈ 400`, timeout, zwraca liczbę tokenów; przy błędzie → fallback.
- `Infrastructure/Email`: `SendGridEmailSender` (zwraca `X-Message-Id`), szablon HTML digestu renderowany po stronie serwera, szablon maila potwierdzającego adres.

**Jak testować**
- Unit (czysta logika, bez I/O): reguły fallbacku, obcięcie do 5 pozycji, parsowanie odpowiedzi AI (poprawny JSON, śmieci, timeout → fallback), render HTML (snapshot test).
- Test kontraktowy opcjonalny, za flagą `Category=External`: jedno prawdziwe wywołanie Anthropic i SendGrid (sandbox mode) z kluczami z user-secrets – nie w CI.
- Nie wymaga GitHuba ani UI, więc nie blokuje się na E3A.

#### E3C – Ustawienia (M6, bez wyboru repo) · 6–8 h
**Co powstaje**
- `SettingsController` + `SettingsService`: `GET/PUT /api/settings` (walidacja strefy IANA, `nextDigestAt` przez `TimeZoneInfo` + `TimeProvider`), zmiana emaila (token 32 B, SHA-256 w bazie, 24 h, enqueue maila), anulowanie, `confirm` (🔓, polityka `anonymous`).
- Frontend: `SettingsPage` (godzina, strefa domyślnie z przeglądarki, włącz/wyłącz, zmiana emaila), strona `/confirm-email?token=…` wysyłająca POST.
- Wysyłka maila przez `IEmailSender` – w tej ścieżce fake/log; prawdziwy sender z E3B podpina się po scaleniu.

**Jak testować**
- Unit: walidatory, `nextDigestAt` przy zmianie czasu (FakeTimeProvider, `Europe/Warsaw` w ostatnią niedzielę października), hash tokenu.
- Integration: wygasły/użyty/zły token → jeden `400 invalid-token`; ponowna zmiana unieważnia stary token; rate limit `email` → 429.
- Vitest: formularz ustawień, strona potwierdzenia (sukces/błąd).

### Etap 4 – Dashboard i wybór repozytoriów w UI (M5 + UI M3) · 8–10 h · po scaleniu E3A
**Co powstaje**
- `DashboardController` + `DashboardService`: jedno zapytanie `tracked_repositories LEFT JOIN repository_snapshots`, `summary`, stany `Loading/Fresh/Stale/Error`, `checksUrl`, ETag + `Cache-Control: private, no-cache`.
- Frontend: `DashboardPage`, `RepoCard`, `CiBadge`, `PullRequestList` (linki do PR), „odświeżono X min temu”, `refetchInterval` 15 min, `EmptyState` z CTA do wyboru repo; `RepoPicker` (search, paginacja, dodaj/usuń, obsługa 409/422/503) – osadzony w ustawieniach i w pustym dashboardzie.

**Jak testować**
- Integration: dashboard nie woła GitHuba (fake `IGitHubClient` rzuca przy każdym wywołaniu); drugi request z `If-None-Match` → 304; izolacja – użytkownik B nie widzi kart A; 50 repo × 10 PR odpowiada w < 100 ms na Testcontainers.
- Vitest: `RepoCard` w każdym stanie, `CiBadge`, `RepoPicker`.
- Ręcznie na Railway: zaloguj → dodaj 3 repo → karty `Loading` → po chwili `Fresh` z prawdziwymi liczbami. **= MVP 1 na produkcji.**

### Etap 5 – dwie ścieżki równoległe
#### E5A – Pipeline digestu (M9) · 8–10 h · po scaleniu E3A, E3B, E3C
**Co powstaje**
- `DigestSchedulerJob` (co godzinę: `digest_enabled`, potwierdzony email, lokalna godzina == `send_hour`, brak digestu na lokalną datę) → enqueue `SendDigestJob(userId)`.
- `SendDigestJob`: insert `Pending` (unique violation → koniec bez wysyłki), odświeżenie snapshotu jeśli > 15 min, compose → AI/fallback → render → SendGrid → `Sent`/`Failed`, `attempt_count`, `error`, metadane AI.

**Jak testować**
- Unit/Integration z `FakeTimeProvider`: użytkownicy w 3 strefach, godzina zmiany czasu, wyłączony digest, brak emaila; podwójne uruchomienie joba → jeden mail; awaria AI → digest z `ai_used = false`; awaria SendGrid → `Failed` + retry Hangfire.
- Ręcznie na Railway: admin uruchamia `SendDigestJob` z `/hangfire` dla własnego konta → mail w skrzynce, wiersz w `digests`.

#### E5B – Historia digestów (M10) · 5–6 h · może ruszyć zaraz po E2
**Co powstaje**
- `DigestsController`: lista 30 dni (projekcja bez `html_body`), szczegóły, `/html` z CSP sandbox i `immutable`; `RetentionJob` (codziennie, `created_at < now() - 30 d`).
- Frontend: `DigestHistoryPage`, `DigestDetail` z `<iframe sandbox>`.

**Jak testować**
- Integration na seedowanych digestach (builder `DigestBuilder`): 31+ dni → lista bez najstarszych; cudzy digest → 404; `/html` ma nagłówki CSP; `RetentionJob` usuwa tylko stare.
- Vitest: lista, pusty stan, podgląd. Nie zależy od E5A, bo tabela `digests` istnieje od etapu 1.

### Etap 6 – Operacje i hardening (M11) · sekwencyjnie · 5–6 h
**Co powstaje**
- `/hangfire` z polityką `Admin` (`Admin:Logins`), link w UI dla `isAdmin`; przegląd polityk rate limitingu na wszystkich endpointach; `ForwardedHeaders` dla Railway.
- Przegląd bezpieczeństwa: `/security-review`, brak tokenów w logach (test Serilog destructuring), CSP dla SPA.
- Dokumentacja uruchomienia (README), runbook deployu.

**Jak testować**
- Integration: zwykły użytkownik na `/hangfire` → 403, niezalogowany → 401; test „smoke” przechodzący cały przepływ na fake'ach (logowanie → repo → snapshot → digest → historia).
- Ręcznie: `/devpulse-kit:pre-deploy`, deploy, pełny scenariusz na produkcji, następnego dnia digest przychodzi o ustawionej godzinie. **= MVP 2.**

---

## 3. Zrównoleglenie: worktrees i subagenci

### 3.1 Gdzie i jak
| Okno | Ścieżki | Mechanizm |
|---|---|---|
| Etap 3 | E3A, E3B, E3C | 3 osobne sesje Claude Code, każda w swoim **git worktree** (`claude --worktree` lub `EnterWorktree`), branche `feat/e3a-github-sync`, `feat/e3b-digest-blocks`, `feat/e3c-settings`. Ja (sesja główna) koordynuję i scalam. |
| Etap 4 ∥ E3B/E3C | E4 startuje po scaleniu E3A, nawet jeśli E3B/E3C jeszcze trwają | nowy worktree z `main` |
| Etap 5 | E5A, E5B | 2 worktrees; E5B może ruszyć wcześniej (już równolegle z etapem 3 jako czwarta ścieżka, jeśli jest przepustowość) |

**Subagenci w każdej ścieżce**
- `devpulse-kit:test-writer` (Sonnet, własny worktree) – po dodaniu każdej publicznej klasy w Application/Infrastructure dopisuje testy xUnit. Główny agent ścieżki pisze kod produkcyjny, test-writer testy – równolegle w obrębie ścieżki.
- `Explore` – tylko gdy trzeba przeszukać większy kawałek kodu z innej ścieżki po scaleniu.
- `devpulse-kit:devpulse-review` (forkowany kontekst) – przed każdym merge.
- Nie odpalamy subagentów do etapów 1, 2 i 6: są sekwencyjne, a wspólne pliki (Program.cs, migracja, router) są tam edytowane intensywnie – równoległość dałaby tylko konflikty.

### 3.2 Reguły zapobiegające konfliktom
| Hotspot | Reguła |
|---|---|
| Migracje EF (`DevPulseDbContextModelSnapshot.cs`) | Cały schemat w `InitialCreate` (etap 1). Jeśli ścieżka mimo to potrzebuje zmiany schematu: tylko jedna ścieżka naraz, nowa migracja tuż przed merge po rebase na `main` (hook `protect-files.sh` i tak blokuje edycję istniejących). |
| `Program.cs` / DI | Ścieżki edytują tylko swój plik w `Infrastructure/DependencyInjection/` i swoją rejestrację recurring jobów. `Program.cs` zmieniamy tylko w etapach sekwencyjnych. |
| Router frontendu | Każdy feature ma własne `routes.tsx`; `App.tsx` składa je i jest gotowy od etapu 1. |
| Typy OpenAPI (`frontend/src/api/schema.d.ts`) | Plik generowany – przy konflikcie nie scalamy ręcznie, tylko regenerujemy po merge. |
| Baza lokalna | Testy integracyjne mają własne kontenery (Testcontainers). Do ręcznego uruchamiania każdy worktree ma osobną bazę (`devpulse_e3a`, …) w tym samym kontenerze z docker-compose, ustawianą w user-secrets/`ConnectionStrings__Default`. |
| Kolejność merge | E3A → E4 → E3B → E3C → E5B → E5A. Przed merge: rebase na `main`, zielone testy, review. |

---

## 4. Definition of Done (każdy etap i ścieżka)
1. `dotnet test ./tests/DevPulse.Tests` zielone (hook `require-green-tests.sh` pilnuje testów Unit przy każdym zakończeniu pracy), `npm test` zielone, CI zielone.
2. Nowe publiczne klasy mają testy (test-writer), endpointy mają testy integracyjne z przypadkami błędów z planu 03.
3. `/devpulse-kit:devpulse-review` bez otwartych problemów.
4. Od etapu 2: deploy na Railway po `/devpulse-kit:pre-deploy` i ręczny smoke test opisany w etapie.
5. Commit/PR na branch etapu, merge do `main` (push do `main` i `dotnet ef database update` wymagają potwierdzenia – hook `guard-commands.sh`).

## 5. Podsumowanie czasu
| Etap | Szacunek | Tryb |
|---|---|---|
| E1 Fundament | 8–10 h | sekwencyjnie |
| E2 Auth + deploy | 8–10 h | sekwencyjnie |
| E3A GitHub + sync | 12–14 h | równolegle |
| E3B Klocki digestu | 8–10 h | równolegle |
| E3C Ustawienia | 6–8 h | równolegle |
| E4 Dashboard | 8–10 h | po E3A |
| E5A Pipeline digestu | 8–10 h | równolegle |
| E5B Historia | 5–6 h | równolegle |
| E6 Operacje | 5–6 h | sekwencyjnie |
| **Suma pracy** | **≈ 68–84 h** | |
| **Ścieżka krytyczna** (E1 → E2 → E3A → E4 → E5A → E6) | **≈ 49–60 h** | przy pełnym zrównolegleniu |

Zysk z równoległości to ok. 20 h, ale realnie mniejszy: scalanie i review trzech ścieżek kosztuje ok. 1–2 h na każdy merge, a koordynacja kilku sesji też wymaga uwagi. Ryzyka, które mogą wydłużyć szacunki: konfiguracja certyfikatu Data Protection na Railway (E2), kształt zapytania GraphQL dla `statusCheckRollup` (E3A), deliverability SendGrid – weryfikacja domeny nadawcy (E3B, warto zlecić ją od razu, bo trwa dniami).

