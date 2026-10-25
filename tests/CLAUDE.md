# DevPulse – konwencje testowe

Ładowane automatycznie przy pracy w tests/. Ogólne zasady projektu: ../CLAUDE.md

## Stack
xUnit + FluentAssertions + Moq; integracyjne: Testcontainers PostgreSQL + WebApplicationFactory.
Czas: FakeTimeProvider (Microsoft.Extensions.TimeProvider.Testing).

## Struktura
```
tests/DevPulse.Tests/
  Builders/      ← buildery encji (UserBuilder, TrackedRepositoryBuilder, DigestBuilder, …)
  Unit/          ← Application + Infrastructure, bez I/O, [Trait("Category", "Unit")]
  Integration/   ← Testcontainers + WebApplicationFactory, [Trait("Category", "Integration")]
  Fixtures/      ← nagrane odpowiedzi GitHuba / Anthropic (JSON)
```

## Zasady
- Nazwy: Should_[Oczekiwanie]_When_[Warunek]
- Struktura: // Arrange / // Act / // Assert z komentarzami
- Encje testowe wyłącznie przez buildery z Builders/ – nie przez `new Encja { … }` w teście
- Minimum: happy path + jeden przypadek brzegowy na metodę publiczną
- Każda klasa testowa ma Category; hook Stop uruchamia tylko `--filter Category=Unit`
- Testy z prawdziwym Anthropic/SendGrid tylko jako [Trait("Category", "External")] – uruchamiają się wyłącznie
  ze zmienną `DEVPULSE_RUN_EXTERNAL=1` (inaczej są pomijane) (klucze z user-secrets,
  SendGrid w sandbox mode) – nigdy w CI, uruchamiane tylko na wyraźne polecenie

## Unit
- HTTP do GitHuba/Anthropic: fake HttpMessageHandler + fixture JSON, nie mockowanie HttpClient
- Logika digestu (Application/Digest) testowana bez mocków I/O
- Czas zawsze przez FakeTimeProvider – nigdy DateTime.UtcNow w teście

## Integration
- Każdy test działa na migracji InitialCreate w świeżym kontenerze – nie zakładaj danych z innych testów
- Endpointy: przypadki błędów z kontraktu (plan 03) – 401 (nie 302), 404 dla cudzego zasobu, 409, 422, 429
- Izolacja użytkowników: zawsze test, że użytkownik B nie widzi danych użytkownika A
- Dashboard: fake IGitHubClient rzuca przy każdym wywołaniu (dowód, że dashboard nie woła GitHuba)
- POST/PUT/DELETE wymagają nagłówka X-XSRF-TOKEN – helper klienta testowego go dokłada

## Subagent
devpulse-kit:test-writer pisze testy po dodaniu klasy publicznej w Application/Infrastructure.
Po jego pracy przejrzyj, czy testy sprawdzają zachowanie, a nie implementację.
