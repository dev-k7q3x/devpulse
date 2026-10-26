# DevPulse – wszystkie kroki ręczne (instrukcja końcowa)

Kod wszystkich etapów (1–6) jest w `main` i przechodzi testy oraz CI. Zostało to, czego nie da się zrobić z kodu:
konta zewnętrzne, klucze, hosting i sprawdzenie na żywo. Rób części **w tej kolejności**; każda kończy się listą do odhaczenia.

| Część | Co | Czas | Wymaga |
|---|---|---|---|
| A | Lokalnie: logowanie, repozytoria, dashboard | 15 min | konto GitHub |
| B | SendGrid i Anthropic (maile i priorytety AI) | 20 min + weryfikacja nadawcy | konta SendGrid / Anthropic |
| C | Lokalnie: zmiana emaila, digest, historia | 15 min | A + B |
| D | Hosting (Railway) i zmienne | 30 min | konto Railway (rejestracje mogą być nadal wstrzymane) |
| E | Weryfikacja produkcji (MVP 1 + MVP 2) | 20 min + następny dzień | D |
| F | Porządki i zgłoszenie wyniku | 5 min | – |

Sekrety (Client secret, klucze API, hasło certyfikatu) zapisuj w menedżerze haseł i **nigdy** nie wklejaj ich do czatu ani do repo.

---

## A. Lokalnie – logowanie, repozytoria, dashboard

### A1. Deweloperska GitHub OAuth App
1. https://github.com/settings/developers → **OAuth Apps** → **New OAuth App**.
2. Pola:
   | Pole | Wartość |
   |---|---|
   | Application name | `DevPulse (dev)` |
   | Homepage URL | `http://localhost:5173` |
   | Authorization callback URL | `http://localhost:5173/signin-github` |
   | Enable Device Flow | nie zaznaczaj |
3. **Register application** → skopiuj **Client ID** → **Generate a new client secret** → skopiuj sekret od razu (pokazywany raz).

> Port ma znaczenie: aplikację otwieraj zawsze na **http://localhost:5173** (Vite), a callback ustaw na `:5173`.
> Jeśli w OAuth App jest już `http://localhost:5080/signin-github` (stara instrukcja), też zadziała – API po zalogowaniu
> przekieruje Cię na :5173 (sesja jest wspólna dla portów localhost) – ale wtedy logowanie zaczynaj z http://localhost:5080/api/auth/login,
> bo GitHub wymaga zgodności `redirect_uri` z callbackiem. Najprościej: zmień callback na `:5173`.

### A2. Konfiguracja i uruchomienie (PowerShell, katalog repo)
```powershell
git checkout main; git pull
docker compose up -d
dotnet tool restore
dotnet ef database update --project ./src/DevPulse.Infrastructure --startup-project ./src/DevPulse.Api
dotnet user-secrets set "Auth:GitHub:ClientId" "<Client ID z A1>" --project ./src/DevPulse.Api
dotnet user-secrets set "Auth:GitHub:ClientSecret" "<Client secret z A1>" --project ./src/DevPulse.Api
dotnet user-secrets set "Admin:Logins:0" "<Twój login GitHub>" --project ./src/DevPulse.Api
```
Uruchom w dwóch terminalach:
```powershell
dotnet run --project ./src/DevPulse.Api        # terminal 1 – API na :5080
cd frontend; npm ci; npm run dev               # terminal 2 – SPA na :5173
```

### A3. Sprawdź (wejdź na http://localhost:5173 – nie :5080)
Błąd `redirect_uri is not associated with this application` na GitHubie = port w callbacku OAuth App różni się od portu, na którym zaczęło się logowanie (patrz uwaga w A1).
- [ ] **A3.1** Przekierowanie na `/login` → **Zaloguj przez GitHub** → ekran zgody (profil, email, repozytoria) → **Authorize** → wracasz zalogowany (login i avatar w nagłówku, link **Hangfire**).
- [ ] **A3.2** Pusty dashboard → wyszukaj i **dodaj 2–3 repo**. Karty pokazują „Pobieramy dane…”, po kilkunastu sekundach liczby PR/issue, status CI i datę aktywności.
- [ ] **A3.3** **Ustawienia** → lista śledzonych repo; usuń jedno → znika z dashboardu.
- [ ] **A3.4** **Hangfire** (link w nagłówku) → panel jobów; w *Recurring jobs* są `snapshot-refresh`, `digest-scheduler`, `digest-retention`.
- [ ] **A3.5** **Wyloguj** → ekran logowania.

---

## B. SendGrid i Anthropic

### B1. SendGrid (wysyłka maili)
1. https://signup.sendgrid.com → konto (plan Free wystarcza: 100 maili/dzień).
2. **Nadawca** – jedna z opcji:
   - **Single Sender Verification** (szybko, bez domeny): *Settings → Sender Authentication → Verify a Single Sender* → podaj swój adres → kliknij link w mailu. Maile mogą częściej trafiać do spamu.
   - **Domain Authentication** (zalecane na produkcję, wymaga własnej domeny): *Settings → Sender Authentication → Authenticate Your Domain* → dodaj wskazane rekordy CNAME u rejestratora domeny → **Verify** (propagacja DNS: od minut do 48 h).
3. *Settings → API Keys → Create API Key* → **Restricted Access** → tylko **Mail Send: Full Access** → skopiuj klucz (pokazywany raz).
4. Adres nadawcy (`FromEmail`) musi być zweryfikowanym adresem albo adresem w zweryfikowanej domenie.

### B2. Anthropic (priorytety AI)
1. https://console.anthropic.com → konto → **Billing** (doładowanie, np. 5 USD wystarcza na bardzo długo – digest kosztuje ułamki centa).
2. Zalecane: **Limits → Spend limit** (np. 5 USD/mies.).
3. **API Keys → Create Key** (np. `devpulse-dev`) → skopiuj. Na produkcję utwórz osobny klucz (`devpulse-prod`).

### B3. Klucze lokalnie
```powershell
dotnet user-secrets set "SendGrid:ApiKey" "<klucz z B1>" --project ./src/DevPulse.Api
dotnet user-secrets set "SendGrid:FromEmail" "<zweryfikowany adres nadawcy>" --project ./src/DevPulse.Api
dotnet user-secrets set "ANTHROPIC_API_KEY" "<klucz z B2>" --project ./src/DevPulse.Api
```
Zrestartuj API (terminal 1).

### B4. (Opcjonalnie, płatne – ułamki centa) Test kontraktowy prawdziwych API
Potwierdza, że Anthropic i SendGrid przyjmują dokładnie te zapytania, które wysyła aplikacja (SendGrid w trybie sandbox – mail nie wychodzi).
```powershell
dotnet user-secrets set "SendGrid:TestRecipient" "<Twój adres>" --project ./src/DevPulse.Api
$env:DEVPULSE_RUN_EXTERNAL = "1"; dotnet test ./tests/DevPulse.Tests --filter Category=External; Remove-Item Env:DEVPULSE_RUN_EXTERNAL
```
- [ ] **B4.1** Wynik: 2 testy zielone.

---

## C. Lokalnie – zmiana emaila, digest, historia

- [ ] **C1** **Ustawienia → Email** → wpisz inny swój adres → **Zapisz** → mail „Potwierdź adres” w skrzynce (sprawdź spam) → link otwiera `/confirm-email` → **Potwierdź adres** → komunikat sukcesu, w ustawieniach nowy adres.
- [ ] **C2** **Ustawienia → Harmonogram** → godzina wysyłki = **bieżąca godzina** (np. teraz 14:20 → 14), strefa `Europe/Warsaw`, digest włączony → **Zapisz**.
- [ ] **C3** **Hangfire** → *Recurring jobs* → zaznacz `digest-scheduler` → **Trigger now** → po chwili w *Jobs → Succeeded* jest `SendDigestJob`.
- [ ] **C4** Mail z digestem w skrzynce: podsumowanie repo + priorytety (źródło: AI).
- [ ] **C5** **Historia digestów** → wpis z dzisiejszą datą, status „Wysłany”, AI → szczegóły → podgląd maila wygląda jak w skrzynce.
- [ ] **C6** Ponowne **Trigger now** tego samego dnia **nie** wysyła drugiego maila (jeden digest dziennie).

Jeśli mail nie przychodzi: *Hangfire → Failed jobs* i kolumna „błąd” w historii (`email-not-configured` = brak klucza/nadawcy, `email-delivery-failed` = SendGrid odrzucił – sprawdź *Activity* w SendGrid i weryfikację nadawcy).

---

## D. Hosting (Railway)

> Stan na 2026-10-24: Railway wstrzymał rejestrację nowych kont po incydencie bezpieczeństwa (https://status.railway.com).
> Jeśli rejestracja nadal nie działa – napisz mi; przygotuję wariant na innym hostingu (np. Render: Docker + zarządzany PostgreSQL,
> zmiana dotyczy tylko pliku konfiguracji deployu i dokumentacji). Railway jest w wymaganiach jako „nienegocjowalny”,
> więc zmiana to Twoja decyzja.

### D1. Projekt i baza
1. https://railway.com → **Login with GitHub** → plan **Hobby**.
2. **New Project → Deploy from GitHub repo** → `dev-k7q3x/devpulse` (jeśli niewidoczne: *Configure GitHub App* → organizacja `dev-k7q3x` → repo `devpulse`).
   Pierwszy deploy się wyłoży (brak zmiennych) – to oczekiwane.
3. **+ Create → Database → Add PostgreSQL** (nazwa serwisu `Postgres`).
4. Serwis aplikacji → **Settings**: *Source → Branch* `main`; opcjonalnie *Wait for CI*; *Networking → Generate Domain* (port **8080**) → skopiuj domenę = **`<DOMENA>`**.

### D2. Produkcyjna GitHub OAuth App
Jak A1, ale: name `DevPulse`, Homepage `https://<DOMENA>`, callback `https://<DOMENA>/signin-github`. Skopiuj Client ID i sekret.

### D3. Zmienne (serwis aplikacji → **Variables → Raw Editor**)
```
ConnectionStrings__Default=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
Auth__GitHub__ClientId=<Client ID z D2>
Auth__GitHub__ClientSecret=<Client secret z D2>
DataProtection__CertificateBase64=<zawartość dataprotection-base64.txt>
DataProtection__CertificatePassword=<zawartość dataprotection-password.txt>
Admin__Logins__0=<Twój login GitHub>
App__PublicBaseUrl=https://<DOMENA>
SendGrid__ApiKey=<klucz SendGrid>
SendGrid__FromEmail=<zweryfikowany adres nadawcy>
ANTHROPIC_API_KEY=<klucz devpulse-prod>
```
- Pierwszą linię wklej dokładnie tak (referencje Railway do bazy). Nie dodawaj `PORT` ani `DATABASE_URL`.
- Certyfikat jest w `C:\Users\pawel\.devpulse\`. Kopiowanie do schowka bez wyświetlania:
  ```powershell
  Get-Content -Raw "$env:USERPROFILE\.devpulse\dataprotection-base64.txt" | Set-Clipboard
  Get-Content -Raw "$env:USERPROFILE\.devpulse\dataprotection-password.txt" | Set-Clipboard
  ```
- **Update Variables → Deploy**. W logach: build → pre-deploy `Applying migration '…InitialCreate'` → `Done.` → status **Active**.
  Błąd startu podaje brakującą zmienną w pierwszej linii (`Brak konfiguracji GitHub OAuth…`, `Brak certyfikatu Data Protection…`, `Brak zmiennej środowiskowej ConnectionStrings__Default…`).

---

## E. Weryfikacja produkcji

**MVP 1**
- [ ] **E1** `https://<DOMENA>/health` i `/health/ready` → `Healthy`.
- [ ] **E2** Logowanie GitHubem → powrót zalogowany; **Cancel** na ekranie zgody → `/login` z komunikatem o anulowaniu.
- [ ] **E3** `https://<DOMENA>/api/auth/me` → `"githubTokenValid": true`, `"missingScopes": []`, `"isAdmin": true`.
- [ ] **E4** Dodaj 3 repo → karty „Pobieramy dane…” → po chwili prawdziwe liczby.
- [ ] **E5** **Restart bez utraty sesji:** Railway → *Deployments* → ⋮ → **Restart** → odśwież stronę → nadal zalogowany.
- [ ] **E6** Logi: brak `[ERR]`/`[FTL]`; przy Twoich żądaniach widoczne Twoje publiczne IP (https://ifconfig.me), nie adres `10.x`/`100.x`.

**MVP 2**
- [ ] **E7** Zmiana emaila z potwierdzeniem (jak C1) – link w mailu prowadzi na `https://<DOMENA>/confirm-email…`.
- [ ] **E8** Ręczny digest (jak C2–C5) przez `https://<DOMENA>/hangfire`.
- [ ] **E9** Ustaw godzinę wysyłki na jutrzejszy poranek → **następnego dnia** digest przychodzi o tej godzinie (±kilka minut) i jest w historii.
- [ ] **E10** Zwykły użytkownik (inne konto GitHub) na `/hangfire` → 403.

---

## F. Porządki i zgłoszenie

1. Certyfikat z `C:\Users\pawel\.devpulse\` (`dataprotection.pfx` + hasło) → menedżer haseł; potem możesz usunąć katalog.
2. Napisz mi wynik w tej formie (bez sekretów):
   ```
   A: OK | B: OK (nadawca: single sender / domena) | C: OK
   D/E: E1–E10 OK  (albo: E5 FAIL – <linie z ERR z logów>)
   Domena: https://<DOMENA>
   ```
   Jeśli coś nie działa – wklej linie błędu z logów albo kod błędu z historii digestów; poprawię i wypchnę zmianę.
