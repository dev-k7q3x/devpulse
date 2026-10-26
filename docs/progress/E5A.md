# E5A – Pipeline digestu (M9) · postęp

Gałąź `feat/e5a-digest-pipeline` (od `main` 1bd188e).

## Status
Gotowe do scalenia: review APPROVE (3. runda), pełny `dotnet test` 465/465 (+2 External pominięte), format czysty.

## Dziennik
- 2026-10-26 – start: gałąź od `main`, przegląd kontraktów z E3A/E3B/E3C/E5B (`NextDigestCalculator`, `DigestComposer`, `IAiPrioritizer`, `IDigestEmailRenderer`, `SendGridEmailSender`/`EmailDeliveryException`, `DigestErrorCodes`, `SnapshotRefreshJob`, `RetentionScheduler`).
- 2026-10-26 – kod: `NextDigestCalculator.DueLocalDate`, `DigestPipelineOptions`, `DigestSchedulerJob`, `SendDigestJob`, `DigestScheduler` (hosted service), rejestracje w `DigestModule`; usunięty `LoggingEmailSender` (+ rejestracja w `SettingsModule`, testy w `EmailConfirmationMessageTests`, wpis w `docs/di/settings.md`). Build bez ostrzeżeń.
- 2026-10-26 – testy: Unit `NextDigestCalculatorDueTests` (3 strefy, spóźnione uruchomienie, okno, godzina powtórzona jesienią, luka wiosenna, walidacja okna), `DigestPipelineModuleTests` (rejestracje, jeden `IEmailSender`, opcje, serializacja `DateOnly` w argumentach Hangfire). Integration `DigestSchedulerJobTests` (5) i `SendDigestJobTests` (14). Unit 300/300, E5A Integration 19/19, pełny zestaw 457/457 (pod `.docker-lock`), `dotnet format` czysty.

- 2026-10-26 – review: REQUEST_CHANGES (porzucony `Pending` po restarcie nigdy nieprzejmowany: `InvisibilityTimeout` Hangfire 30 min < próg 1 h). Poprawki: `StalePendingAfter` 20 min, `DigestSchedulerJob` co godzinę ponownie enqueue'uje porzucone `Pending` z ostatniej doby (`AbandonedRecoveryWindow`), timeout HTTP przy składaniu treści → `content-failed`, testy `DigestSchedulerTests` (rejestracja, ponowienie, zatrzymanie) + 3 nowe testy integracyjne. Pełny zestaw 463/463 (pod `.docker-lock`), format czysty.

- 2026-10-26 – ponowne review: REQUEST_CHANGES (zapis `Sent` anulowany przy zatrzymaniu serwera → wiersz `Pending` → odzyskiwanie wysłałoby mail drugi raz; opis ograniczenia nieaktualny). Poprawki: `MarkSent` zapisywany z `CancellationToken.None`, przejęcie ustawia `created_at = now` (zamyka wyścig dwóch przejęć bez migracji), odzyskiwanie pomija użytkowników z wyłączonym digestem / nieważnym tokenem, kod błędu przy anulowaniu zależny od etapu (bez treści → `content-failed`). Testy: anulowanie tuż po wysyłce, druga instancja w trakcie przejętej wysyłki, pomijanie wyłączonych przy odzyskiwaniu. Pełny zestaw 465/465 (pod `.docker-lock`), format czysty.

- 2026-10-26 – trzecie review: **APPROVE**. Wdrożone sugestie: błędy (`FailAsync`) zawsze zapisywane z `CancellationToken.None`, przejęcie czyści `error` poprzedniej próby, poprawiony komentarz o timeoucie, test filtra `TokenInvalid` przy odzyskiwaniu. Pełny zestaw 465/465, format czysty. Frontend nietknięty (brak zmian API – `schema.d.ts` bez regeneracji).

## Pytania i decyzje
1. **Kto dostaje digest:** `digest_enabled`, potwierdzony email, **bez `token_invalid`** i z co najmniej jednym śledzonym repo. Nieważny token → pomijamy (CLAUDE.md: „joby pomijają użytkownika”, UI wymusza ponowne logowanie); bez repo digest nie miałby treści.
2. **Kiedy digest „należy się”:** `NextDigestCalculator.DueLocalDate(now, sendHour, tz, window)` – termin z tej samej reguły co `Next` (zmiana czasu z E3C) wypadł w oknie `(now − 1 h, now]`. Scheduler co godzinę (`0 * * * *`) z oknem 1 h trafia każdy termin dokładnie raz; godzina powtórzona jesienią nie daje drugiego digestu, nieistniejąca wiosną idzie tuż po luce. Okno w `DigestPipeline:SchedulerWindow`. Uruchomienie spóźnione o > 1 h (np. aplikacja leżała) pomija digest tego dnia – świadomie, zamiast wysyłać o nieoczekiwanej porze.
3. **Zapytania schedulera:** jedno zapytanie o kandydatów (projekcja) + jedno o istniejące digesty dla wyliczonych par (user, data) – bez N+1. Nieznana strefa (`TryFindSystemTimeZoneById`) → log i pominięcie, scheduler działa dalej.
4. **Idempotencja i ponowienia `SendDigestJob`:** insert `Pending`; unique violation (`ux_digests_user_id_digest_date`, sprawdzana nazwa constraintu) → atomowe przejęcie `ExecuteUpdate … WHERE status = Failed OR (status = Pending AND created_at < now − StalePendingAfter)`. `Sent` albo świeży `Pending` (inna instancja wysyła) → koniec bez wysyłki.
   - **Porzucony `Pending`** (restart w trakcie wysyłki): `StalePendingAfter` = **20 min** – krócej niż `InvisibilityTimeout` Hangfire.PostgreSql (30 min), więc job oddany przez Hangfire po restarcie widzi wiersz jako porzucony i go przejmuje. Zabezpieczenie dodatkowe: `DigestSchedulerJob` co godzinę ponownie enqueue'uje porzucone `Pending` z ostatniej doby (`AbandonedRecoveryWindow`).
   - **Wyścig dwóch przejęć** (np. Hangfire oddaje przerwany job w tej samej chwili, w której scheduler odzyskuje porzucony wiersz): przejęcie ustawia `created_at = now` w tym samym `UPDATE`, a PostgreSQL po zdobyciu blokady wiersza sprawdza warunek ponownie – druga instancja widzi już „świeży” `Pending` i kończy bez wysyłki (test `Should_NotTakeOverAgain_When_SecondInstanceRunsDuringTakenOverSend`). Bez nowej kolumny: `created_at` czyta tylko `RetentionJob`, przesunięcie o najwyżej dobę nie ma znaczenia. Pełniejsze rozwiązanie (`claimed_at`) wymagałoby migracji – niepotrzebne.
   - **Po udanej wysyłce** status `Sent` jest zapisywany z `CancellationToken.None` – zatrzymanie serwera tuż po przyjęciu maila przez SendGrid nie zostawia `Pending`, więc odzyskiwanie nie wyśle drugi raz.
   - **Pozostałe ryzyka (świadomie przyjęte):** crash podczas wywołania AI (przed zapisem treści) → każde odzyskanie woła AI ponownie (rzadkie, koszt ułamek centa); `Pending` porzucony u użytkownika, który potem wyłączył digest, zostaje w historii jako „w trakcie” do retencji (30 dni).
5. **Błędy (`digests.error` = wyłącznie `DigestErrorCodes`):**
   - przejściowe (`EmailDeliveryException.IsTransient` 429/5xx, błąd sieci, timeout HTTP, zatrzymanie serwera) → `Failed` + `email-delivery-failed` i **rzucenie wyjątku** → `[AutomaticRetry(Attempts = 3)]` ponawia na tym samym wierszu;
   - trwałe: 4xx od SendGrid → `email-delivery-failed`, `InvalidOperationException` z sendera (brak klucza/nadawcy) → `email-not-configured`, brak potwierdzonego emaila → `no-recipient`, wyjątek przy składaniu/renderze (także timeout HTTP przy odświeżaniu snapshotów, gdy job nie jest anulowany) → `content-failed` – bez wyjątku, więc bez ponowień.
6. **AI najwyżej raz:** treść (temat, priorytety, stan repo, HTML) zapisywana przed wysyłką; ponowienie renderuje ją z zapisanych danych (render deterministyczny), bez drugiego wywołania Anthropic. Brak repo ze snapshotem → AI nie jest wołane.
7. **Snapshoty przed digestem:** jeśli którekolwiek śledzone repo nie ma snapshotu świeższego niż `Snapshot:RefreshInterval` → `SnapshotRefreshJob.RefreshUserAsync` (logika E3A, błędy GitHuba zapisuje w snapshotach i nie rzuca). Repo bez żadnego snapshotu nie trafiają do digestu (nie twierdzimy „0 PR-ów” bez danych).
8. **`OldestOpenPrCreatedAt`** liczone z listy PR w snapshocie (maks. 10 najnowszych – E3A) – przy > 10 otwartych PR reguła „stare PR-y” może nie zobaczyć najstarszego. Do ewentualnego doprecyzowania w GraphQL (E3A), bez zmian w tej ścieżce.
9. **Usunięty `LoggingEmailSender`:** po E3B jedyną rejestracją `IEmailSender` jest `SendGridEmailSender`. Konsekwencja: lokalnie bez `SendGrid__ApiKey`/`SendGrid:FromEmail` mail potwierdzający adres i digest kończą się błędem konfiguracji (digest: `email-not-configured`) zamiast cichego logu.
10. **Bez endpointu „wyślij teraz”** – ręczne uruchomienie przez panel `/hangfire` (etap 6).
11. **Bez sekcji `DigestPipeline` w `appsettings.json`** – wartości domyślne w kodzie (jak sekcja `GitHub` w E3A); nadpisywalne zmiennymi środowiskowymi.

## Do zrobienia ręcznie przez użytkownika
- Klucze: `ANTHROPIC_API_KEY`, `SendGrid__ApiKey`, `SendGrid__FromEmail` (zweryfikowana domena nadawcy) – lokalnie user-secrets, na produkcji env.
- Raz, za zgodą (płatne): `DEVPULSE_RUN_EXTERNAL=1 dotnet test ./tests/DevPulse.Tests --filter Category=External` (kontrakt Anthropic i SendGrid, `SendGrid:TestRecipient`).
- Po etapie 6: w `/hangfire` ręczne uruchomienie `SendDigestJob` dla własnego konta → mail w skrzynce, wiersz w `digests`, podgląd w historii. Następnego dnia digest o ustawionej godzinie.

## Rejestracje DI
`DigestModule` (szczegóły: `docs/di/digest.md`, sekcja „E5A – pipeline digestu”):
- `IOptions<DigestPipelineOptions>` ← sekcja `DigestPipeline` (`SchedulerWindow` 1 h, `StalePendingAfter` 20 min, `AbandonedRecoveryWindow` 1 doba),
- `DigestSchedulerJob` (transient), `SendDigestJob` (transient, zależy od `SnapshotRefreshJob` z `GitHubModule`),
- `DigestScheduler` (hosted service – rejestracja recurring `digest-scheduler`, cron `0 * * * *`, ponawiana co 30 s).
`SettingsModule`: usunięta rejestracja `LoggingEmailSender`.
