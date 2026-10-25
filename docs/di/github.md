# M2–M4 GitHub, repozytoria, synchronizacja – rejestracje DI

Wszystko w `Infrastructure/DependencyInjection/GitHubModule.cs` (`AddGitHub`, wołane przez `AddInfrastructure`).
**Bez zmian w `Program.cs`.**

| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IOptions<RepositoriesOptions>`, `IOptions<SnapshotOptions>` | – | Bez zmian (już były). |
| `IOptions<GitHubOptions>` ← sekcja `GitHub` | – | Domyślne wartości w kodzie: `ApiBaseUrl`, `RequestTimeout` 15 s, `RateLimitReserve` 500, `AvailableCacheDuration` 10 min, `MaxRepositoryPages` 50, `SnapshotBatchSize` 20. Sekcji nie ma w `appsettings.json` – nadpisanie przez env, np. `GitHub__RateLimitReserve`. |
| `AddMemoryCache()` | Singleton | Cache listy `available` (per userId) i stron REST z ETag. |
| `GitHubRateLimitBudget` | Singleton | Ostatni stan `x-ratelimit-*` per (userId, zasób). W pamięci procesu – jedna instancja. |
| `IGitHubTokenStore` → `GitHub/GitHubTokenStore` | Singleton | Token z bazy przez `IDbContextFactory` + `ITokenProtector`; po 401 `User.MarkTokenInvalid`. |
| `IGitHubClient` → `GitHub/GitHubClient` | Singleton (fabryka) | `HttpClient` tworzony ręcznie (bez `IHttpClientFactory` – brak `Microsoft.Extensions.Http`): `GitHubRateLimitHandler` → `SocketsHttpHandler` (`PooledConnectionLifetime` 5 min). Nagłówki: `User-Agent: DevPulse`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. |
| `IRepositoryService` → `Services/RepositoryService` | Scoped | Używa `IDevPulseDbContext`, `IGitHubClient`, `IMemoryCache`, `IBackgroundJobClient`. |
| `SnapshotRefreshJob` | Transient | Job Hangfire (`RefreshAllAsync`, `RefreshUserAsync`, `RefreshRepositoryAsync`), `IDbContextFactory`, bez HttpContext. |
| `SnapshotRefreshScheduler` (hosted) | Singleton | `BackgroundService`: `AddOrUpdate("snapshot-refresh", RefreshAllAsync, cron z Snapshot:RefreshInterval)`; ponawia co 30 s, gdy storage Hangfire nie odpowiada. |

## Api (bez nowych rejestracji)
- `Controllers/RepositoriesController` – wykrywany automatycznie.
- `RateLimiting/RateLimitingServiceCollectionExtensions` – polityka `github-read` wybiera partycję po `refresh`:
  `refresh=true` → `github-refresh:{user}` (2/min), inaczej `github-read:{user}` (20/min).
