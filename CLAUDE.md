# DevPulse

SaaS dashboard do monitorowania kondycji repozytoriów GitHub.
Agreguje PR-y, issue, CI status i wysyła codzienny digest z priorytetami AI.

Stack: .NET 10 Web API + React 19 + TypeScript + Vite + PostgreSQL + EF Core + Hangfire (PostgreSQL)
AI: Anthropic API (claude-sonnet-5-5) · Email: SendGrid · Auth: GitHub OAuth · Deploy: Railway

Wymagania biznesowe i zakres: @docs/requirements.md

---

## Szczegółowe instrukcje (ładują się automatycznie przy pracy w danym obszarze)
- Konwencje testowe: tests/CLAUDE.md
- Konwencje React/TypeScript: .claude/rules/frontend.md

Zaakceptowane plany (czytaj tylko plik dotyczący bieżącego obszaru):
- claude-sessions/01-architecture-plan.md – moduły M0–M11, decyzje architektoniczne
- claude-sessions/02-database-schema.md – tabele, indeksy, szyfrowanie tokenów, cache
- claude-sessions/03-api-contracts.md – endpointy, DTO, kody błędów, rate limiting
- claude-sessions/04-implementation-plan.md – etapy E1–E6, worktrees, Definition of Done

---

## Komendy projektu

### Backend
- Build: `dotnet build ./src/DevPulse.sln`
- Unit testy: `dotnet test ./tests/DevPulse.Tests --filter Category=Unit`
- Integracyjne: `dotnet test ./tests/DevPulse.Tests --filter Category=Integration`
  (wymagają Dockera – Testcontainers PostgreSQL)
- Wszystkie: `dotnet test ./tests/DevPulse.Tests`
- Run: `dotnet run --project ./src/DevPulse.Api`   (lokalnie także /openapi/v1.json; /hangfire od etapu 6)
  (wymaga user-secrets `Auth:GitHub:ClientId` i `Auth:GitHub:ClientSecret` – bez nich start kończy się błędem; docs/deploy/railway.md)
- Nowa migracja: `dotnet ef migrations add [Nazwa] --project ./src/DevPulse.Infrastructure --startup-project ./src/DevPulse.Api`
  (preferuj `/devpulse-kit:new-migration [Nazwa]`)
- Aplikacja migracji: `dotnet ef database update --project ./src/DevPulse.Infrastructure --startup-project ./src/DevPulse.Api`
- Migration bundle (pre-deploy na Railway): `dotnet ef migrations bundle --project ./src/DevPulse.Infrastructure --startup-project ./src/DevPulse.Api`
  (budowany w Dockerfile jako /app/efbundle; narzędzia EF używają DevPulseDbContextDesignTimeFactory, nie hosta API)

### Frontend
- Dev: `cd frontend && npm run dev`          (port 5173, proxy /api → backend)
- Build: `cd frontend && npm run build`
- Testy: `cd frontend && npm test`           (Vitest + React Testing Library)
- Lint + type check: `cd frontend && npm run lint && npm run type-check`
- Typy API: regeneruj `frontend/src/api/schema.d.ts` z /openapi/v1.json (openapi-typescript) po każdej zmianie DTO

### Środowisko
- Start: `docker compose up -d`   (lokalny PostgreSQL:5432)
- Stop: `docker compose down`
- Obraz produkcyjny: `docker build .`   (multi-stage: node build → dotnet publish + efbundle → runtime; deploy: railway.json)

### Skille devpulse-kit
- `/devpulse-kit:new-endpoint [METHOD] [PATH]` – DTO, serwis, kontroler, testy
- `/devpulse-kit:new-migration [Nazwa]` – migracja po sprawdzeniu encji i DbSet
- `/devpulse-kit:devpulse-review` – przed każdym commitem/PR
- `/devpulse-kit:pre-deploy` – przed deployem na Railway

---

## Architektura – Clean Architecture

```
src/
  DevPulse.Api/            ← Controllers, Auth (cookie + GitHub OAuth), Program.cs (DI, Hangfire,
                             OpenAPI, health checks, rate limiter, SPA fallback), wwwroot (build SPA)
  DevPulse.Application/    ← Interfaces, DTOs, Validators (FluentValidation),
                             Digests/ (czysta logika digestu + fallback regułowy)
  DevPulse.Domain/         ← Encje, value objects, enums – zero zależności
  DevPulse.Infrastructure/ ← Persistence (EF Core), GitHub (GraphQL + REST), Ai (Anthropic),
                             Email (SendGrid), Jobs (Hangfire), Security (Data Protection),
                             Services, DependencyInjection/ (plik per moduł)
tests/
  DevPulse.Tests/          ← xUnit: Builders/, Unit/, Integration/
frontend/src/              ← React 19 + TypeScript + Vite: app/, api/, features/, components/
docs/                      ← requirements.md
claude-sessions/           ← zaakceptowane plany (dokumentacja, nie kod)
```

```
 Railway: jeden serwis (API + SPA + Hangfire server)          Railway PostgreSQL
 ┌───────────────────────────────────────────────────┐       ┌──────────────────────┐
 │ SPA ──cookie + X-XSRF-TOKEN──▶ Controllers        │       │ dane aplikacji       │
 │                                  │                │◀─────▶│ repository_snapshots │
 │                     interfejsy Application        │       │ hangfire.*           │
 │                                  │                │       │ data_protection_keys │
 │  Jobs: SnapshotRefreshJob (15 min) ──────────────────▶ GitHub API (GraphQL/REST)
 │        DigestSchedulerJob (1 h) → SendDigestJob ─────▶ Anthropic API, SendGrid
 │        RetentionJob (1 dzień)                     │
 └───────────────────────────────────────────────────┘
 GET /api/dashboard czyta wyłącznie snapshoty z bazy – nigdy GitHuba.
```

Reguła zależności: Api → Infrastructure → Application → Domain.
Controllers: zero logiki biznesowej, tylko routing, autoryzacja i mapowanie na interfejsy z Application.

---

## Konwencje C#

### Nazewnictwo
- PascalCase: klasy, metody, właściwości, stałe publiczne
- _camelCase: pola prywatne instancji
- camelCase: parametry, zmienne lokalne
- Interfejsy: prefix I (IRepositoryService)
- DTOs: [Zasób][Akcja]Request / [Zasób][Akcja]Response w Application/DTOs (RepositoryAddRequest, SettingsResponse)

### Async / await
Wszystkie metody z I/O kończą się Async i zwracają Task lub Task<T>.
Nigdy: .Result, .Wait(), .GetAwaiter().GetResult()
Zawsze przekazuj CancellationToken przez cały stos wywołań.

### Nullable
Włączone w całym rozwiązaniu. Używaj ? nie !.
Preferuj `is null` i `is not null` nad == null.

### Walidacja i błędy
- Walidacja requestów: FluentValidation w Application/Validators – każdy Request ma validator
- Błędy HTTP: zawsze ProblemDetails ze stałym slugiem `type` (tabela 0.1 w planie 03)
- Cudzy zasób → 404, nie 403 (nie zdradzamy istnienia)
- Logowanie: ILogger<T> (Serilog) przez DI; strukturalne logi z named parameters

### Dane i czas
- Każde zapytanie o dane użytkownika filtruje jawnie po userId – bez global query filters
- Czas wyłącznie przez TimeProvider, nigdy DateTime.Now/UtcNow w logice
- Klucze główne: Guid.CreateVersion7(); id z GitHuba: long
- Konfiguracje EF w Infrastructure/Persistence/Configurations/*Configuration.cs;
  snake_case, enumy jako text + CHECK (HasConversion<string>()), listy niezmienne jako jsonb (OwnsMany(...).ToJson())
- Limity i progi (TTL snapshotu, Repositories:MaxPerUser, Admin:Logins) w konfiguracji, nie w stałych

### Testy – skrót (szczegóły w tests/CLAUDE.md)
xUnit + FluentAssertions + Moq + Testcontainers
Nazwy: Should_[Oczekiwanie]_When_[Warunek]
Struktura: // Arrange / // Act / // Assert z komentarzami

---

## Konwencje TypeScript / React (skrót – szczegóły w .claude/rules/frontend.md)

- Wyłącznie funkcyjne komponenty z jawnym interface propsów
- Dane z API: TanStack Query; klucze ZAWSZE z queryKeys.ts; typy DTO tylko z wygenerowanego schema.d.ts
- Wszystkie requesty przez src/api/httpClient (cookie + X-XSRF-TOKEN)
- Nigdy `any`; importy przez alias @/

---

## Granice autonomii

### Rób samodzielnie
- Modyfikacje w src/, tests/, frontend/src/ w zakresie bieżącego etapu z planu 04
- Uruchamianie testów, buildów, linta, type-check
- Tworzenie nowych plików w obrębie istniejącej struktury katalogów
- Uruchamianie subagenta devpulse-kit:test-writer po dodaniu klasy publicznej

### Zapytaj przed
- Nową migracją EF Core (poza zaplanowaną InitialCreate z etapu 1)
- `dotnet ef database update` i pushem do main (hook guard-commands.sh i tak zapyta)
- Instalacją nowej paczki NuGet lub npm
- Zmianą docker-compose.yml, Dockerfile lub plików konfiguracyjnych
- Dodaniem nowego projektu do rozwiązania
- Zmianą kontraktu API lub schematu bazy odbiegającą od planów 02–03
- Zmianą Program.cs podczas pracy w ścieżce równoległej (worktree)
- Czymkolwiek, co zwiększa koszt AI lub zużycie budżetu GitHub API

### Nigdy – nawet w auto mode (krytyczne punkty zablokowane też regułami deny w .claude/settings.json i hookami devpulse-kit)
- Nie commituj do repozytorium bez mojej prośby
- Nie modyfikuj appsettings.Production.json
- Nie edytuj ani nie usuwaj istniejących migracji EF Core – twórz nowe
- Nie wysyłaj requestów do zewnętrznych API (GitHub, Anthropic, SendGrid) bez jawnego polecenia
- Nie wypisuj zawartości .env, .env.local ani żadnego pliku z kluczami (w tym certyfikatu PFX)
- Nie loguj tokenów GitHub, access_token_encrypted ani kluczy API
- Nie kończ pracy z czerwonymi testami Unit (hook require-green-tests.sh)
- Nie dodawaj funkcji spoza zakresu z docs/requirements.md

---

## Instrukcje per kontekst

### Praca interaktywna (domyślna)
Pytaj, gdy masz wątpliwości.
Po implementacji uruchom testy i powiedz mi wynik.
Przed commitem uruchom /devpulse-kit:devpulse-review.

### Praca bez nadzoru (auto mode, sesje w chmurze, worktrees etapów 3 i 5)
- Przy niejednoznaczności: zapisz pytanie do docs/questions.md, kontynuuj konserwatywnie
- Po zakończeniu: dopisz listę zmian do docs/session-summary.md
- Nie instaluj nowych zależności bez sprawdzenia, że ich nie ma
- W worktree edytuj tylko swoje foldery, swój plik w Infrastructure/DependencyInjection/ i swoje testy;
  schema.d.ts przy konflikcie regeneruj, nie scalaj ręcznie

### CI/CD i zadania cykliczne (claude -p)
Zakres jest ściśle zdefiniowany w poleceniu – nie wychodź poza niego.
Zacznij odpowiedź od PASS albo FAIL, potem szczegóły; output czytelny w logach.

---

## Integracje zewnętrzne

### GitHub API (typed HttpClient: GraphQL + REST)
Serwis: Infrastructure/GitHub (IGitHubClient), GitHubRateLimitHandler (DelegatingHandler)
Scopes OAuth: read:user user:email repo
Rate limit: 5000 req/h per token. Przy remaining < 500 żądania interaktywne dostają 503 github-rate-limited –
budżet zostaje dla jobów.
Snapshoty: GraphQL nodes(ids:) po github_node_id, paczki po 20 repo. Listy repo: REST z ETag (304 nie zużywa limitu).
401 → users.token_invalid = true, joby pomijają użytkownika, UI wymusza ponowne logowanie.

### Anthropic API
Klucz: ANTHROPIC_API_KEY (env). Model: claude-sonnet-5-5
Serwis: Infrastructure/Ai/AnthropicPrioritizer (IAiPrioritizer)
Używamy do: priorytetów digestu – maks. 5 po 1 zdanie, wyjście strukturalne JSON, max_tokens ≈ 400, timeout.
Woła go wyłącznie SendDigestJob (≤ 1×/użytkownik/dobę). Żaden endpoint HTTP nie wywołuje Anthropic.
Błąd/timeout/zły JSON → fallback regułowy (failing CI > stare PR-y > brak aktywności), ai_used = false.

### Hangfire
Jobs w Infrastructure/Jobs/: SnapshotRefreshJob, DigestSchedulerJob, SendDigestJob, RetentionJob.
Storage: Hangfire.PostgreSql, schemat `hangfire` – poza migracjami EF.
WAŻNE: jobs nie mają HttpContext – nie używaj IHttpContextAccessor ani ICurrentUser w jobach, przekazuj userId jawnie.
Panel /hangfire: polityka Admin (GitHub loginy z Admin:Logins), także na produkcji.

### SendGrid
Konfiguracja w appsettings.json pod kluczem "SendGrid" (klucz API tylko w env / user-secrets).
Serwis: Infrastructure/Email/SendGridEmailSender (IEmailSender) – zwraca X-Message-Id.
Błąd → Digest.Status = Failed + retry Hangfire.

### Railway
Jeden serwis z Dockerfile + Railway PostgreSQL. Pre-deploy: EF migration bundle. Healthcheck: /health.
Klucze Data Protection w tabeli data_protection_keys, chronione certyfikatem PFX (base64 w env).

---

## Znane pułapki – przeczytaj przed pierwszą sesją

- DbContext jest Scoped → nie wstrzykuj do Singleton; użyj IDbContextFactory
- Dashboard NIGDY nie woła GitHuba – czyta tylko repository_snapshots (cel < 2 s)
- GitHub: issues.totalCount w GraphQL nie zawiera PR-ów; REST open_issues_count zawiera – nie używaj go
- GitHub: login i full_name repo mogą się zmienić – identyfikuj po id (bigint) i node_id
- GitHub: secondary rate limit (403/429 z retry-after) to nie brak uprawnień – mapuj na 503 github-rate-limited
- Npgsql: timestamptz wymaga DateTime w UTC (Kind=Local/Unspecified rzuca wyjątek)
- Strefy IANA: testuj zmianę czasu (Europe/Warsaw, ostatnia niedziela października) przez FakeTimeProvider
- Railway: kontenery są efemeryczne – bez kluczy DP w bazie restart unieważnia sesje i tokeny
- Railway: za proxy potrzebne ForwardedHeaders (IP dla rate limitera, scheme dla callbacku /signin-github)
- Rate limiter w pamięci – poprawny tylko przy jednej instancji
- Link potwierdzający email prowadzi do SPA, które robi POST – nigdy GET ze skutkiem ubocznym (skanery linków)
- Idempotencja digestu: UNIQUE (user_id, digest_date) – insert Pending, unique violation = koniec bez wysyłki
- TanStack Query: invalidateQueries wymaga DOKŁADNIE tych samych kluczy z queryKeys.ts
- EF Core: uważaj na N+1 – zawsze Include() lub projekcja; lista digestów bez html_body
- CancellationToken: przekazuj przez cały stos, nie zatrzymuj się na pierwszej metodzie
- FluentValidation: validator musi być zarejestrowany w DI – sprawdź moduł w Infrastructure/DependencyInjection/
- Hooki devpulse-kit wymagają bash + jq (Windows: winget install jqlang.jq), inaczej blokują edycje

---

## Znane decyzje architektoniczne (uzasadnienia: plan 01 §4, plan 02 §1 i §4, plan 03 §0, §7, §9)

- Clean Architecture w 4 projektach – wymagają tego ścieżki hooków/skilli devpulse-kit; logika digestu testowalna bez I/O
- Cookie auth (HttpOnly, SameSite=Lax) + antiforgery, nie JWT – SPA i API na jednej domenie
- Token GitHub szyfrowany Data Protection, nie hashowany – jobs potrzebują go w jawnej postaci
- Cache = tabela repository_snapshots w Postgres, bez Redisa – mniej infrastruktury, dashboard < 100 ms
- GraphQL do snapshotów, REST + ETag do list – kilka punktów limitu na użytkownika co 15 min
- Odświeżanie tylko aktywnych użytkowników (login < 14 dni lub digest włączony) – oszczędność limitu
- Hangfire w procesie API – retry za darmo, osobny worker to przesada przy tej skali
- Jeden godzinowy DigestSchedulerJob zamiast joba per użytkownik – zmiana godziny bez przepinania jobów
- AI krótko, strukturalnie, z fallbackiem – koszt ułamki centa, awaria Anthropic nie blokuje digestu
- Historia digestów = wyrenderowany HTML + jsonb, bez tabel podrzędnych – podgląd = dokładnie to, co poszło mailem
- Izolacja przez jawny userId – jobs nie mają HttpContext
- Kontrakt przez OpenAPI + openapi-typescript – frontend nie rozjeżdża się z DTO
- UUID v7, snake_case, enumy jako text + CHECK – brak fragmentacji B-tree, czytelny SQL, łatwe migracje
- To samo repo u dwóch użytkowników = dwa snapshoty – współdzielony cache mógłby ujawnić prywatne repo
- /api bez wersjonowania – SPA i API wdrażane razem
- Limit 50 repo/użytkownika, dashboard bez paginacji – < 2 s, przewidywalny koszt GraphQL i promptu AI
- Brak ręcznego odświeżania i testowego digestu na żądanie – koszt AI ograniczony konstrukcyjnie

---

## Katalogi do ignorowania

/bin, /obj, /node_modules, /dist, /frontend/dist, /src/DevPulse.Api/wwwroot (artefakt buildu)
*.user, *.suo, .vs/
.env, .env.local (odczyt zablokowany regułą deny)
/claude-sessions (zaakceptowane plany – dokumentacja, nie kod; czytaj tylko pod konkretne zadanie)

## Praca autonomiczna (dopisane do CLAUDE.md)
- Nie commituj – commity robi człowiek po review
- Nie modyfikuj Program.cs – potrzebne rejestracje DI zapisz w docs/di/[moduł].md
- Niejednoznaczności zapisuj w docs/questions.md (pytanie + Twoja decyzja) i kontynuuj
- Kontrolery używają wyłącznie interfejsów z Application – nigdy typów z Infrastructure
