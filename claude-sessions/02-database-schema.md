# DevPulse – schemat bazy danych PostgreSQL

> Sesja 02 · 2026-10-21 · zaakceptowany schemat bazy danych (bazuje na `01-architecture-plan.md`).

## 1. Konwencje ogólne
| Temat | Decyzja | Uzasadnienie |
|---|---|---|
| Nazewnictwo | `snake_case`, tabele w liczbie mnogiej (`EFCore.NamingConventions`, `UseSnakeCaseNamingConvention()`). Encje C# w PascalCase. | Idiomatyczne w Postgresie, brak cudzysłowów w ręcznym SQL. |
| Klucze główne | `uuid`, generowane w aplikacji przez `Guid.CreateVersion7()`. | v7 jest uporządkowany w czasie, więc nie fragmentuje B-tree jak v4. Id nie zdradza liczby użytkowników w URL-ach (`/api/digests/{id}`). |
| Id z GitHuba | `bigint` (`github_id`, `github_repo_id`). | Id GitHuba nie mieszczą się bezpiecznie w `int`. Login i nazwa repo mogą się zmienić, id nie. |
| Czas | Zawsze `timestamptz` (UTC, `DateTimeOffset`/`DateTime` UTC w C#). Data lokalna digestu jako `date`. | Npgsql 6+ wymusza UTC dla `timestamptz`. Strefa użytkownika tylko w `time_zone_id`. |
| Enumy | `text` + `CHECK` (`HasConversion<string>()`). | Czytelne w SQL i logach, dodanie wartości to zmiana CHECK, nie przenumerowanie. Natywnych enumów PG nie używamy (trudne migracje). |
| Usuwanie | Fizyczne, `ON DELETE CASCADE` od `users` w dół. | Brak wymagań audytowych. Usunięcie konta czyści wszystko jednym DELETE. |
| Znaczniki czasu | `created_at` (default `now()`), `updated_at` tam, gdzie wiersz jest modyfikowany. | Diagnostyka. |
| Schematy | Tabele aplikacji w `public`, Hangfire w schemacie `hangfire`. | Hangfire zarządza swoimi tabelami sam. Separacja ułatwia backupy i przegląd. |

## 2. Diagram relacji
```
users 1───1 user_settings
  │
  ├──* tracked_repositories 1───0..1 repository_snapshots
  │
  └──* digests

data_protection_keys   (bez relacji, infrastruktura)
hangfire.*             (zarządzane przez Hangfire.PostgreSql, poza EF)
```

---

## 3. Tabele

### 3.1 `users`
Konto użytkownika tworzone przy pierwszym logowaniu GitHub OAuth. Przechowuje zaszyfrowany access token.

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `github_id` | `bigint` | NOT NULL, UNIQUE | Stabilny identyfikator konta GitHub. |
| `login` | `varchar(39)` | NOT NULL | Limit GitHuba to 39 znaków. Aktualizowany przy każdym logowaniu. |
| `avatar_url` | `varchar(512)` | NULL | |
| `access_token_encrypted` | `text` | NOT NULL | Payload Data Protection (base64url). Zob. 4.1. |
| `token_scopes` | `varchar(256)` | NOT NULL | Scopes z odpowiedzi OAuth (np. `read:user,user:email,repo`), żeby wykryć braki po zmianie wymaganych scopes. |
| `token_invalid` | `boolean` | NOT NULL, default `false` | Ustawiane po 401 z GitHuba. Joby pomijają takiego użytkownika, UI wymusza ponowne logowanie. |
| `token_updated_at` | `timestamptz` | NOT NULL | Kiedy token został ostatnio zapisany (i przeszyfrowany bieżącym kluczem). |
| `last_login_at` | `timestamptz` | NOT NULL | Używane w definicji „aktywnego użytkownika” (14 dni). |
| `created_at` | `timestamptz` | NOT NULL, default `now()` | |
| `updated_at` | `timestamptz` | NOT NULL | |

**Indeksy**
- `ux_users_github_id` UNIQUE (`github_id`): każde logowanie szuka użytkownika po `github_id` (upsert). Gwarantuje też jedno konto DevPulse na konto GitHub.
- Brak indeksu na `last_login_at`. `SnapshotRefreshJob` co 15 min wybiera aktywnych użytkowników przez `last_login_at > now() - 14d OR digest_enabled` (OR po dwóch tabelach), więc indeks i tak by nie pomógł. Przy tysiącach wierszy seq scan trwa poniżej milisekundy.

### 3.2 `user_settings`
Ustawienia 1:1 z `users`. Tworzone razem z użytkownikiem i wypełniane wartościami domyślnymi.

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `user_id` | `uuid` | PK, FK → `users.id` ON DELETE CASCADE | Współdzielony klucz 1:1. |
| `email` | `varchar(254)` | NULL | Potwierdzony adres docelowy digestu. Domyślnie primary verified email z GitHuba. |
| `email_confirmed` | `boolean` | NOT NULL, default `false` | `true` dla adresu z GitHuba (już zweryfikowany) i po kliknięciu w link. |
| `pending_email` | `varchar(254)` | NULL | Nowy adres czekający na potwierdzenie. Digest dalej idzie na `email`. |
| `pending_email_token_hash` | `bytea` | NULL | SHA-256 tokenu z linku. Sam token nie jest przechowywany. Zob. 4.2. |
| `pending_email_token_expires_at` | `timestamptz` | NULL | Np. 24 h od wysłania linku. |
| `send_hour` | `smallint` | NOT NULL, default `7`, CHECK `send_hour BETWEEN 0 AND 23` | Pełna godzina lokalna. |
| `time_zone_id` | `varchar(64)` | NOT NULL, default `'UTC'` | Strefa IANA (np. `Europe/Warsaw`). Poprawność sprawdza FluentValidation przez `TimeZoneInfo.TryFindSystemTimeZoneById`. |
| `digest_enabled` | `boolean` | NOT NULL, default `true` | |
| `updated_at` | `timestamptz` | NOT NULL | |

**Ograniczenia dodatkowe**
- CHECK `(pending_email IS NULL) = (pending_email_token_hash IS NULL)`: oczekujący adres zawsze ma token i odwrotnie.

**Indeksy**
- PK (`user_id`): odczyt ustawień zalogowanego użytkownika.
- `ux_user_settings_pending_email_token_hash` UNIQUE (`pending_email_token_hash`) WHERE `pending_email_token_hash IS NOT NULL`: endpoint potwierdzenia dostaje tylko token z linku (nie wymaga zalogowania), więc musi znaleźć wiersz po hashu. Indeks częściowy obejmuje tylko oczekujące potwierdzenia, czyli zwykle kilka wierszy.
- Brak indeksu pod `DigestSchedulerJob`. Job raz na godzinę ładuje wszystkich z `digest_enabled = true` i filtruje w aplikacji przez `TimeZoneInfo` (lokalna godzina == `send_hour`). Lokalnej godziny nie da się zindeksować bez przeliczania stref w SQL, a skala (tysiące wierszy raz na godzinę) tego nie wymaga.

### 3.3 `tracked_repositories`
Repozytoria GitHub wybrane przez użytkownika do śledzenia.

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `user_id` | `uuid` | NOT NULL, FK → `users.id` ON DELETE CASCADE | Właściciel (izolacja użytkowników). |
| `github_repo_id` | `bigint` | NOT NULL | Stabilne id (`databaseId` z GraphQL). |
| `github_node_id` | `varchar(64)` | NOT NULL | Globalne id GraphQL (`R_kgDO…`). Pozwala pobrać wiele repo przez `nodes(ids:)` zamiast aliasów `owner/name`, co działa też po zmianie nazwy repo. |
| `owner` | `varchar(39)` | NOT NULL | |
| `name` | `varchar(100)` | NOT NULL | |
| `full_name` | `varchar(140)` | NOT NULL | `owner/name`, aktualizowane przy odświeżeniu (repo mogą zmieniać nazwę i być przenoszone). |
| `html_url` | `varchar(512)` | NOT NULL | |
| `default_branch` | `varchar(255)` | NOT NULL | Aktualizowane przy odświeżeniu. |
| `is_private` | `boolean` | NOT NULL | Do oznaczenia w UI. |
| `created_at` | `timestamptz` | NOT NULL, default `now()` | |
| `updated_at` | `timestamptz` | NOT NULL | |

**Indeksy**
- `ux_tracked_repositories_user_id_github_repo_id` UNIQUE (`user_id`, `github_repo_id`): ten sam użytkownik nie doda repo dwa razy (ochrona przed podwójnym kliknięciem i wyścigiem). Ponieważ `user_id` jest pierwszą kolumną, indeks obsługuje też wszystkie zapytania „repo użytkownika X” (dashboard, ustawienia, refresh job), więc osobny indeks na `user_id` jest zbędny. Pokrywa też FK.

**Uwaga projektowa:** to samo repo GitHub śledzone przez dwóch użytkowników to dwa wiersze i dwa snapshoty. Jest to świadoma decyzja: brak pracy zespołowej, a każdy użytkownik pobiera dane własnym tokenem, z własnymi uprawnieniami i limitem. Współdzielony cache między kontami mógłby wyciec dane prywatnego repo do kogoś, kto stracił do niego dostęp.

### 3.4 `repository_snapshots`
Cache stanu repozytorium (wynik ostatniego odświeżenia z GitHuba). Relacja 1:1 z `tracked_repositories`, wiersz jest nadpisywany (upsert), bez historii.

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `tracked_repository_id` | `uuid` | PK, FK → `tracked_repositories.id` ON DELETE CASCADE | |
| `open_pr_count` | `integer` | NOT NULL, CHECK `>= 0` | `pullRequests(states: OPEN).totalCount`. |
| `open_issue_count` | `integer` | NOT NULL, CHECK `>= 0` | `issues(states: OPEN).totalCount` (bez PR-ów). |
| `open_pull_requests` | `jsonb` | NOT NULL, default `'[]'` | Top 10 otwartych PR (najnowsze): `[{number, title, url, author, createdAt, isDraft}]`. |
| `ci_status` | `varchar(16)` | NOT NULL, CHECK IN (`'Success'`,`'Failure'`,`'Pending'`,`'None'`) | `statusCheckRollup.state` ostatniego commita na default branch, zmapowany. |
| `ci_commit_sha` | `char(40)` | NULL | Commit, którego dotyczy status (link do checków w UI). |
| `last_activity_at` | `timestamptz` | NULL | `pushedAt` repo (NULL dla pustego repo). |
| `refreshed_at` | `timestamptz` | NOT NULL | Czas ostatniego **udanego** pobrania danych. |
| `last_attempt_at` | `timestamptz` | NOT NULL | Czas ostatniej próby (udanej lub nie). |
| `last_error` | `varchar(500)` | NULL | Krótki, bezpieczny opis błędu ostatniej próby (np. `NotFound`, `Forbidden`), NULL po sukcesie. Bez treści odpowiedzi i tokenów. |

**Indeksy**
- PK (`tracked_repository_id`): dashboard robi `tracked_repositories WHERE user_id = @u LEFT JOIN repository_snapshots ON PK`. Jedno zapytanie po indeksach, < 10 ms.
- Brak indeksu na `refreshed_at`. Refresh job wybiera **użytkowników** (jedno zapytanie GraphQL na paczkę repo użytkownika), a nie przeterminowane snapshoty, więc nie przeszukuje tej tabeli po czasie.

### 3.5 `digests`
Historia wysłanych (i nieudanych) digestów, retencja 30 dni.

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `user_id` | `uuid` | NOT NULL, FK → `users.id` ON DELETE CASCADE | |
| `digest_date` | `date` | NOT NULL | Data lokalna użytkownika, dla której digest jest generowany. |
| `time_zone_id` | `varchar(64)` | NOT NULL | Strefa w chwili generowania (do wyświetlenia, jeśli użytkownik później ją zmieni). |
| `status` | `varchar(16)` | NOT NULL, CHECK IN (`'Pending'`,`'Sent'`,`'Failed'`) | |
| `subject` | `varchar(200)` | NULL | Temat maila (wypełniany przy renderze). |
| `recipient_email` | `varchar(254)` | NULL | Adres, na który faktycznie wysłano. |
| `priorities` | `jsonb` | NULL | `[{repoFullName, text, severity}]`, maks. 5 pozycji. |
| `repository_summary` | `jsonb` | NULL | Stan repo użyty jako wejście (liczniki, CI, aktywność). Do debugowania priorytetów i ewentualnego przyszłego widoku. |
| `html_body` | `text` | NULL | Wyrenderowany HTML dokładnie taki, jak w mailu. |
| `ai_used` | `boolean` | NOT NULL, default `false` | `false` = priorytety z fallbacku regułowego. |
| `ai_model` | `varchar(64)` | NULL | Np. `claude-sonnet-5-5`. |
| `ai_input_tokens` | `integer` | NULL | Monitoring kosztów AI per digest. |
| `ai_output_tokens` | `integer` | NULL | |
| `email_provider_message_id` | `varchar(128)` | NULL | `X-Message-Id` z SendGrid (korelacja z logami SendGrid). |
| `attempt_count` | `smallint` | NOT NULL, default `0` | Liczba prób wysyłki (retry Hangfire). |
| `error` | `varchar(1000)` | NULL | Ostatni błąd (bez danych wrażliwych). |
| `created_at` | `timestamptz` | NOT NULL, default `now()` | |
| `sent_at` | `timestamptz` | NULL | Wypełnione przy `status = 'Sent'`. |

**Ograniczenia dodatkowe**
- CHECK `(status = 'Sent') = (sent_at IS NOT NULL)`.

**Indeksy**
- `ux_digests_user_id_digest_date` UNIQUE (`user_id`, `digest_date`): (1) idempotencja z planu M9: `SendDigestJob` najpierw wstawia wiersz `Pending`, a drugi równoległy job (np. scheduler uruchomiony dwa razy) dostaje unique violation i kończy pracę bez wysyłki; (2) obsługuje listę historii `WHERE user_id = @u AND digest_date >= @d ORDER BY digest_date DESC` przez skan indeksu wstecz; (3) pokrywa FK na `user_id`.
- `ix_digests_created_at` (`created_at`): `RetentionJob` codziennie robi `DELETE … WHERE created_at < now() - 30 days` dla wszystkich użytkowników. Unikalny indeks zaczyna się od `user_id`, więc nie pomaga. Bez tego indeksu DELETE czytałby całą tabelę razem z dużymi kolumnami TOAST. Koszt indeksu przy jednym INSERT dziennie na użytkownika jest pomijalny.

### 3.6 `data_protection_keys`
Pierścień kluczy ASP.NET Data Protection (`PersistKeysToDbContext<DevPulseDbContext>`). Kontenery Railway są efemeryczne (decyzja 3 z planu 01).

| Kolumna | Typ | Ograniczenia | Opis |
|---|---|---|---|
| `id` | `integer` | PK, identity | Schemat narzucony przez `IDataProtectionKeyContext`. |
| `friendly_name` | `text` | NULL | |
| `xml` | `text` | NULL | Opis klucza. Zaszyfrowany certyfikatem, zob. 4.1. |

**Indeksy:** tylko PK. Tabela ma kilka wierszy i jest czytana w całości przy starcie i przy odświeżaniu pierścienia.

### 3.7 Tabele Hangfire (`hangfire.*`)
Tworzone i migrowane przez `Hangfire.PostgreSql` (`PrepareSchemaIfNecessary = true`, `SchemaName = "hangfire"`). Nie są częścią modelu EF ani migracji EF.

---

## 4. Odpowiedzi na kwestie szczególne

### 4.1 Tokeny GitHub: szyfrowanie, nie hash
- **Hash nie wchodzi w grę.** Token jest nam potrzebny w postaci jawnej do wywołań GitHub API w jobach. Hash służy tylko do porównywania (np. tokenu z linku emailowego), a z niego nie da się odzyskać wartości.
- **Szyfrowanie: ASP.NET Data Protection** (`ITokenProtector` w Application, implementacja w `Infrastructure/Security`):
  - osobny protector z purpose `DevPulse.GitHubAccessToken.v1`, odseparowany kryptograficznie od cookie auth i od innych zastosowań,
  - domyślny algorytm AES-256-CBC + HMAC-SHA256 (szyfrowanie z uwierzytelnieniem): podmieniony ciphertext nie da się odszyfrować,
  - wynik w `users.access_token_encrypted` jako `text`. Payload zawiera id klucza, więc rotacja kluczy (domyślnie co 90 dni) nie psuje starych tokenów. Stare klucze zostają w pierścieniu do odszyfrowania.
- **Ochrona kluczy w spoczynku:** klucze leżą w tej samej bazie co ciphertext, więc sam zrzut bazy wystarczyłby do odszyfrowania. Dlatego `ProtectKeysWithCertificate(...)` z certyfikatem X.509 (PFX w base64) w zmiennej środowiskowej Railway. Wyciek bazy bez sekretów środowiska nie ujawnia tokenów.
- **Cykl życia:** przy każdym logowaniu token jest nadpisywany (i szyfrowany bieżącym kluczem), `token_invalid = false`. Po 401 z GitHuba: `token_invalid = true`, a token pozostaje do nadpisania przy kolejnym logowaniu. Usunięcie konta kasuje wiersz. Tokenów OAuth App nie odświeżamy (nie wygasają), więc nie ma kolumny refresh token.
- **Nigdy w logach:** `access_token_encrypted` wykluczone z logowania encji (Serilog destructuring), DTO nigdy go nie zawierają.

### 4.2 Token potwierdzenia emaila: tu hash jest właściwy
Losowy token (32 bajty, `RandomNumberGenerator`) trafia tylko do linku. W bazie zapisujemy `SHA-256` (`bytea`) i czas wygaśnięcia. Token ma wysoką entropię, więc wystarczy szybki hash bez soli (inaczej niż przy hasłach). Wyciek bazy nie pozwala potwierdzić cudzego adresu.

### 4.3 Cache `repository_snapshots`: timestamp, bez kolumny TTL
- **Świeżość liczona z `refreshed_at`**, a progi są w konfiguracji (`Snapshot:RefreshInterval = 15 min`), nie w danych. Zmiana TTL nie wymaga migracji danych, a jedna kolumna zastępuje parę `refreshed_at` + `expires_at`, które mogłyby się rozjechać.
- **Kto i kiedy odświeża:**
  - `SnapshotRefreshJob` co 15 min dla aktywnych użytkowników bez `token_invalid` (bez sprawdzania wieku, bo interwał joba = TTL),
  - po dodaniu repo: natychmiastowy enqueue. Do tego czasu wiersza nie ma, a UI pokazuje „ładowanie” (LEFT JOIN zwraca NULL),
  - przed digestem: odświeżenie, jeśli `now() - refreshed_at > 15 min`.
- **Dashboard nigdy nie odświeża.** Zwraca `refreshed_at`, a UI pokazuje „odświeżono X min temu”.
- **Błędy:** przy nieudanej próbie stare dane zostają (lepsze niż puste), aktualizujemy tylko `last_attempt_at` i `last_error`. UI pokazuje ostrzeżenie, gdy `last_error IS NOT NULL` albo dane są starsze niż 1 h.
- **Upsert** przez `INSERT … ON CONFLICT (tracked_repository_id) DO UPDATE` (EF: odczyt + update w jednej transakcji albo `ExecuteSql`). Bez historii snapshotów, bo żadne wymaganie jej nie potrzebuje.
- **`open_pull_requests` jako `jsonb`, nie tabela:** lista jest zawsze czytana i zastępowana w całości, ma maks. 10 elementów, nie filtrujemy po niej i nie łączymy jej z innymi tabelami. Osobna tabela oznaczałaby DELETE + 10 INSERT-ów co 15 min na każde repo, bez żadnej korzyści. Mapowanie przez `OwnsMany(...).ToJson()` (EF Core) na `List<OpenPullRequest>`.

### 4.4 Historia digestów: hybryda HTML + JSON, bez tabel pozycji
- **`html_body` (`text`)** to źródło prawdy dla podglądu: dokładnie to, co dostał użytkownik (decyzja 10 z planu 01). Szablon może się zmienić, a stare digesty dalej wyglądają jak w dniu wysyłki.
- **`priorities` i `repository_summary` (`jsonb`)** to dane strukturalne do listy historii (np. „3 priorytety, 1 failing CI”) bez parsowania HTML, do debugowania jakości AI i do ewentualnego przyszłego widoku natywnego.
- **Dlaczego nie relacyjnie (`digest_priorities`, `digest_repository_items`):** dane są niezmienne po zapisie, zawsze czytane w całości razem z digestem, nigdy nie odpytywane przekrojowo („wszystkie priorytety dla repo X”), a po 30 dniach usuwane. Tabele podrzędne dałyby więcej JOIN-ów, więcej INSERT-ów i kaskadowe DELETE przy retencji, bez żadnego zapytania, które by z nich korzystało. Jeśli kiedyś pojawi się analityka priorytetów, `jsonb` da się odpytać (`jsonb_array_elements`) albo zmigrować.
- **Rozmiar:** HTML ok. 20–50 KB na digest, TOAST kompresuje go automatycznie. 1000 użytkowników × 30 dni ≈ 30 tys. wierszy, czyli ok. 1 GB przed kompresją. Lista historii **nie** pobiera `html_body` (projekcja w zapytaniu), robi to tylko `GET /api/digests/{id}`.
- **Retencja:** `RetentionJob` usuwa po `created_at < now() - 30 dni` (indeks `ix_digests_created_at`). Endpoint listy dodatkowo filtruje po 30 dniach, więc wynik jest poprawny także między przebiegami joba.

---

## 5. Mapowanie na encje Domain (dla M0/M1)
| Tabela | Encja | Uwagi EF |
|---|---|---|
| `users` | `User` | `HasIndex(GitHubId).IsUnique()` |
| `user_settings` | `UserSettings` | `HasOne<User>().WithOne().HasForeignKey<UserSettings>(UserId)` |
| `tracked_repositories` | `TrackedRepository` | unikalny indeks złożony |
| `repository_snapshots` | `RepositorySnapshot` + owned `OpenPullRequest` | `OwnsMany(p => p.OpenPullRequests, b => b.ToJson())` |
| `digests` | `Digest` | `Priorities` przez `OwnsMany(...).ToJson()`; `RepositorySummary` analogicznie |
| `data_protection_keys` | `DataProtectionKey` (z pakietu) | `IDataProtectionKeyContext` |

Konfiguracje w `src/DevPulse.Infrastructure/Persistence/Configurations/*Configuration.cs` (`IEntityTypeConfiguration<T>`).
