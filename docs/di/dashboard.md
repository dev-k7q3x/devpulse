# M5 Dashboard – rejestracje DI

## Infrastructure – `DependencyInjection/DashboardModule.cs` (`AddDashboard`, wołane przez `AddInfrastructure`)
| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `IDashboardService` → `Services/DashboardService` | Scoped | `IDevPulseDbContext` (scoped), `IOptions<SnapshotOptions>` (`StaleAfter`), `TimeProvider`. Nigdy nie woła `IGitHubClient`. |

`SnapshotOptions` binduje `GitHubModule` (sekcja `Snapshot`) – moduł dashboardu jej nie rejestruje drugi raz.

## Api
`Controllers/DashboardController` – bez nowych rejestracji. Nagłówki `ETag` i `Cache-Control: private, no-cache`, `If-None-Match` → 304.
`Program.cs` bez zmian.
