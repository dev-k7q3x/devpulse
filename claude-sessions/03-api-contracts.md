# DevPulse – kontrakty REST API

> Sesja 03 · 2026-10-21 · zaakceptowane kontrakty REST API (bazuje na `01-architecture-plan.md` i `02-database-schema.md`).

## 0. Konwencje wspólne
| Temat | Decyzja |
|---|---|
| Prefiks i wersjonowanie | `/api/...` bez `v1`. SPA i API są wdrażane razem jako jeden serwis, więc nie ma klientów, którzy zostaliby na starej wersji. |
| Format | JSON w camelCase. Enumy jako stringi (`JsonStringEnumConverter`). Czas w ISO 8601 UTC (`2026-10-21T07:00:00Z`). `digestDate` jako `yyyy-MM-dd`. |
| Identyfikatory | Id DevPulse to `uuid` (string). `githubRepoId` to `number`: id GitHuba (ok. 10⁹) mieści się w bezpiecznym zakresie liczb JS (2⁵³). |
| DTO | `[Zasób][Akcja]Request/Response` w `Application/DTOs`. Walidacja przez FluentValidation. |
| Błędy | Zawsze `ProblemDetails` (RFC 9457, `AddProblemDetails()`). Pole `type` ma stały slug, po którym SPA rozpoznaje błąd (tabela 0.1). |
| Autoryzacja | **Domyślnie wymagane logowanie**: `FallbackPolicy = RequireAuthenticatedUser`. `[AllowAnonymous]` stoi tylko tam, gdzie zaznaczono 🔓. Dla `/api/*` API zwraca 401, a nie 302 (nadpisane `OnRedirectToLogin`). |
| Własność danych | Zasób innego użytkownika daje **404, a nie 403**, żeby nie zdradzać, że istnieje. Serwisy zawsze filtrują po `userId` (decyzja 11 z planu 01). |
| CSRF | Cookie `SameSite=Lax` plus antiforgery w trybie double-submit: cookie `XSRF-TOKEN` i nagłówek `X-XSRF-TOKEN`, wymagane dla POST/PUT/DELETE. |
| Rate limiting | Wbudowany `Microsoft.AspNetCore.RateLimiting`. Limity liczone per `userId`, a dla anonimowych per IP. Odmowa to 429 z nagłówkiem `Retry-After`. Licznik w pamięci wystarcza, bo na Railway działa jedna instancja (sekcja 7). |

### 0.1 Wspólne kody błędów
| Kod | `type` | Kiedy |
|---|---|---|
| 400 | `validation` | Błąd walidacji (`ValidationProblemDetails` z `errors: Record<string, string[]>`). |
| 400 | `invalid-token` | Nieprawidłowy, wygasły albo już użyty token potwierdzenia emaila. |
| 401 | `unauthenticated` | Brak cookie albo sesja wygasła. SPA przekierowuje na `/login`. |
| 401 | `github-token-invalid` | Token GitHuba został odwołany (`users.token_invalid = true`). Zwracane **tylko przez endpointy, które wołają GitHuba**. SPA wymusza ponowne logowanie. |
| 403 | `forbidden` | Brak polityki `Admin` (tylko `/hangfire`). |
| 404 | `not-found` | Brak zasobu albo zasób należy do kogoś innego. |
| 409 | `conflict` | Naruszenie unikalności (np. repo jest już śledzone). |
| 422 | `limit-exceeded` | Przekroczony limit biznesowy (np. maksymalna liczba repo). |
| 429 | `rate-limited` | Lokalny rate limiter. Nagłówek `Retry-After`. |
| 502 | `github-unavailable` | GitHub zwrócił 5xx albo timeout. |
| 503 | `github-rate-limited` | Kończy się budżet GitHub API użytkownika (7.2). Nagłówek `Retry-After` liczony z `x-ratelimit-reset`. |

---

## 1. Auth i konto (M1): `AuthController`
| Metoda i ścieżka | Auth | Opis |
|---|---|---|
| `GET /api/auth/login?returnUrl=` | 🔓 | Uruchamia challenge GitHub OAuth i przekierowuje (302) na GitHub. |
| `GET /signin-github` | 🔓 | Callback OAuth obsługiwany przez handler `AspNet.Security.OAuth.GitHub` (nie przez kontroler). Robi upsert `User`, zapisuje zaszyfrowany token i przekierowuje na `returnUrl`. |
| `POST /api/auth/logout` | 🔒 | Usuwa cookie sesji. |
| `GET /api/auth/me` | 🔒 | Zwraca zalogowanego użytkownika i stan jego tokenu GitHub. |
| `DELETE /api/auth/me` | 🔒 | Trwale usuwa konto: kaskada od `users` w dół i wylogowanie. |

**`GET /api/auth/login`**
- Query: `returnUrl?: string`. Akceptowana jest tylko ścieżka lokalna (`Url.IsLocalUrl`), domyślnie `/`.
- Response: `302 Location: https://github.com/login/oauth/authorize?...`
- Błędy: `400 validation` dla nielokalnego `returnUrl` (ochrona przed open redirect), `429` (polityka `anonymous`).
- Gdy użytkownik odmówi na GitHubie, callback przekierowuje na `/login?error=access_denied` (SPA pokazuje komunikat).

**`POST /api/auth/logout`**: brak body, odpowiedź `204`. Błędy: `401`.

**`GET /api/auth/me`** → `200 CurrentUserResponse`
| Pole | Typ | Opis |
|---|---|---|
| `id` | `uuid` | |
| `login` | `string` | |
| `avatarUrl` | `string \| null` | |
| `githubTokenValid` | `boolean` | `false`: UI pokazuje baner „Zaloguj się ponownie”. Dashboard i historia nadal działają, bo czytają z bazy. |
| `missingScopes` | `string[]` | Porównanie `token_scopes` z wymaganymi scopes. Niepusta lista oznacza konieczność ponownego logowania. |
| `isAdmin` | `boolean` | Pokazuje link do `/hangfire`. |

Błędy: `401 unauthenticated`. SPA wywołuje ten endpoint przy starcie i na tej podstawie działa `ProtectedRoute`.

**`DELETE /api/auth/me`**: brak body, odpowiedź `204`. Błędy: `401`, `429` (polityka `sensitive`). Usunięcie konta jest w zakresie, bo przechowujemy tokeny z dostępem do prywatnych repo.

---

## 2. Repozytoria (M3): `RepositoriesController`
| Metoda i ścieżka | Auth | Opis |
|---|---|---|
| `GET /api/repositories/available` | 🔒 | Paginowana lista repo z GitHuba, do których użytkownik ma dostęp, z flagą „już śledzone”. |
| `GET /api/repositories` | 🔒 | Lista śledzonych repo, czytana z bazy. |
| `POST /api/repositories` | 🔒 | Dodaje repo do śledzenia i od razu enqueue'uje pierwszy snapshot. |
| `DELETE /api/repositories/{id}` | 🔒 | Przestaje śledzić repo; snapshot usuwa kaskada. |

**`GET /api/repositories/available`** ⚠️ wywołuje GitHuba
- Query:

  | Pole | Typ | Domyślnie | Opis |
  |---|---|---|---|
  | `page` | `int ≥ 1` | `1` | |
  | `pageSize` | `int 1–100` | `50` | |
  | `search` | `string?` (maks. 100) | | Filtr po `fullName`, bez rozróżniania wielkości liter. |
  | `refresh` | `boolean` | `false` | Pomija cache (osobny, ostrzejszy limit). |
- Mechanizm: serwer pobiera **pełną** listę `GET /user/repos?per_page=100&affiliation=owner,collaborator,organization_member` (wszystkie strony, ETag per strona, więc odpowiedź 304 nie zużywa limitu). Listę trzyma w `IMemoryCache` per `userId` przez 10 min, a potem filtruje i paginuje w pamięci. Paginujemy po stronie serwera, bo wyszukiwanie musi objąć wszystkie repo, a paginacja GitHuba tego nie pozwala.
- Response `200 AvailableRepositoriesResponse`:

  | Pole | Typ |
  |---|---|
  | `items` | `AvailableRepositoryItem[]` |
  | `items[].githubRepoId` | `number` |
  | `items[].fullName` | `string` |
  | `items[].htmlUrl` | `string` |
  | `items[].isPrivate` | `boolean` |
  | `items[].description` | `string \| null` |
  | `items[].pushedAt` | `string (date-time) \| null` |
  | `items[].isTracked` | `boolean` |
  | `page`, `pageSize`, `totalCount` | `number` |
  | `fetchedAt` | `string (date-time)` (wiek cache, informacyjnie) |
- Błędy: `400`, `401 unauthenticated`, `401 github-token-invalid`, `429`, `502`, `503 github-rate-limited`.

**`GET /api/repositories`** → `200 TrackedRepositoryResponse[]`, bez paginacji (maks. 50, sekcja 6)
| Pole | Typ |
|---|---|
| `id` | `uuid` |
| `githubRepoId` | `number` |
| `fullName` | `string` |
| `htmlUrl` | `string` |
| `isPrivate` | `boolean` |
| `addedAt` | `string (date-time)` |

Błędy: `401`.

**`POST /api/repositories`**
- Request `RepositoryAddRequest`: `{ githubRepoId: number }`.
  Przyjmujemy samo id, a metadane (`node_id`, `owner`, `name`, `default_branch`, `is_private`) serwer bierze z cache listy available. Gdy w cache ich nie ma, pobiera je z `GET /repositories/{id}`. Klient nie może więc podsunąć fałszywej nazwy ani repo, do którego nie ma dostępu.
- Response `201 TrackedRepositoryResponse` z nagłówkiem `Location: /api/repositories/{id}`. Dashboard pokazuje kartę w stanie `Loading`, dopóki job nie zapisze snapshotu.
- Błędy: `400`, `401` (oba typy), `404 not-found` (repo nie istnieje albo brak dostępu), `409 conflict` (już śledzone, unikalny indeks), `422 limit-exceeded` (50 repo), `429`, `502`, `503`.

**`DELETE /api/repositories/{id}`**: odpowiedź `204`. Błędy: `401`, `404`.

---

## 3. Dashboard (M5): `DashboardController`
| Metoda i ścieżka | Auth | Opis |
|---|---|---|
| `GET /api/dashboard` | 🔒 | Karty wszystkich śledzonych repo, czytane wyłącznie ze snapshotów w bazie (nigdy z GitHuba). |

- Zapytanie: `tracked_repositories WHERE user_id = @u LEFT JOIN repository_snapshots`, posortowane po `fullName`. Bez paginacji: maks. 50 repo, a na każdym maks. 10 PR-ów.
- Cache HTTP: `ETag` liczony z `max(refreshed_at, last_attempt_at)` i liczby repo, plus `Cache-Control: private, no-cache`. Odpytanie TanStack Query co 15 min zwykle kończy się 304.
- Response `200 DashboardResponse`:

  | Pole | Typ | Opis |
  |---|---|---|
  | `summary.repositoryCount` | `number` | |
  | `summary.totalOpenPrs` | `number` | |
  | `summary.totalOpenIssues` | `number` | |
  | `summary.failingCiCount` | `number` | |
  | `oldestRefreshAt` | `string (date-time) \| null` | Do komunikatu „odświeżono X min temu”. |
  | `repositories` | `RepositoryCard[]` | |
  | `[].id` | `uuid` | |
  | `[].fullName`, `[].htmlUrl` | `string` | |
  | `[].isPrivate` | `boolean` | |
  | `[].state` | `"Loading" \| "Fresh" \| "Stale" \| "Error"` | `Loading`: brak snapshotu. `Stale`: snapshot starszy niż 1 h. `Error`: `last_error != null`. |
  | `[].openPrCount` | `number \| null` | `null` przy `Loading`. |
  | `[].openIssueCount` | `number \| null` | |
  | `[].openPullRequests` | `PullRequestItem[]` | Top 10: `{ number, title, url, author, createdAt, isDraft }`. |
  | `[].ci.status` | `"Success" \| "Failure" \| "Pending" \| "None"` | |
  | `[].ci.commitSha` | `string \| null` | |
  | `[].ci.checksUrl` | `string \| null` | `{htmlUrl}/commit/{sha}/checks` |
  | `[].lastActivityAt` | `string (date-time) \| null` | |
  | `[].refreshedAt` | `string (date-time) \| null` | |
  | `[].lastError` | `string \| null` | Bezpieczny kod, np. `NotFound` lub `Forbidden`. |
- Błędy: `401 unauthenticated`. **`github-token-invalid` nie występuje**, bo endpoint nie woła GitHuba.

---

## 4. Ustawienia (M6): `SettingsController`
| Metoda i ścieżka | Auth | Opis |
|---|---|---|
| `GET /api/settings` | 🔒 | Zwraca ustawienia digestu i stan adresu email. |
| `PUT /api/settings` | 🔒 | Aktualizuje godzinę wysyłki, strefę i włączenie digestu. |
| `POST /api/settings/email` | 🔒 | Rozpoczyna zmianę adresu i wysyła link potwierdzający na nowy adres. |
| `DELETE /api/settings/email/pending` | 🔒 | Anuluje oczekującą zmianę adresu. |
| `POST /api/settings/email/confirm` | 🔓 | Potwierdza nowy adres tokenem z linku. |

Wybór repozytoriów w ustawieniach korzysta z endpointów z sekcji 2 (bez duplikowania).

**`GET /api/settings`** → `200 SettingsResponse`
| Pole | Typ | Opis |
|---|---|---|
| `email` | `string \| null` | Potwierdzony adres docelowy. |
| `emailConfirmed` | `boolean` | |
| `pendingEmail` | `string \| null` | |
| `pendingEmailExpiresAt` | `string (date-time) \| null` | |
| `sendHour` | `number (0–23)` | |
| `timeZoneId` | `string` | IANA |
| `digestEnabled` | `boolean` | |
| `nextDigestAt` | `string (date-time) \| null` | Wyliczone w UTC przez `TimeZoneInfo`. `null`, gdy digest jest wyłączony albo brak potwierdzonego emaila. |

Błędy: `401`.

**`PUT /api/settings`**
- Request `SettingsUpdateRequest`:

  | Pole | Typ | Walidacja |
  |---|---|---|
  | `sendHour` | `number` | całkowita, 0–23 |
  | `timeZoneId` | `string` | maks. 64, `TimeZoneInfo.TryFindSystemTimeZoneById` |
  | `digestEnabled` | `boolean` | |
- Response `200 SettingsResponse` (pełny stan, żeby UI od razu dostało nowe `nextDigestAt`).
- Błędy: `400`, `401`.
- Uwaga: jeśli dzisiejszy digest już wyszedł, zmiana godziny nie spowoduje drugiej wysyłki tego samego dnia, bo chroni przed tym `UNIQUE (user_id, digest_date)`.

**`POST /api/settings/email`** ⚠️ koszt SendGrid i ryzyko spamu
- Request `EmailChangeRequest`: `{ email: string }` (format email, maks. 254).
- Response: `202 SettingsResponse` (z ustawionym `pendingEmail`). Wysyłka maila jest enqueue'owana w Hangfire.
- Ponowne wywołanie nadpisuje poprzedni pending i unieważnia stary token.
- Błędy: `400`, `401`, `409 conflict` (adres jest już aktualnym potwierdzonym), `429` (polityka `email`).

**`DELETE /api/settings/email/pending`**: odpowiedź `204` (idempotentne). Błędy: `401`.

**`POST /api/settings/email/confirm`** 🔓
- Request `EmailConfirmRequest`: `{ token: string }` (base64url, 43 znaki).
- Link w mailu prowadzi do strony SPA `/confirm-email?token=…`, a dopiero SPA wysyła POST. Gdyby link sam wywoływał GET ze skutkiem ubocznym, skanery linków w skrzynkach mogłyby potwierdzać adresy bez udziału użytkownika.
- Działa bez logowania (użytkownik może otworzyć mail na innym urządzeniu). Wiersz jest wyszukiwany po `SHA-256(token)`.
- Response `200 EmailConfirmResponse`: `{ email: string }`.
- Błędy: `400 validation` (zły format), `400 invalid-token` (token nie istnieje, wygasł albo został już użyty; jeden komunikat dla wszystkich przypadków, bez enumeracji), `429` (polityka `anonymous` per IP).

---

## 5. Historia digestów (M10): `DigestsController`
| Metoda i ścieżka | Auth | Opis |
|---|---|---|
| `GET /api/digests` | 🔒 | Lista digestów z ostatnich 30 dni, od najnowszego, bez HTML. |
| `GET /api/digests/{id}` | 🔒 | Szczegóły digestu: priorytety, status, metadane. |
| `GET /api/digests/{id}/html` | 🔒 | Wyrenderowany HTML maila do podglądu w sandboxowanym iframe. |

**`GET /api/digests`** → `200 DigestListItem[]`
- **Bez paginacji**: zakres jest z natury ograniczony do ≤ 31 wierszy (jeden digest dziennie, 30 dni), a projekcja pomija `html_body` i `repository_summary`, więc odpowiedź ma kilka KB. Filtr `digest_date >= today_local - 30` działa także między przebiegami `RetentionJob`.

  | Pole | Typ |
  |---|---|
  | `id` | `uuid` |
  | `digestDate` | `string (date)` |
  | `status` | `"Pending" \| "Sent" \| "Failed"` |
  | `subject` | `string \| null` |
  | `sentAt` | `string (date-time) \| null` |
  | `aiUsed` | `boolean` |
  | `priorities` | `DigestPriority[]` (maks. 5: `{ repoFullName, text, severity: "High" \| "Medium" \| "Low" }`) |

Błędy: `401`.

**`GET /api/digests/{id}`** → `200 DigestDetailResponse`: wszystkie pola z listy, a do tego:
| Pole | Typ |
|---|---|
| `timeZoneId` | `string` |
| `recipientEmail` | `string \| null` |
| `attemptCount` | `number` |
| `error` | `string \| null` (bezpieczny opis błędu, gdy `Failed`) |
| `hasHtml` | `boolean` |

Pola `ai_input_tokens`, `ai_output_tokens`, `ai_model` i `email_provider_message_id` **nie są wystawiane**: są potrzebne tylko do monitoringu i operacji.

Błędy: `401`, `404`.

**`GET /api/digests/{id}/html`** → `200 text/html; charset=utf-8`
- Nagłówki: `Content-Security-Policy: sandbox; default-src 'none'; img-src https: data:; style-src 'unsafe-inline'`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400, immutable` (treść po zapisie się nie zmienia).
- Iframe w SPA: `<iframe sandbox src="/api/digests/{id}/html">`. Osobny endpoint zamiast `srcdoc` pozwala nadać własne CSP i nie trzyma 50 KB HTML w JSON-ie.
- Błędy: `401`, `404` (także wtedy, gdy `html_body` jest `NULL`, np. przy statusie `Failed` przed renderem).

---

## 6. Operacje (M11): poza kontrolerami
| Ścieżka | Auth | Opis |
|---|---|---|
| `GET /health` | 🔓 | Liveness (proces działa) dla healthchecka Railway. `200` lub `503`. |
| `GET /health/ready` | 🔓 | Readiness: połączenie z PostgreSQL i działający serwer Hangfire. Odpowiedź bez szczegółów, tylko `Healthy` lub `Unhealthy`. |
| `/hangfire` | 🔒 polityka `Admin` | Dashboard Hangfire, dostępny dla GitHub loginów z `Admin:Logins`. Inni dostają `403`, niezalogowani `401`. |
| `GET /openapi/v1.json` | 🔓 tylko Development | Źródło dla `openapi-typescript`. W produkcji wyłączone. |

**Limity biznesowe** (konfiguracja, nie stałe w kodzie): `Repositories:MaxPerUser = 50`. Utrzymuje dashboard poniżej 2 s bez paginacji i ogranicza koszt GraphQL do 3 paczek po 20 repo co 15 min.

---

## 7. Rate limiting: gdzie są wąskie gardła

### 7.1 Polityki lokalne (ASP.NET RateLimiter)
| Polityka | Algorytm i limit | Partycja | Endpointy | Uzasadnienie |
|---|---|---|---|---|
| `default` | token bucket, 120 req/min | userId | wszystkie 🔒 bez innej polityki | Ochrona przed zapętlonym klientem. Dashboard i historia czytają z bazy, więc to tylko bezpiecznik. |
| `github-read` | 20 req/min | userId | `GET /repositories/available` | Przy chybionym cache jedno wywołanie to N stron `GET /user/repos`. |
| `github-refresh` | 2 req/min | userId | `available?refresh=true` | Pominięcie cache zawsze kosztuje limit GitHuba. |
| `repo-mutations` | 30 req/min | userId | `POST`/`DELETE /repositories` | Każdy POST może wywołać GitHuba i enqueue'uje job snapshotu. |
| `email` | 3/h oraz 10/dobę (chained) | userId | `POST /settings/email` | Koszt SendGrid, reputacja domeny, ochrona przed spamowaniem cudzych adresów. |
| `anonymous` | 10 req/min | IP (`X-Forwarded-For` od proxy Railway przez `ForwardedHeaders`) | `/auth/login`, `/settings/email/confirm` | Ochrona przed brute force tokenu i nadużyciem OAuth. |
| `sensitive` | 3/h | userId | `DELETE /auth/me` | |

Odpowiedź przy przekroczeniu: `429 rate-limited` z `Retry-After`. Licznik jest w pamięci, co jest poprawne przy jednej instancji Railway. Przy skalowaniu w poziomie trzeba go przenieść do magazynu współdzielonego (znane ograniczenie).

### 7.2 Budżet GitHub API (5000 req/h per token)
- `GitHubRateLimitHandler` (DelegatingHandler w M2) czyta `x-ratelimit-remaining` i `x-ratelimit-reset` z każdej odpowiedzi i trzyma je per `userId` w pamięci.
- **Rezerwa dla jobów:** gdy `remaining < 500`, żądania interaktywne (`available`, `POST /repositories`) dostają `503 github-rate-limited`, a budżet zostaje dla `SnapshotRefreshJob` i digestu.
- Odpowiedź 403 lub 429 z GitHuba z `retry-after` (secondary rate limit) również mapujemy na `503 github-rate-limited`.
- Dashboard i historia **nigdy** nie zużywają budżetu GitHuba.

### 7.3 Koszt AI
- **Żaden endpoint HTTP nie wywołuje Anthropic API.** AI woła wyłącznie `SendDigestJob`, a `UNIQUE (user_id, digest_date)` gwarantuje co najwyżej jedno wywołanie na użytkownika na dobę (plus retry Hangfire tego samego joba).
- Koszt jest więc ograniczony z góry przez `liczbę użytkowników × 1/dzień × max_tokens ≈ 400`, bez potrzeby rate limitera po stronie API.

---

## 8. Mapowanie na kontrolery i moduły
| Kontroler | Endpointy | Moduł |
|---|---|---|
| `AuthController` | `/api/auth/*` | M1 |
| `RepositoriesController` | `/api/repositories*` | M3 |
| `DashboardController` | `/api/dashboard` | M5 |
| `SettingsController` | `/api/settings*` | M6 |
| `DigestsController` | `/api/digests*` | M10 |
| `Program.cs` | `/health*`, `/hangfire`, `/openapi`, SPA fallback (`MapFallbackToFile("index.html")` tylko poza `/api`) | M0, M11 |

---

## 9. Podjęte decyzje i świadomie pominięte endpointy
| Temat | Decyzja | Uzasadnienie |
|---|---|---|
| Ręczne odświeżanie dashboardu (`POST /api/dashboard/refresh`) | **Pominięte** | CLAUDE.md: odświeżanie co 15 min wystarczy. Po dodaniu repo snapshot i tak jest enqueue'owany od razu. |
| Testowy digest na żądanie (`POST /api/digests/test`) | **Pominięte** | To byłby jedyny endpoint z kosztem AI na żądanie. Bez niego koszt AI pozostaje ograniczony konstrukcyjnie (7.3). Do testów lokalnych wystarczy ręczne uruchomienie joba z dashboardu Hangfire (admin). |
| Usunięcie konta (`DELETE /api/auth/me`) | **W zakresie** | Przechowujemy tokeny z dostępem do prywatnych repo, więc użytkownik musi móc je usunąć. Kaskada w schemacie robi to jednym DELETE. |
| Limit śledzonych repo | **50 per użytkownik** (konfigurowalne) | Dashboard bez paginacji < 2 s, przewidywalny koszt GraphQL, mały prompt AI. |
