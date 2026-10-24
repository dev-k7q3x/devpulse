# M1 Auth – rejestracje DI i konfiguracja

Stan po implementacji etapu 2 (gałąź `feat/auth-module`). Zmiany w `Program.cs` zostały wprowadzone za zgodą
(sesja interaktywna, nie worktree). Ten plik opisuje, co i gdzie jest zarejestrowane.

## Infrastructure – `DependencyInjection/SecurityModule.cs` (`AddSecurity`, wołane przez `AddInfrastructure`)
| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `AddDataProtection().SetApplicationName("DevPulse").PersistKeysToDbContext<DevPulseDbContext>()` | – | Klucze w `data_protection_keys`, stała nazwa aplikacji. |
| `.ProtectKeysWithCertificate(cert).UnprotectKeysWithAnyCertificate(cert)` | – | Gdy ustawione `DataProtection:CertificateBase64`. Bez certyfikatu i z `RequireCertificate = true` → wyjątek przy starcie. |
| `ITokenProtector` → `Security/TokenProtector` | Singleton | Purpose `DevPulse.GitHubAccessToken.v1`. |
| `IAuthService` → `Services/AuthService` | Scoped | Upsert użytkownika, `/me`, usunięcie konta. |
| `DevPulseDbContextDesignTimeFactory` | – | Poza DI – używają go tylko `dotnet ef` i migration bundle. |
| `IOptions<AdminOptions>` | – | Bez zmian (już było). |

## Api – `Auth/AuthServiceCollectionExtensions.cs` (`AddDevPulseAuthentication`)
| Rejestracja | Czas życia | Uwagi |
|---|---|---|
| `AddHttpContextAccessor()` | Singleton | Dla `CurrentUser`. |
| `ICurrentUser` → `Auth/CurrentUser` | Scoped | Claim `devpulse:user_id`. Tylko żądania HTTP – nie joby. |
| `AddAuthentication(Cookies).AddCookie(...)` | – | `devpulse_session`, HttpOnly, SameSite=Lax, `SameAsRequest`, 14 dni przesuwane. `OnRedirectToLogin` → 401 `unauthenticated` dla `/api/*`, `OnRedirectToAccessDenied` → 403 `forbidden`. |
| `.AddGitHub(...)` | – | Scopes z `GitHubScopes.Required`, callback `/signin-github`, claim `urn:github:avatar_url`, cookie korelacji SameSite=Lax. `OnCreatingTicket` → `IAuthService.SignInWithGitHubAsync` + principal z samym id DevPulse. `OnAccessDenied` → `/login?error=access_denied`, `OnRemoteFailure` → `/login?error=login_failed`. |
| `AddAuthorizationBuilder().SetFallbackPolicy(RequireAuthenticatedUser)` | – | Wszystko wymaga sesji poza `[AllowAnonymous]`. |
| `AddAntiforgery(HeaderName = "X-XSRF-TOKEN", Cookie = devpulse_antiforgery)` | – | Cookie `XSRF-TOKEN` (czytelne dla JS) wydaje `GET /api/auth/me`. |
| `AntiforgeryValidationFilter` | Scoped | Globalny filtr MVC (POST/PUT/PATCH/DELETE) → 400 `validation`. |

## `Program.cs` – kolejność middleware
```
UseForwardedHeaders → UseExceptionHandler → UseStatusCodePages → UseSerilogRequestLogging → (MapOpenApi)
→ UseDefaultFiles/UseStaticFiles → UseRouting → UseAuthentication → UseAuthorization → UseRateLimiter → MapControllers
```
- `ForwardedHeadersOptions`: `XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, wyczyszczone `KnownIPNetworks`/`KnownProxies` (adresy proxy Railway nie są stałe).
- Filtry MVC: `AntiforgeryValidationFilter`, potem `ValidationFilter`.
- `AddOpenApi` z `CamelCaseQueryParametersTransformer` (parametry query w camelCase jak w kontrakcie).
- `.AllowAnonymous()` na `MapOpenApi`, fallbacku `/api/{**path}` i `MapFallbackToFile` (SPA i `/login` bez sesji).

## Konfiguracja
| Klucz | Gdzie | Uwagi |
|---|---|---|
| `Auth:GitHub:ClientId`, `Auth:GitHub:ClientSecret` | user-secrets lokalnie, `Auth__GitHub__ClientId` / `Auth__GitHub__ClientSecret` na Railway | Wymagane – brak zatrzymuje start (fail fast). |
| `Auth:SessionLifetime` | `appsettings.json` (`14.00:00:00`) | |
| `DataProtection:CertificateBase64`, `DataProtection:CertificatePassword` | Railway env | PFX w base64. |
| `DataProtection:RequireCertificate` | `true` w `appsettings.json`, `false` w `appsettings.Development.json` | |

Lokalnie: `dotnet user-secrets set "Auth:GitHub:ClientId" "<id>" --project ./src/DevPulse.Api` (projekt ma już `UserSecretsId`).
