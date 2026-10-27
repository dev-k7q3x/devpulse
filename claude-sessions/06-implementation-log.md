# DevPulse – log implementacji (E1–E6)

> Sesja 06 · 2026-10-27 · podsumowanie implementacji na podstawie `git log`, `docs/progress/*`, `docs/questions.md`
> i `docs/session-summary.md`. Plan, do którego się odnosi: `04-implementation-plan.md`.

## 1. Przebieg etapów

| Etap | Commit / PR | Tryb pracy | Co powstało | Testy po etapie (backend / frontend) |
|---|---|---|---|---|
| Reguły pracy | `3583577` | – | allow/ask/deny w `.claude/settings.json`, sekcja „Praca autonomiczna” w CLAUDE.md | – |
| E1a Szkielet | `49f0b5a` · PR #1 | plan mode, potem ręczne zatwierdzanie | solution z 4 projektami + testy, `/health`, docker-compose, szkielet SPA, CI | – |
| E1b Fundament | `bc9deb7` · PR #2 | sekwencyjnie | wszystkie encje, migracja `InitialCreate`, 11 interfejsów, DTO z planu 03, walidatory, Hangfire, rate limiter, moduły DI per obszar | 141 / 14 |
| E2 Auth | `0566030` · PR #3 | auto mode | GitHub OAuth, cookie + antiforgery, Data Protection (klucze w bazie, PFX), Dockerfile z efbundle, `railway.json` | 198 / 35 |
| E3A ∥ E3B ∥ E3C | `ce9c6d0`, `ee75935`, `cdba095` · PR #5–#7 (+ #4 fix) | 3 worktree równolegle (subagenci), scalanie w sesji głównej | GitHub (GraphQL + REST/ETag) i snapshoty; composer, Anthropic, SendGrid; ustawienia i zmiana emaila | 390 / 47 |
| E4 ∥ E5B | `c379a21`, `e5bb239` · PR #8–#9 | 2 worktree równolegle | dashboard (ETag/304, bez GitHuba) i wybór repo; historia digestów, podgląd w sandboxie, retencja | 427 / 85 |
| E5A | `1aa9f3b` · PR #10 | worktree | `DigestSchedulerJob` (co godzinę), idempotentny `SendDigestJob` | 465 / 85 |
| E6 | `a18e730` · PR #11 | sekwencyjnie | `/hangfire` dla adminów, nagłówki bezpieczeństwa, smoke test E2E, runbook, instrukcja kroków ręcznych | 500 / 87 |
| Poprawki | `d82679f`, `4ca66bc`, `89140a4` · PR #12–#14 | interaktywnie | przekierowanie dev na Vite, doprecyzowanie kroków ręcznych po lokalnym teście A–C | – |

Kalendarz: plany 21.10, implementacja 22.10–26.10 wieczorami, lokalna weryfikacja 27.10.
Plan 04 szacował ≈ 68–84 h pracy jednej osoby (ścieżka krytyczna ≈ 49–60 h).

## 2. Momenty, w których korygowałem agenta albo plan

1. **Polecenie sprzeczne z planem (E2).** Prompt do modułu Auth prosił o JWT + refresh token, encję `RefreshToken`,
   AES z `DEVPULSE_TOKEN_KEY` i `/api/auth/callback`. Agent zapisał w `docs/questions.md` (pkt 1), że to przeczy planom 01–03.
   Decyzja: realizujemy plany (cookie + Data Protection). Zaakceptowany plan w repo wygrał z poleceniem pisanym z pamięci.
2. **Fail fast vs narzędzia EF (E2).** Po dodaniu fail fast bez sekretów OAuth `dotnet ef` i budowa bundla przestały działać,
   bo budowały host API. Rozwiązanie: `DevPulseDbContextDesignTimeFactory`.
3. **Paczki NuGet.** Agent nie dodał `Microsoft.Extensions.Http` ani SDK Anthropic/SendGrid bez zgody. Zostały typed/singletonowe
   `HttpClient` z `SocketsHttpHandler` (questions 15, 21).
4. **E5A – dwie rundy REQUEST_CHANGES w review.** `/devpulse-kit:devpulse-review` wykrył utratę digestu po restarcie procesu
   i możliwą podwójną wysyłkę. Poprawione (przejmowanie `Pending` starszych niż 20 min, retry na tym samym wierszu); APPROVE w 3. rundzie.
5. **Brak testowego digestu na żądanie.** Ręczny test idzie przez Hangfire → `digest-scheduler` → *Trigger now*,
   bez endpointu „wyślij teraz”, bo koszt AI ma być ograniczony konstrukcyjnie (plan 03 §9, questions 37).

## 3. Decyzje podjęte w trakcie implementacji (poza planami)

- Cookie `XSRF-TOKEN` wydaje tylko `GET /api/auth/me`, bo antiforgery ustawia `no-store` i zepsułoby ETag dashboardu (q4).
- ETag dashboardu szerszy niż w planie 03: karta, która przeszła w `Stale`, musi się odświeżyć (q23).
- Dashboard odpytywany co 30 s tylko, gdy któraś karta jest `Loading` (q24).
- Okno 30 dni historii liczone w strefie użytkownika; digest spoza okna → 404 (q26).
- `digests.error` przechowuje wyłącznie stały kod błędu, nigdy treść wyjątku (q27).
- Zmiana czasu: godzina nieistniejąca → tuż po przeskoku, powtórzona → jeden digest (q19, q29).
- `/hangfire` bez sesji → 401 ProblemDetails; uprawnienie admina sprawdzane po loginie z bazy (q35, q36).
- CSP SPA `script-src 'self'`; panel Hangfire bez CSP (skrypty inline) (q38).

## 4. Pułapki procesu pracy równoległej

- **Docker Desktop padał** przy trzech równoległych zestawach Testcontainers (E3, E4). Rozwiązanie: restart w sesji głównej
  i plik blokady `.claude/worktrees/.docker-lock` – testy integracyjne ścieżek uruchamiane kolejno.
- **Limit sesji API** przerwał wszystkie trzy ścieżki E3 (i raz E5A). Praca wznowiona po resecie bez strat, bo stan był
  zapisany w `docs/progress/*.md`.
- **Konflikty przy scalaniu** dotyczyły tylko generowanego `schema.d.ts` (regenerowany) i `queryKeys.ts` (obie sekcje).
  Plik DI per moduł (`Infrastructure/DependencyInjection/*Module.cs`) sprawił, że `Program.cs` nie był punktem konfliktu.

## 5. Statystyki (stan `main` po PR #14)

- 19 commitów bez merge, 14 PR-ów.
- 212 plików `.cs` (≈ 17,4 tys. linii w `src/` i `tests/`), ≈ 3,4 tys. linii TS/TSX we `frontend/src` (bez `schema.d.ts`).
- Testy: backend 500 (Unit + Integration na Testcontainers, + 2 External uruchamiane ręcznie), frontend 87.
- 17 endpointów, każdy z polityką rate limitingu sprawdzaną testem.

## 6. Ocena

**Działało:** plany 01–04 jako źródło prawdy (agent sam wskazał sprzeczne polecenie), moduł DI per obszar zamiast edycji `Program.cs`,
`docs/progress/*.md` jako pamięć ścieżek przetrwała przerwy, review przed każdym PR-em łapało realne błędy (E5A).

**Wymagało korekty:** równoległe Testcontainers przeciążają Docker Desktop; deploy zaplanowany w E2 jest odłożony,
bo Railway wstrzymał rejestracje, więc problemy produkcyjne (proxy, PFX, OAuth callback) są sprawdzone tylko lokalnie na obrazie w trybie Production.

**Zostało:** deploy na Railway (`docs/deploy/kroki-reczne.md`, sekcje D–F), `/devpulse-kit:pre-deploy`, finalne review całości.
