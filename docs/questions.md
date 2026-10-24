# Pytania i decyzje podjęte w trakcie pracy

Format: pytanie → decyzja. Pozycje z etapu 2 zatwierdzone przez użytkownika 2026-10-24 („rób to, co rekomendujesz”).

## 2026-10-23 · Etap 2 – moduł Auth (`feat/auth-module`)

1. **Polecenie (JWT + refresh token, encja `RefreshToken`, AES z `DEVPULSE_TOKEN_KEY`, `/api/auth/github`, `/api/auth/callback`) było sprzeczne z planami 01–03.**
   → Decyzja użytkownika: realizować plany 01–03 (cookie + GitHub OAuth + Data Protection, endpointy z planu 03). Bez nowej migracji i bez encji `RefreshToken`.

2. **Nowy interfejs `IAuthService` – plan 01 wymieniał 11 interfejsów, a żaden nie obejmował upsertu użytkownika ani usunięcia konta.**
   → Dodany `Application/Interfaces/IAuthService.cs`, implementacja w `Infrastructure/Services/AuthService.cs` (rejestracja w `SecurityModule`). Kontroler i handler OAuth używają tylko interfejsu.

3. **Testy `AuthService` – jednostkowe czy integracyjne?** Projekt nie ma providera EF InMemory/SQLite (nowa paczka wymaga zgody), a mock `DbSet` z async LINQ nie sprawdzałby upsertu, projekcji ani `ExecuteDeleteAsync` z kaskadą.
   → `Integration/AuthServiceTests.cs` na prawdziwym PostgreSQL (Testcontainers). Czysta logika wydzielona i pokryta testami Unit: `GitHubScopes`, `GitHubClaimsMapper`, `CurrentUser`, `TokenProtector`, `SecurityModule`.

4. **Skąd cookie `XSRF-TOKEN`?** `IAntiforgery.GetAndStoreTokens` ustawia `Cache-Control: no-store`, więc wydawanie go przy każdym GET zepsułoby ETag/304 dashboardu (plan 03).
   → Wydaje go tylko `GET /api/auth/me`. Akcja ma `[AllowAnonymous]` (anonimowy `POST /api/settings/email/confirm` z E3C też potrzebuje tokenu), ale bez sesji nadal zwraca 401 `unauthenticated`.

5. **Email przy pierwszym logowaniu: „primary verified” (plan 04).** Handler `AspNet.Security.OAuth.GitHub` bierze publiczny email z profilu, a jeśli go nie ma – primary z `/user/emails`. Publiczny email na GitHubie jest zawsze zweryfikowany, ale nie musi być primary.
   → Zostawione zachowanie handlera (bez dodatkowego żądania do GitHuba). Użytkownik może zmienić adres w ustawieniach (E3C).

6. **Wyścig przy pierwszym logowaniu tym samym kontem w dwóch kartach** (unikalny `github_id`).
   → Bez obsługi: druga próba kończy się `/login?error=login_failed`, a ponowne logowanie działa. Przy tej skali nie warto komplikować kodu.

7. **Brak konfiguracji GitHub OAuth.** Handler OAuth bez `ClientId` rzuca wyjątek przy każdym żądaniu, nie tylko przy logowaniu.
   → Fail fast przy starcie we wszystkich środowiskach (też Development). Lokalnie trzeba ustawić user-secrets (`UserSecretsId` dodany do `DevPulse.Api.csproj`).

8. **Callback w developmencie.** `redirect_uri` jest liczony z hosta żądania. Przez proxy Vite to `http://localhost:5173/signin-github`, więc w `vite.config.ts` dodane proxy `/signin-github`. W GitHub OAuth App (dev) ustaw callback `http://localhost:5173/signin-github` (handoff z etapu 1 podawał port 5080).

9. **`ForwardedHeaders` ufa każdemu proxy** (wyczyszczone `KnownIPNetworks`/`KnownProxies`, `ForwardLimit = 1`). Railway nie publikuje stałych adresów proxy.
   → Przyjęte. Do weryfikacji na pierwszym deployu: czy Railway dopisuje IP klienta jako ostatni wpis `X-Forwarded-For`.

10. **Martwa sesja po usunięciu konta w innej przeglądarce.** Nie walidujemy principala w bazie przy każdym żądaniu (koszt zapytania).
    → `GET /api/auth/me` wylogowuje i zwraca 401, gdy użytkownika nie ma. Inne endpointy i tak filtrują po `userId`, więc nic nie zwrócą.

## 2026-10-24 · Etap 2 – domknięcie

11. **`dotnet ef` po dodaniu fail-fast budował host API i wymagał sekretów OAuth** (także przy budowie migration bundle w Dockerfile).
    → `Infrastructure/Persistence/DevPulseDbContextDesignTimeFactory` – narzędzia EF i bundle nie budują hosta. Connection string z `ConnectionStrings__Default`, domyślnie lokalny PostgreSQL z docker-compose.

12. **Otwarte decyzje z handoffu etapu 1 (§7).**
    → `.gitattributes` z `* text=auto eol=lf` – dodany (indeks już był w LF, renormalizacja niepotrzebna).
    → `libgssapi-krb5-2` w obrazie runtime – dodany.
    → Skill `devpulse-review` nie widzi nieśledzonych plików – zmiana w pluginie poza tym repo, obejście: `git add -A` przed review.

13. **Sugestie z review PR #2 (§6).**
    → Zrobione: komentarz o synchronicznym `Servers()` w `HangfireHealthCheck`, `FluentValidation.DependencyInjectionExtensions` przeniesione do Infrastructure, `User.RecordLogin` rzuca wyjątek zamiast obcinać scopes (+ test), komentarz przy przypiętym `Newtonsoft.Json`.
    → Odłożone: camelCase zagnieżdżonych kluczy w `ValidationFilter` (dopiero przy zagnieżdżonych requestach), stały kod błędu w `SendDigestJob` (E5A).

14. **Certyfikat Data Protection.**
    → Wygenerowany lokalnie (RSA 3072, ważny 10 lat) w katalogu `.devpulse` w profilu użytkownika, poza repo (`*.pfx` jest też w `.dockerignore`). Do przeniesienia do menedżera haseł.
