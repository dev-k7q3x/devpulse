# M6 Ustawienia – rejestracje DI i konfiguracja

Ścieżka E3C (`feat/e3c-settings`). Bez zmian w `Program.cs` – wszystko w `Infrastructure/DependencyInjection/SettingsModule.cs`.

| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IOptions<AppOptions>` ← sekcja `App` | – | `PublicBaseUrl` – adres SPA do linków w mailach. |
| `ISettingsService` → `Services/SettingsService` | Scoped | `GET/PUT /api/settings`, zmiana/anulowanie/potwierdzenie emaila. |
| `SendEmailConfirmationJob` | Scoped | Job Hangfire (enqueue z `SettingsService`). Aktywator Hangfire tworzy scope per job. |
| `IEmailSender` → `Email/LoggingEmailSender` | Singleton, **TryAdd** | Atrapa do czasu scalenia E3B. `SendGridEmailSender` rejestrowany zwykłym `Add` w `DigestModule` wygrywa (ostatnia rejestracja). Po scaleniu E3B można usunąć `LoggingEmailSender`. |

Brak recurring jobów – mail potwierdzający jest jednorazowym `Enqueue`.

## Konfiguracja
| Klucz | Gdzie | Uwagi |
|---|---|---|
| `App:PublicBaseUrl` | `appsettings.json` (`http://localhost:5173`), na Railway `App__PublicBaseUrl=https://<domena>` | **Wymagane na produkcji** – inaczej link w mailu prowadzi na localhost. |

## Używane istniejące elementy
- Polityki rate limitera `email` (`POST /api/settings/email`) i `anonymous` (`POST /api/settings/email/confirm`).
- `IDataProtectionProvider` (z `SecurityModule`) – purpose `DevPulse.EmailConfirmationToken.v1` szyfruje token w argumentach joba Hangfire.
- Indeks `ux_user_settings_pending_email_token_hash` (InitialCreate) – wyszukiwanie przy potwierdzeniu.
