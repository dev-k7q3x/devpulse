# DevPulse – wymagania

## Cel produktu
SaaS dashboard do monitorowania kondycji repozytoriów GitHub.
Problem: developerzy tracą czas, sprawdzając stan projektów w kilku miejscach.
Rozwiązanie: jeden dashboard + codzienny digest email z priorytetami AI.

## Wymagania funkcjonalne
1. Rejestracja/logowanie przez GitHub OAuth (nie chcę osobnych haseł)
2. Podłączenie wielu repozytoriów GitHub per użytkownik
3. Dashboard z kartami per repozytorium:
   - liczba otwartych PR-ów (z linkami)
   - liczba otwartych issue
   - status ostatniego przebiegu CI
   - data ostatniej aktywności
4. Codzienny email (digest) o wybranej godzinie z:
   - podsumowaniem stanu wszystkich repozytoriów
   - priorytetami wygenerowanymi przez AI (Claude) – co wymaga uwagi dziś
5. Historia digestów (ostatnie 30 dni)
6. Ustawienia: adres email, wybór repozytoriów, godzina wysyłki

## Ograniczenia technologiczne (nienegocjowalne)
- Backend: .NET 10 Web API (C#)
- Frontend: React 19 + TypeScript + Vite
- Baza danych: PostgreSQL
- ORM: Entity Framework Core
- AI: Anthropic API (claude-sonnet-5-5)
- Email: SendGrid
- Background jobs: Hangfire (storage w PostgreSQL)
- Deployment: Railway

## Ograniczenia niefunkcjonalne
- GitHub API: max 5000 req/h per token – musimy agresywnie cache'ować
- Czas odpowiedzi dashboardu: < 2 s (dane z cache, nie na żywo)
- Koszt AI per digest: rozsądny – nie generujemy esejów, tylko krótkie priorytety

## Czego NIE robimy (żeby projekt się nie rozrastał)
- Nie obsługujemy GitLaba ani Bitbucketa (tylko GitHub)
- Nie mamy aplikacji mobilnej
- Nie mamy real-time (WebSocket) – odświeżanie co 15 minut wystarczy
- Nie mamy pracy zespołowej – każdy użytkownik widzi tylko swoje repozytoria