# E3B – Klocki digestu: composer, AI, email

## Status
**Gotowe do scalenia.** Review: APPROVE. Pełny `dotnet test`: 262/262 (External pominięte: 2), build i format czyste. Bez commitu.

## Dziennik
- 2026-10-24 – start: gałąź `feat/e3b-digest-blocks` od `main` (b6686ba), przegląd modeli `Application/Digests` oraz interfejsów `IDigestComposer`, `IAiPrioritizer` i `IEmailSender`.
- 2026-10-24 – kod: `DigestComposer` (fallback regułowy), `PolishText`, `IDigestEmailRenderer` + `DigestEmailRenderer`, `AnthropicPrioritizer`, `SendGridEmailSender`, `DigestModule`, sekcje `Anthropic`/`SendGrid` w appsettings.json. `dotnet build`: 0 ostrzeżeń.
- 2026-10-24 – testy Unit: `DigestComposerTests`, `PolishTextTests`, `AnthropicPrioritizerTests` (fixture'y w `Fixtures/Anthropic`), `SendGridEmailSenderTests`, `DigestEmailRendererTests` (snapshot HTML i tekstu w `Fixtures/SendGrid`), `DigestModuleTests`. Test kontraktowy `External/DigestProvidersContractTests` (`Category=External`, nieuruchamiany). Wynik: **Unit 202/202**, `dotnet format --verify-no-changes` czysty (src i tests).
- 2026-10-24 – próba pełnego `dotnet test`: wszystkie testy Integration padają na `DockerUnavailableException`, bo daemon Dockera nie działa (`npipe://./pipe/dockerDesktopLinuxEngine` nie istnieje). Z izolowanego worktree nie mogę uruchomić Docker Desktop. E3B nie zmienia kodu używanego przez testy integracyjne.
- 2026-10-24 – `/devpulse-kit:devpulse-review`: REQUEST_CHANGES. Jedyny punkt blokujący: testy External uruchamiały się przy pełnym `dotnet test` bez filtra.
- 2026-10-25 – poprawki po review:
  - `ExternalFactAttribute`: testy External są pomijane bez `DEVPULSE_RUN_EXTERNAL=1`;
  - odpowiedź 4xx Anthropic (oprócz 429) logowana jako `Error` – to błąd konfiguracji;
  - AI daje najwyżej jeden priorytet na repo (`DistinctBy`, plus test);
  - usunięte nieużywane `PolishText.Repositories`.

  Unit 203/203. Docker działa ponownie (sesja główna). Pełny `dotnet test` pod blokadą `.docker-lock`: **262/262, External pominięte: 2**.

## Pytania i decyzje
1. **Anthropic i SendGrid przez HttpClient, bez SDK.** Decyzja sesji głównej: bez nowych paczek NuGet. Testy używają fake `HttpMessageHandler` z fixture'ami. Skill `claude-api` domyślnie zaleca oficjalne SDK, więc tę rozbieżność zgłaszam. Przejście na `Anthropic` SDK wymaga zgody na paczkę.
2. **Bez `IHttpClientFactory`.** `Microsoft.Extensions.Http` nie jest dostępne w Infrastructure, a dodanie paczki lub `FrameworkReference` mogłoby kolidować z csproj E3A. Zamiast tego: singleton `HttpClient` z `SocketsHttpHandler.PooledConnectionLifetime = 15 min`, czyli oficjalnie zalecana alternatywa. Po scaleniu E3A można przejść na `AddHttpClient` (docs/di/digest.md).
3. **Kształt requestu Messages API** (ze skilla `claude-api`):
   - nagłówki `x-api-key` i `anthropic-version: 2026-10-21`;
   - `thinking: {type: "between_tools"}`, bo `disabled` daje 400 na Claude Sonnet 5.5, a adaptive myślenie zjadałoby limit 400 tokenów;
   - `output_config: {effort: "low", format: {type: "json_schema", schema}}`;
   - bez `temperature` (400 w wersji API 2026-10-21).
4. **Bez server-side `fallbacks`** (skill zaleca je dla Sonnet 5.5). Odmowa (`stop_reason: refusal`) kończy się naszym fallbackiem regułowym. Fallback na inny model zwiększałby koszt AI, a to według CLAUDE.md wymaga zgody.
5. **Walidacja odpowiedzi AI:**
   - repo musi być na liście wejściowej (nazwa normalizowana do oryginału);
   - tekst w jednej linii, maks. 300 znaków;
   - severity tylko High, Medium albo Low;
   - maks. 5 pozycji.
   Same nieprawidłowe pozycje oznaczają fallback. Pusta lista od modelu to poprawny wynik („nic nie wymaga uwagi”). Brak repo albo brak klucza oznacza brak wywołania (null, czyli fallback).
6. **Reguły fallbacku:**
   - jedno repo daje najwyżej jeden priorytet, z najważniejszej reguły;
   - kolejność: failing CI (wyżej repo z większą liczbą otwartych PR-ów), potem stare PR-y (najstarsze pierwsze, próg 7 dni), potem brak aktywności (próg 30 dni);
   - brak daty aktywności to brak danych, a nie brak aktywności.
   Progi są w `DigestRulesOptions` (sekcja `Digest`, bez wpisu w appsettings, bo sekcja jest spoza mojego zakresu edycji).
7. **Interfejs renderera** (`IDigestEmailRenderer`, `DigestEmailModel`, `RenderedDigestEmail`) leży w `Application/Digests`, a nie w `Application/Interfaces`, bo to mój zakres edycji. Można go przenieść przy scalaniu.
8. **Zmiana w `IDigestComposer`: brak.** Sygnatura z E1 wystarczyła.
9. **`tests/DevPulse.Tests.csproj`:** glob fixture'ów rozszerzony z `*.json` na `*.json;*.html;*.txt` (snapshot maila). Możliwy konflikt z E3A, jeśli też zmieni tę linię – przy scalaniu zostawić szerszy glob.
10. **Nowy builder** `Builders/RepositoryDigestStateBuilder.cs`. Istniejących builderów nie zmieniałem.
11. **Uwagi z review do E5A (nie blokują merge):**
    - `SendGridEmailSender` bez konfiguracji rzuca `InvalidOperationException`. `SendDigestJob` powinien traktować ten wyjątek jak nietrwały błąd po stronie konfiguracji (bez sensu ponawiać), tak jak `EmailDeliveryException.IsTransient == false`.
    - Pusty `SendGrid:FromEmail` na produkcji trzeba dopisać do checklisty pre-deploy.
    - Przed E5A raz puścić test External, żeby potwierdzić, że prawdziwe API akceptuje kształt requestu.
    - `tests/CLAUDE.md` warto uzupełnić o `DEVPULSE_RUN_EXTERNAL=1`. Ten plik jest poza moim zakresem edycji.
    - `DigestPriority` i `DigestRepositorySummary` w testach renderera są tworzone przez `new`. To wartości z jsonb z publicznym konstruktorem, więc nie dodawałem builderów.
12. **Treść maila po polsku.** Priorytety AI też po polsku (system prompt). `WebUtility.HtmlEncode` zamienia znaki Latin-1 (np. ó) na encje, co jest poprawne przy `charset=utf-8`.

## Do zrobienia ręcznie przez użytkownika
- **Anthropic:** klucz API w `ANTHROPIC_API_KEY`. Lokalnie: `dotnet user-secrets set "ANTHROPIC_API_KEY" "<klucz>" --project ./src/DevPulse.Api`.
- **SendGrid:**
  - konto i weryfikacja domeny nadawcy (Settings → Sender Authentication → Authenticate Your Domain; DNS, trwa do kilku dni);
  - klucz API z uprawnieniem „Mail Send”: `SendGrid__ApiKey`;
  - adres nadawcy w zweryfikowanej domenie: `SendGrid__FromEmail`.
- **Test kontraktowy** (opcjonalnie, płatny, tylko na wyraźne polecenie): `DEVPULSE_RUN_EXTERNAL=1 dotnet test ./tests/DevPulse.Tests --filter Category=External`. Wymaga dodatkowo `SendGrid:TestRecipient`. SendGrid działa w trybie sandbox, więc mail nie wychodzi.

## Rejestracje DI
Szczegóły w `docs/di/digest.md`. W skrócie (`DigestModule`):
- `DigestRulesOptions`;
- `IDigestComposer` → `DigestComposer` (singleton);
- `IDigestEmailRenderer` → `DigestEmailRenderer` (singleton);
- `AnthropicOptions` z `ANTHROPIC_API_KEY`;
- `IAiPrioritizer` → `AnthropicPrioritizer` (singleton, własny HttpClient);
- `SendGridOptions`;
- `IEmailSender` → `SendGridEmailSender` (singleton, zwykłe `Add`).
