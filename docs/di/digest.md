# M7–M8 Digest – rejestracje DI i konfiguracja (E3B)

Wszystko w `Infrastructure/DependencyInjection/DigestModule.cs` (`AddDigest`, wołane przez `AddInfrastructure`). `Program.cs` bez zmian.

| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IOptions<DigestRulesOptions>` ← sekcja `Digest` | – | Progi fallbacku: `StalePullRequestAfter` (7 dni), `InactiveAfter` (30 dni). Domyślne w kodzie, sekcji w appsettings brak – dodaj, jeśli trzeba zmienić. |
| `IDigestComposer` → `Application/Digests/DigestComposer` | Singleton | Czysta logika, bez I/O. |
| `IDigestEmailRenderer` → `Infrastructure/Email/DigestEmailRenderer` | Singleton | Interfejs w `Application/Digests` (używa go `SendDigestJob` w E5A). |
| `IOptions<AnthropicOptions>` ← sekcja `Anthropic` + `ApiKey` z `ANTHROPIC_API_KEY` | – | Klucz tylko ze zmiennej środowiskowej / user-secrets pod tą nazwą. |
| `IAiPrioritizer` → `Infrastructure/Ai/AnthropicPrioritizer` | Singleton | Własny `HttpClient` (`SocketsHttpHandler`, `PooledConnectionLifetime` 15 min). Timeout przez `TimeProvider` (domyślnie 20 s). Wymaga `TimeProvider` z Api. |
| `IOptions<SendGridOptions>` ← sekcja `SendGrid` | – | `ApiKey` tylko z env/user-secrets (`SendGrid__ApiKey`). |
| `IEmailSender` → `Infrastructure/Email/SendGridEmailSender` | Singleton | Jedyna rejestracja `IEmailSender` (atrapa `LoggingEmailSender` z E3C usunięta w E5A). |

## E5A – pipeline digestu
| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IOptions<DigestPipelineOptions>` ← sekcja `DigestPipeline` | – | `SchedulerWindow` (1 h), `StalePendingAfter` (20 min – poniżej `InvisibilityTimeout` Hangfire 30 min), `AbandonedRecoveryWindow` (1 doba); wartości domyślne w kodzie, sekcja opcjonalna. |
| `DigestSchedulerJob` | Transient | Recurring `digest-scheduler`, cron `0 * * * *`, bez ponowień; ponownie enqueue'uje porzucone `Pending`. |
| `SendDigestJob` | Transient | Enqueue'owany przez scheduler; `[AutomaticRetry(Attempts = 3)]` – ponowienia tylko przy przejściowych błędach wysyłki. Korzysta z `SnapshotRefreshJob` (GitHubModule). |
| `DigestScheduler` | HostedService | Rejestruje recurring job w tle (ponawia co 30 s, aplikacja wstaje bez bazy). |

## Konfiguracja (appsettings.json)
```json
"Anthropic": { "Model": "claude-sonnet-5-5", "MaxTokens": 400, "Effort": "low", "Timeout": "00:00:20" },
"SendGrid": { "FromEmail": "", "FromName": "DevPulse", "SandboxMode": false }
```
Sekrety: `ANTHROPIC_API_KEY`, `SendGrid__ApiKey` (Railway env / user-secrets). `SendGrid:FromEmail` musi należeć do domeny zweryfikowanej w SendGrid.

## Po scaleniu E3A
E3A może dodać `Microsoft.Extensions.Http` (typed HttpClient dla GitHuba). Wtedy oba klienty z tego modułu można przepiąć na
`services.AddHttpClient<IAiPrioritizer, AnthropicPrioritizer>()` – konstruktory już przyjmują `HttpClient`.
