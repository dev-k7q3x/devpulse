# E3C – Ustawienia (M6) · postęp

## Status
**Gotowe** – backend 249/249 (Unit + Integration), frontend 47/47, review APPROVE; czeka na scalenie (kolejność: E3A → E3B → E3C).

## Dziennik
- 2026-10-24 – start: gałąź `feat/e3c-settings` z `main` (b6686ba), przeczytany kontrakt (plan 03 §4), DTO, walidatory i encja `UserSettings` z E1.
- 2026-10-24 – backend: `SettingsController`, `SettingsService`, `NextDigestCalculator`, `EmailConfirmationToken`, `SendEmailConfirmationJob`, `EmailConfirmationMessage`, `LoggingEmailSender`, `SettingsModule`, sekcja `App` w appsettings.json. Build bez ostrzeżeń, testy Unit 164/164.
- 2026-10-24 – testy integracyjne napisane (`SettingsEndpointsTests`, `SendEmailConfirmationJobTests`), ale **Docker Desktop przestał odpowiadać** (`npipe dockerDesktopLinuxEngine` nie istnieje) – Testcontainers nie wystartują. Sandbox worktree nie pozwala mi uruchomić Docker Desktop.
- 2026-10-24 – frontend: `SettingsPage` (`ScheduleForm`, `EmailSection`), `ConfirmEmailPage`, `useSettings`, `lib/dateTime.ts`, `queryKeys.settings`, regenerowany `schema.d.ts`. Vitest 47/47, lint, type-check, prettier, build zielone. `dotnet format` czysty.
- 2026-10-24 – `/devpulse-kit:devpulse-review`: APPROVE, sugestie wdrożone (pkt 11). Unit 165/165.
- 2026-10-25 – Docker wrócił (restart przez sesję główną). Poprawiony seed w `SendEmailConfirmationJobTests` (brak `.Build()`). Testy integracyjne E3C 25/25, pełny `dotnet test` **249/249** (z blokadą `.docker-lock`, kolejno z innymi ścieżkami).

## Pytania i decyzje
1. **Kolejność wysyłki przy zmianie czasu** (reguła dla `DigestSchedulerJob` z E5A): nieistniejąca godzina lokalna (wiosną) → pierwszy moment po luce (Warszawa 02:00 → 03:00 CEST); powtórzona godzina (jesienią) → tylko pierwsze wystąpienie. Implementacja: `Application/Settings/NextDigestCalculator` – E5A powinien użyć tej samej klasy.
2. **`nextDigestAt` pomija dni, w których digest już powstał** (`UNIQUE user_id + digest_date`) – zmiana godziny na późniejszą tego samego dnia pokazuje jutro, zgodnie z uwagą w planie 03 §4.
3. **Token w argumentach Hangfire**: argumenty jobów leżą w `hangfire.job` jawnym tekstem, więc token jest szyfrowany Data Protection (purpose `DevPulse.EmailConfirmationToken.v1`), a job sprawdza przed wysyłką, czy hash nadal pasuje (ponowienie po nowszej zmianie albo anulowaniu niczego nie wysyła).
4. **Mail potwierdzający** w tej ścieżce (decyzja sesji głównej): `Infrastructure/Email/EmailConfirmationMessage` (HTML + tekst) i `Infrastructure/Jobs/SendEmailConfirmationJob`. E3B robi tylko szablon digestu i `SendGridEmailSender`.
5. **`/confirm-email` wymaga kliknięcia „Potwierdź adres”** zamiast automatycznego POST po otwarciu: skanery linków wykonujące JavaScript też nie potwierdzą adresu, a React StrictMode nie wyśle POST dwa razy (drugi dałby błąd „użyty token”).
6. **Minimalna zmiana w `app/routes.tsx`**: `/confirm-email` musi być poza `ProtectedRoute`, a `settingsRoutes` są zagnieżdżone w layoucie – dodany eksport `settingsPublicRoutes` i jedna linia `...settingsPublicRoutes` w `app/routes.tsx`.
7. **`lib/dateTime.ts`** (nowy katalog, reguła frontendu: formatowanie dat i stref tylko przez helpery z `lib/`) – `browserTimeZone`, `availableTimeZones`, `formatDateTime`. Przyda się E4/E5B.
8. **Strefa z przeglądarki**: nowe konto ma strefę `UTC` (default z E1); formularz proponuje strefę przeglądarki z podpowiedzią „zapisz, aby ją ustawić” – bez cichego zapisu.
9. **Nowa klasa opcji `AppOptions`** (`Application/Options/AppOptions.cs`, osobny plik, żeby nie konfliktować z E3A w `DevPulseOptions.cs`).
11. **Review (`/devpulse-kit:devpulse-review`): APPROVE.** Wdrożone: `LoggingEmailSender` rzuca wyjątek na produkcji (retry Hangfire zamiast cichej utraty maila), osobny log, gdy sender nie zwrócił message id, jawny warunek `PendingEmailTokenExpiresAt is null`. Świadomie zaakceptowane: `SaveChanges` + `Enqueue` nie są atomowe (skutek: oczekujący adres bez maila – użytkownik ponawia albo anuluje); `Intl.supportedValuesOf` może podać strefę nieznaną serwerowi – UI pokaże wtedy błąd walidacji.
10. **OpenAPI: `sendHour: number | string`** w `schema.d.ts` – .NET 10 dopuszcza liczby jako stringi w JSON (`JsonNumberHandling` web defaults). Frontend normalizuje `Number(settings.sendHour)`. Poza zakresem tej ścieżki.

## Do zrobienia ręcznie przez użytkownika
- Na Railway dodać zmienną `App__PublicBaseUrl=https://<DOMENA>` (link w mailu potwierdzającym). Do dopisania w `docs/deploy/` przy scalaniu.
- Ręczny test po scaleniu E3B (prawdziwy SendGrid): zmiana adresu w `/settings` → mail → link → „Potwierdź adres”.

## Rejestracje DI
Szczegóły: `docs/di/settings.md`. W skrócie (`SettingsModule`): `AppOptions` (sekcja `App`), `ISettingsService` → `SettingsService` (scoped), `SendEmailConfirmationJob` (scoped), `IEmailSender` → `LoggingEmailSender` przez `TryAddSingleton` (atrapa – zastąpi ją SendGrid z E3B).
