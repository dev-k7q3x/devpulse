# Deploy na Render (alternatywa dla Railway)

Główna ścieżka to Railway ([`railway.md`](railway.md), [`kroki-reczne.md`](kroki-reczne.md) część D).
Railway od 22 października 2026 (po incydencie bezpieczeństwa z 21 października) wstrzymał rejestrację nowych kont.
Jeśli nie masz konta na Railway, ten sam obraz z `Dockerfile` postawisz na Render. Konfiguracja jest w `render.yaml` (Blueprint).

Plan Free wystarcza, żeby sprawdzić, czy wszystko działa: koszt 0 zł. Ograniczenia:
- usługa zasypia po 15 min bez ruchu, pierwsze żądanie po przerwie budzi ją przez ok. minutę – w tym czasie nie działają też joby Hangfire,
- instancja ma 512 MB RAM i 0,1 CPU, więc start trwa dłużej niż na Railway,
- darmowy PostgreSQL ma 1 GB i **wygasa po 30 dniach** (potem 14 dni na przejście na płatny plan, inaczej baza jest usuwana),
- brak pre-deploy command – migracje uruchamia `ENTRYPOINT` przy starcie (`MIGRATE_ON_START=true`).

Na stałe: plan Starter usługi (pre-deploy command działa – patrz niżej) i płatny plan bazy. Sprawdź aktualny cennik na render.com/pricing.

## Różnice względem Railway

| | Railway | Render Free |
|---|---|---|
| Konfiguracja w repo | `railway.json` | `render.yaml` |
| Migracje | pre-deploy `./efbundle` | `./efbundle` w `ENTRYPOINT` przy `MIGRATE_ON_START=true` |
| Connection string | referencje `${{Postgres.PGHOST}}` … | wpisany ręcznie z panelu bazy |
| Domena | `<nazwa>.up.railway.app` | `<nazwa>.onrender.com` |
| Restart | Deployments → ⋮ → Restart | Manual Deploy → Restart service |

Bundle przy starcie jest bezpieczny, bo migracje EF biorą blokadę bazy, a na planie Free działa jedna instancja.
Jeśli migracja się nie uda, kontener kończy pracę przed startem aplikacji (`./efbundle || exit 1`) i deploy kończy się błędem w logach.

## 1. Blueprint
1. https://render.com → **Sign in with GitHub**.
2. **New → Blueprint** → repo `dev-k7q3x/devpulse` → Render odczyta `render.yaml` i pokaże usługę `devpulse` i bazę `devpulse-db`.
3. Render poprosi o wartości zmiennych z `sync: false`. Na tym etapie wpisz tylko te, które już masz (OAuth, certyfikat, SendGrid).
   `ConnectionStrings__Default` i `App__PublicBaseUrl` uzupełnisz w kroku 3, gdy baza i domena już istnieją. `ANTHROPIC_API_KEY` możesz zostawić pusty.
4. **Apply**. Pierwszy deploy się wyłoży (brak connection stringa) – to oczekiwane.

## 2. Produkcyjna GitHub OAuth App
Jak w `kroki-reczne.md` (A1), ale: Homepage `https://<nazwa>.onrender.com`, callback `https://<nazwa>.onrender.com/signin-github`.
Domenę widać na górze strony usługi `devpulse`.

## 3. Zmienne (usługa `devpulse` → **Environment**)
```
ConnectionStrings__Default=Host=<Hostname>;Port=5432;Database=devpulse;Username=devpulse;Password=<Password>
App__PublicBaseUrl=https://<nazwa>.onrender.com
```
- `Hostname` i `Password` są w bazie `devpulse-db` → **Info → Connections**. Użyj **internal** hostname (usługa i baza są w tym samym regionie).
- Nie wklejaj *Internal Database URL* (`postgresql://…`) – Npgsql potrzebuje formatu `Host=…;Port=…`.
- `PORT` ustawia Render – nie dodawaj go. `MIGRATE_ON_START` jest już w `render.yaml`.
- Reszta zmiennych jak w `railway.md` (sekcja 3).

**Save Changes** → Render zrobi nowy deploy. W logach: `Applying migration '…InitialCreate'` → `Done.` → start aplikacji → status **Live**.

## 4. Weryfikacja
Lista z `kroki-reczne.md`, część E, z dwiema zmianami:
- **E5 Restart:** usługa → **Manual Deploy → Restart service** → odśwież stronę → nadal zalogowany.
- **E6 IP klienta:** jeśli w logach requestów widać adres proxy zamiast Twojego IP, Render dodaje więcej niż jeden wpis w `X-Forwarded-For`.
  Wtedy podnieś `ForwardLimit` w `Program.cs` (konfiguracja `ForwardedHeadersOptions`). Railway działa z `ForwardLimit = 1`.
- **E9** (digest następnego dnia) na planie Free zadziała tylko wtedy, gdy usługa nie śpi o godzinie wysyłki – odwiedź stronę kilka minut wcześniej.

## 5. Plan Starter zamiast Free (opcjonalnie)
W `render.yaml` zmień `plan: free` na `plan: starter`, usuń `MIGRATE_ON_START` i dodaj `preDeployCommand: ./efbundle`.
Wtedy migracje działają jak na Railway: osobny krok przed startem nowej wersji.

## 6. Porządki
Po teście: usługa i baza → **Settings → Delete**. Płatne plany są naliczane, dopóki usługa istnieje.
