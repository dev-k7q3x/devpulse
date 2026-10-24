# Etap 2 – kroki do wykonania ręcznie (zamknięcie etapu)

Kod etapu 2 jest w `main`. Zostały kroki, które wymagają Twoich kont (GitHub, Railway) i nie da się ich zrobić z kodu.
Rób je **w tej kolejności** – domena z Railway jest potrzebna do produkcyjnej aplikacji OAuth.

Czas: ok. 30–45 min. Odniesienie techniczne: [`railway.md`](railway.md).

---

## Krok 1. Projekt na Railway (ok. 10 min)

1. Wejdź na **https://railway.com** → **Login** → **Login with GitHub** (konto, które ma dostęp do organizacji `dev-k7q3x`).
2. Plan: do działania jednego serwisu + bazy wystarczy **Hobby** (ok. 5 USD/mies., rozliczane za zużycie). Trial też zadziała na start, ale ma limity.
3. **New Project** → **Deploy from GitHub repo**.
   - Jeśli repo `dev-k7q3x/devpulse` nie jest widoczne: **Configure GitHub App** → wybierz organizację `dev-k7q3x` → **Only select repositories** → `devpulse` → **Save**. Wróć do Railway i odśwież listę.
   - Wybierz `dev-k7q3x/devpulse` → **Deploy Now**.
4. Railway sam znajdzie w repo `railway.json` (build z `Dockerfile`, pre-deploy `./efbundle`, healthcheck `/health`).
   **Pierwszy deploy się wyłoży – to oczekiwane** (brak zmiennych środowiskowych, aplikacja celowo nie startuje bez konfiguracji). Naprawisz to w kroku 3.
5. Dodaj bazę: w widoku projektu **+ Create** (albo **New**) → **Database** → **Add PostgreSQL**.
   - Zostaw domyślną nazwę serwisu **`Postgres`** – zmienne w kroku 3 się do niej odwołują. Jeśli nazwa jest inna, podmień `Postgres` w `${{Postgres.…}}` na swoją.
6. Kliknij serwis aplikacji (`devpulse`) → **Settings**:
   - **Source → Branch**: `main` (auto-deploy po każdym merge do `main`).
   - Opcjonalnie **Wait for CI**: włącz – deploy ruszy dopiero, gdy GitHub Actions będzie zielone.
   - **Networking → Public Networking → Generate Domain**. Jeśli zapyta o port: **8080**.
   - Skopiuj domenę, np. `devpulse-production-xxxx.up.railway.app`. Dalej oznaczam ją jako **`<DOMENA>`**.

---

## Krok 2. Aplikacje GitHub OAuth (ok. 10 min)

Potrzebne są **dwie** aplikacje – OAuth App ma tylko jeden callback URL (osobno produkcja, osobno lokalny development).

### 2a. Produkcja
1. Wejdź na **https://github.com/settings/developers** → zakładka **OAuth Apps** → **New OAuth App**
   (dla aplikacji należącej do organizacji: **https://github.com/organizations/dev-k7q3x/settings/applications** → **New OAuth App** – obie opcje działają).
2. Wypełnij:
   | Pole | Wartość |
   |---|---|
   | Application name | `DevPulse` |
   | Homepage URL | `https://<DOMENA>` |
   | Application description | opcjonalnie: `Dashboard kondycji repozytoriów GitHub` |
   | Authorization callback URL | `https://<DOMENA>/signin-github` |
   | Enable Device Flow | **nie zaznaczaj** |
3. **Register application**.
4. Na stronie aplikacji skopiuj **Client ID**.
5. **Generate a new client secret** → skopiuj sekret **od razu** (GitHub pokaże go tylko raz). Zapisz oba w menedżerze haseł jako „DevPulse OAuth – prod”.

### 2b. Development (lokalnie)
Powtórz 2a z innymi wartościami:
| Pole | Wartość |
|---|---|
| Application name | `DevPulse (dev)` |
| Homepage URL | `http://localhost:5173` |
| Authorization callback URL | `http://localhost:5173/signin-github` |

Zapisz Client ID i sekret jako „DevPulse OAuth – dev”.

---

## Krok 3. Zmienne środowiskowe na Railway (ok. 5 min)

1. Railway → serwis aplikacji → zakładka **Variables** → **Raw Editor**.
2. Wklej poniższy blok i podmień wartości w `<…>`:
   ```
   ConnectionStrings__Default=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
   Auth__GitHub__ClientId=<Client ID z kroku 2a>
   Auth__GitHub__ClientSecret=<Client secret z kroku 2a>
   DataProtection__CertificateBase64=<zawartość pliku dataprotection-base64.txt>
   DataProtection__CertificatePassword=<zawartość pliku dataprotection-password.txt>
   Admin__Logins__0=<Twój login GitHub, np. pawellukasiewiczkontakt-creator>
   ```
   - Pierwszą linię wklej **dokładnie tak** – `${{Postgres.PGHOST}}` itd. to referencje Railway, same podstawią dane bazy.
   - **Nie** dodawaj `PORT` ani `DATABASE_URL` – Railway ustawia `PORT` sam, a aplikacja wymaga formatu Npgsql, nie URI.
3. Wartości certyfikatu są w katalogu `C:\Users\pawel\.devpulse\`. Najwygodniej skopiować je do schowka w PowerShell (bez wyświetlania na ekranie):
   ```powershell
   Get-Content -Raw "$env:USERPROFILE\.devpulse\dataprotection-base64.txt" | Set-Clipboard
   # wklej w Railway jako DataProtection__CertificateBase64, potem:
   Get-Content -Raw "$env:USERPROFILE\.devpulse\dataprotection-password.txt" | Set-Clipboard
   # wklej jako DataProtection__CertificatePassword
   ```
4. **Update Variables** → Railway zaproponuje redeploy → **Deploy**.
5. Obserwuj **Deployments** → najnowszy deploy → **View logs**:
   - **Build** – kilka minut (frontend, .NET, migration bundle).
   - **Pre-deploy** – w logach `Applying migration '20261023195103_InitialCreate'` i `Done.` (przy kolejnych deployach: `No migrations were applied`).
   - **Deploy** – healthcheck `/health` → status **Active / Success**.
   - Jeśli kontener się wyłoży, pierwsza linia błędu powie, czego brakuje (`Brak konfiguracji GitHub OAuth…`, `Brak certyfikatu Data Protection…`, `Brak zmiennej środowiskowej ConnectionStrings__Default…`).

---

## Krok 4. Weryfikacja na produkcji (ok. 10 min) – kryteria zakończenia etapu

Odhacz każdy punkt. W przeglądarce użyj normalnego okna (nie trybu prywatnego – tam sesja nie przetrwa restartu przeglądarki, co myli test).

- [ ] **4.1** `https://<DOMENA>/health` → `Healthy`; `https://<DOMENA>/health/ready` → `Healthy` (może potrzebować ok. 30 s po starcie).
- [ ] **4.2** `https://<DOMENA>/` → przekierowanie na `/login` z przyciskiem **Zaloguj przez GitHub**.
- [ ] **4.3** Kliknij przycisk → ekran zgody GitHuba wymienia dostęp do profilu, adresów email i **repozytoriów (publicznych i prywatnych)**.
- [ ] **4.4** **Cancel** na ekranie zgody → wracasz na `/login` z komunikatem „Logowanie zostało anulowane na GitHubie…”.
- [ ] **4.5** Zaloguj się ponownie → **Authorize** → wracasz do aplikacji, w nagłówku widać Twój login i avatar, nie ma żółtego banera „Zaloguj się ponownie”.
- [ ] **4.6** Otwórz `https://<DOMENA>/api/auth/me` → JSON z Twoim loginem, `"githubTokenValid": true`, `"missingScopes": []`, `"isAdmin": true`.
- [ ] **4.7** **Restart bez utraty sesji** (najważniejszy test etapu): Railway → serwis → **Deployments** → menu ⋮ przy aktywnym deployu → **Restart**. Poczekaj na status Active, odśwież `https://<DOMENA>/` → **nadal jesteś zalogowany** (bez przejścia przez GitHub).
- [ ] **4.8** **Wyloguj** w nagłówku → ekran logowania; `https://<DOMENA>/api/auth/me` → `{"type":"unauthenticated",…}`.
- [ ] **4.9** Railway → **Observability / Logs** serwisu: brak wpisów `[ERR]` i `[FTL]`; w liniach `HTTP GET /api/auth/me responded …` nie ma błędów 500.
- [ ] **4.10** (ForwardedHeaders) W logach Railway (zakładka **HTTP Logs** albo logi deployu) przy Twoich żądaniach sprawdź, czy widoczny adres klienta to Twoje publiczne IP (sprawdzisz je np. na https://ifconfig.me), a nie adres wewnętrzny `10.x`/`100.x`. Jeśli nie da się tego odczytać – pomiń i napisz mi o tym.

---

## Krok 5. Lokalny development z logowaniem (ok. 5 min)

1. W katalogu repo (PowerShell):
   ```powershell
   git checkout main; git pull
   dotnet user-secrets set "Auth:GitHub:ClientId" "<Client ID z kroku 2b>" --project ./src/DevPulse.Api
   dotnet user-secrets set "Auth:GitHub:ClientSecret" "<Client secret z kroku 2b>" --project ./src/DevPulse.Api
   ```
2. Uruchom:
   ```powershell
   docker compose up -d
   dotnet run --project ./src/DevPulse.Api          # terminal 1 (port 5080)
   cd frontend; npm run dev                        # terminal 2 (port 5173)
   ```
3. Wejdź na **http://localhost:5173** (nie 5080) → **Zaloguj przez GitHub** → po zgodzie wracasz zalogowany.
   Lokalna baza ma już migrację `InitialCreate`.

---

## Krok 6. Certyfikat do menedżera haseł (2 min)

Katalog `C:\Users\pawel\.devpulse\` zawiera `dataprotection.pfx`, `dataprotection-password.txt`, `dataprotection-base64.txt` i `dp.crt` (publiczny certyfikat).
1. Dodaj do menedżera haseł wpis „DevPulse – certyfikat Data Protection” z załącznikiem `dataprotection.pfx` i hasłem z `dataprotection-password.txt`.
2. Po sprawdzeniu, że wpis jest kompletny, możesz usunąć katalog `.devpulse` z dysku (kopia w Railway + menedżerze wystarczy).

Utrata certyfikatu nie niszczy danych, ale unieważnia sesje i zaszyfrowane tokeny – wszyscy muszą zalogować się ponownie.

---

## Krok 7. Daj mi znać

Odpisz krótko, np.:
```
Etap 2: 4.1–4.10 OK
(albo: 4.7 FAIL – po restarcie wylogowało; w logach: <wklej linie z ERR>)
Domena: https://<DOMENA>
```
Nie wklejaj sekretów (Client secret, hasło certyfikatu, base64).

Jeśli wszystko jest OK, etap 2 jest zamknięty i ruszamy z etapem 3 (plan 04): trzy ścieżki równoległe w osobnych worktree –
**E3A** GitHub + repozytoria + synchronizacja (ścieżka krytyczna), **E3B** digest: composer, AI, email, **E3C** ustawienia.
Przed E3B dobrze jest już teraz założyć konto SendGrid i zacząć weryfikację domeny nadawcy (trwa do kilku dni) – szczegóły podam przy starcie etapu 3.
