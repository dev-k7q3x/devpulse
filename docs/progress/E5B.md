# E5B – Historia digestów (M10) · postęp

Gałąź `feat/e5b-digest-history` · worktree `.claude/worktrees/agent-a6851393e198ff852`

## Status
Gotowe do scalenia: pełny `dotnet test` 418/418 (+2 External pominięte), frontend 55/55, review APPROVE.

## Dziennik
- 2026-10-26 – start: gałąź od `main` (51351ce), przeczytany kontrakt (plan 03 §5), `IDigestService`, DTO, encja `Digest`, `DigestBuilder`, wzorzec `SnapshotRefreshScheduler` (E3A).
- 2026-10-26 – backend: `DigestService`, `DigestsController`, `RetentionJob` + `RetentionScheduler`, `DigestHistoryOptions` (sekcja `Digests`), `Application/DigestHistory` (`DigestHistoryRules`, `DigestErrorCodes`), `DigestHistoryModule`. Build bez ostrzeżeń. Testy Unit `DigestHistoryRulesTests`, `DigestHistoryModuleTests` – 15/15.
- 2026-10-26 – testy Integration napisane (`DigestsEndpointsTests`, `RetentionJobTests`); **Docker Desktop nie odpowiada** (`npipe dockerDesktopLinuxEngine` nie istnieje) – zgodnie z poleceniem nie restartuję.
- 2026-10-26 – frontend: `DigestHistoryPage` (lista, pusty stan z linkiem do ustawień, błąd, status, źródło priorytetów AI/regułowe), `DigestDetailPage` (priorytety, metadane, bezpieczny błąd, podgląd wyłącznie w `<iframe sandbox="">`), `useDigests`, `digestLabels`, lokalny `digests.css`, trasy `digests` i `digests/:id`, `queryKeys.digests`, zregenerowany `schema.d.ts`. Vitest 55/55, lint, type-check, prettier, build zielone. `dotnet format` czysty, testy Unit 289/289.
- 2026-10-26 – `/devpulse-kit:devpulse-review`: **APPROVE**. Sugestia 2 (rzekomo puste `Error` w teście Failed) nietrafna – `DigestBuilder.Failed()` zapisuje „SendGrid 503”, test sprawdza ukrycie surowego tekstu. Odłożone: test `RetentionScheduler` (spójnie z nietestowanym `SnapshotRefreshScheduler`), „30 dni” w napisach UI na sztywno (zgodne z domyślną konfiguracją), użycie `queryKeys.digests.all` przy przyszłej invalidacji.
- 2026-10-26 – Docker wrócił (restart w sesji głównej). Pod blokadą `.docker-lock`: jeden czerwony test – asercja `Cache-Control` porównywała string, a HttpClient serializuje dyrektywy we własnej kolejności; poprawione na sprawdzanie dyrektyw. **Pełny `dotnet test`: 418/418** (+2 External pominięte).

## Pytania i decyzje
- **Okno historii stosowane do listy, szczegółów i `/html`** (nie tylko listy) – digest starszy niż 30 dni jest niewidoczny także między przebiegami `RetentionJob`; szczegóły/HTML spoza okna → 404. Okno liczone w bieżącej strefie użytkownika (`user_settings.time_zone_id`, nieznana → UTC), jak w kontrakcie (`digest_date >= today_local - 30`).
- **Retencja po `created_at`, okno listy po `digest_date`** – jak w planach 02/03. Próg w konfiguracji: `Digests:RetentionPeriod` (pełne doby, domyślnie 30), `DigestHistoryOptions` w `Application/Options`.
- **Pole `error` w szczegółach:** pokazywane tylko dla `Failed` i tylko jako opis znanego kodu z nowego `Application/DigestHistory/DigestErrorCodes` (`email-delivery-failed`, `email-not-configured`, `no-recipient`, `content-failed`); każda inna treść (np. surowy komunikat wyjątku) → ogólne „Nie udało się wysłać digestu.” **E5A: `SendDigestJob` powinien zapisywać w `digests.error` wyłącznie te kody.**
- **`RetentionJob`:** cron `0 3 * * *` (03:00 UTC) jako stała – to nie próg biznesowy; `AutomaticRetry(3)`, `DisableConcurrentExecution`. Scheduler skopiowany ze wzorca `SnapshotRefreshScheduler` (E3A) – do ewentualnego ujednolicenia we wspólny rejestrator recurring jobów (poza zakresem ścieżki, plik E3A).
- **Nagłówki `/html` ustawiane tylko przy 200** – 404 nie dostaje `immutable`.
- **Komponenty UI lokalnie w `features/digests/`** (komunikaty, `digests.css`) – `components/` należy do E4; do ujednolicenia po scaleniu. `app/routes.test.tsx` bez zmian (strona nie wywraca się na nietypowej odpowiedzi atrapy).
- **`formatDigestDate` bez konwersji stref** – `digestDate` to data lokalna, nie moment.

## Do zrobienia ręcznie przez użytkownika
- Brak kroków ręcznych specyficznych dla E5B. Po wdrożeniu (z E5A): obejrzeć historię i podgląd prawdziwego digestu w przeglądarce.

## Rejestracje DI
Zob. `docs/di/digest-history.md`: `DigestHistoryOptions` (sekcja `Digests`), `IDigestService` → `DigestService` (Scoped), `RetentionJob` (Transient), `RetentionScheduler` (hosted service).
