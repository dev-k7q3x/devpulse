# E3A – Integracja GitHub, śledzone repo, synchronizacja (M2, M3, M4)

Gałąź `feat/e3a-github-sync` · worktree `.claude/worktrees/agent-a809f2939a8d7d02c`

## Status
**Gotowe do merge.** Pełny `dotnet test` 273/273, frontend 35/35, review APPROVE (sugestie wdrożone). Bez commitu.

## Dziennik
- 2026-10-24 – start: gałąź od `main` (b6686ba), przegląd kontraktu (plan 03 §2, §7.2) i encji.
- 2026-10-24 – kod produkcyjny: `Infrastructure/GitHub` (klient REST+ETag, GraphQL `nodes(ids:)`, `GitHubRateLimitHandler`, budżet, token store), `RepositoryService`, `SnapshotRefreshJob` + `SnapshotRefreshScheduler`, `RepositoriesController`, polityka limitu dla `refresh=true`. Build bez ostrzeżeń.
- 2026-10-24 – testy Unit (fixture JSON + fake HttpMessageHandler): `GitHubClientTests`, `GitHubRateLimitHandlerTests`, `GitHubCiStatusMapperTests`, `RecurringScheduleTests` – **Unit 181/181 zielone**.
- 2026-10-24 – testy Integration napisane: `RepositoriesEndpointsTests` (401, filtr/paginacja/isTracked, cache vs refresh, 429 refresh, mapowanie 401/503/502, 400 walidacja i XSRF, 201 + enqueue, 409, 422, 404, izolacja A/B, DELETE własne/cudze) i `SnapshotRefreshJobTests` (zapis + rename, nadpisanie, błąd zachowuje dane, wyjątki GitHuba bez throw, brak snapshotu przy pierwszym błędzie, pojedyncze repo, cudze repo, wybór aktywnych użytkowników). **Nie uruchomione – Docker Desktop zatrzymany.**
- 2026-10-24 – `schema.d.ts` zregenerowany (API na 127.0.0.1:5191; 5181 zajmował Vite innej ścieżki); frontend type-check + 35/35; `dotnet format` czysty.
- 2026-10-24 – Docker Desktop padł w trakcie pracy (przeciążenie trzema ścieżkami); nie restartowałem go z worktree.
- 2026-10-25 – po przywróceniu Dockera (blokada `.docker-lock`): testy integracyjne E3A 26/26 za pierwszym razem. Review APPROVE; poprawki z review (wyścigi zapisu, uszkodzony JSON, nieodszyfrowywalny token → `token_invalid`) + testy `GitHubTokenStoreTests`. **Pełny `dotnet test`: 273/273** (Unit 182 + Integration 91), `dotnet format` czysty.

## Pytania i decyzje
1. **`IHttpClientFactory` wymaga paczki `Microsoft.Extensions.Http`, której Infrastructure nie ma.** → Bez nowej paczki: `HttpClient` singleton z `SocketsHttpHandler` (`PooledConnectionLifetime` 5 min – rotacja DNS) i `GitHubRateLimitHandler` jako `DelegatingHandler`, rejestrowany fabryką w `GitHubModule`.

2. **Które wywołania są „interaktywne” (rezerwa 500)?** `IGitHubClient` nie ma flagi. → Wg metody: `GetUserRepositoriesAsync` i `GetRepositoryAsync` (available, POST) – interaktywne; `GetRepositoryStatesAsync` (snapshoty w jobach) – nie. Bez zmiany interfejsu z Application.
3. **Ponowienia joba snapshotu.** → `AutomaticRetry(Attempts = 0)`: błąd GitHuba zapisuje się w snapshocie (`last_error`: `TokenInvalid`/`RateLimited`/`Unavailable`/`NotFound`/`Forbidden`), a kolejne odświeżenie i tak przychodzi za 15 min. Recurring rozdziela pracę na joby per użytkownik (izolacja błędów).
4. **Błąd przy pierwszym snapshocie (brak wiersza).** `RecordFailure` wymaga istniejącego snapshotu, a utworzenie pustego pokazałoby zera jako dane. → Wiersza nie tworzymy, karta zostaje w stanie `Loading` do kolejnej udanej próby; błąd w logach.
5. **Polityka `refresh=true`.** Atrybut nie może zależeć od query. → Polityka `github-read` wybiera partycję po parametrze `refresh` (`github-refresh:…` z limitem 2/min albo `github-read:…` 20/min). Zmiana w `Api/RateLimiting/RateLimitingServiceCollectionExtensions.cs` (poza listą plików ścieżki, ale nikt inny go nie edytuje).
6. **Rejestracja recurring joba** – `SnapshotRefreshScheduler` (`BackgroundService` w `GitHubModule`) ponawia `AddOrUpdate` co 30 s, aż storage Hangfire odpowie – aplikacja nadal wstaje bez bazy.
7. **Konfiguracja `GitHub` (timeout, rezerwa, czas cache)** – wartości domyślne w `GitHubOptions`; sekcji nie dopisuję do `appsettings.json` (plik konfiguracyjny + ryzyko konfliktu z E3B/E3C). Nadpisanie: `GitHub__RateLimitReserve` itd.
8. **Pusty default branch / brak `statusCheckRollup`** – pusty branch zostawia dotychczasową nazwę; brak rollupu = `CiStatus.None`; `ERROR` → `Failure`, `EXPECTED` → `Pending`.

9. **Testy endpointów a Hangfire i GitHub.** → Fabryka pochodna (raz na kolekcję) podmienia `IGitHubClient` na `FakeGitHubClient` i `IBackgroundJobClient` na `RecordingBackgroundJobClient` – żaden job nie trafia do Hangfire i nic nie idzie do GitHuba. Job testowany bezpośrednio na prawdziwej bazie.

10. **Review (APPROVE) – wdrożone sugestie:** wyścigi zapisu snapshotu (23505 przy równoległym pierwszym snapshocie, 23503 przy usunięciu repo w trakcie) kończą job bez błędu; uszkodzony JSON od GitHuba → `github-unavailable`; token, którego nie da się odszyfrować, jest oznaczany jako nieważny (użytkownik wypada z odświeżania); testy `GitHubTokenStore` (Integration) i uszkodzonego JSON (Unit).
11. **Odłożone z review:** (a) błąd jednej paczki GraphQL (np. 3. z 3 przy 50 repo) oznacza błędem wszystkie snapshoty użytkownika – przy maks. 3 paczkach i odświeżeniu co 15 min akceptowalne, do ewentualnej poprawy w E6; (b) limit 50 repo sprawdzany nieatomowo – dwa równoległe POST przy 49 repo mogą dać 51; ogranicza to polityka `repo-mutations`, zabezpieczenie w bazie wymagałoby zmiany schematu (poza zakresem ścieżki).
12. **`Location: /api/repositories/{id}` bez endpointu GET** – zostaje, bo tak stanowi kontrakt (plan 03 §2).

## Do zrobienia ręcznie przez użytkownika
- Po zalogowaniu lokalnie (krok 5 z `docs/deploy/etap-2-kroki-recznie.md`): `GET http://localhost:5173/api/repositories/available` zwraca Twoje repo; `POST /api/repositories` dodaje repo, a po kilku sekundach w bazie jest wiersz w `repository_snapshots` (karta na dashboardzie w etapie 4).
- `/hangfire` nie jest jeszcze zmapowany (etap 6) – recurring `snapshot-refresh` widać w tabeli `hangfire.set` / `hangfire.hash` albo w logach („Recurring job snapshot-refresh registered”).

## Rejestracje DI
- Szczegóły: `docs/di/github.md`. Wszystko w `GitHubModule`; `Program.cs` bez zmian.
