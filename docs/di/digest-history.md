# M10 Historia digestów – rejestracje DI

Moduł: `Infrastructure/DependencyInjection/DigestHistoryModule.cs` (`AddDigestHistory`, wołane przez `AddInfrastructure`). `Program.cs` bez zmian.

| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IOptions<DigestHistoryOptions>` (sekcja `Digests`) | – | `RetentionPeriod` (domyślnie `30.00:00:00`, pełne doby) – okno listy i próg retencji. |
| `IDigestService` → `Services/DigestService` | Scoped | Lista (projekcja bez `html_body`), szczegóły, HTML. Filtr jawnie po `userId` i po oknie w strefie użytkownika. |
| `RetentionJob` | Transient | Hangfire, `IDbContextFactory`, `ExecuteDeleteAsync` po `created_at`. |
| `RetentionScheduler` | Hosted service | Rejestruje recurring `digest-retention` (`0 3 * * *`, 03:00 UTC) w tle, ponawia co 30 s, aplikacja wstaje bez bazy. |

Kontroler: `Api/Controllers/DigestsController.cs` – nagłówki `/html`: CSP `sandbox; default-src 'none'; img-src https: data:; style-src 'unsafe-inline'`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400, immutable` (ustawiane tylko przy 200).
