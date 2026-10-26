# Runbook – DevPulse na produkcji

Jeden serwis (API + SPA + serwer Hangfire) z `Dockerfile` i jedna baza PostgreSQL. Konfiguracja: [`railway.md`](railway.md).

## Deploy
1. Merge do `main` po zielonym CI (backend, frontend, obraz Docker).
2. Hosting buduje obraz z `Dockerfile`; pre-deploy `./efbundle` aplikuje nowe migracje EF (z blokadą – bezpieczne przy ponowieniu).
3. Start kontenera; healthcheck `GET /health` (liveness, bez bazy). Gotowość: `GET /health/ready` (baza + serwer Hangfire, odpowiedź ≤ 5 s).
4. Po deployu: `/health/ready` = `Healthy`, logowanie działa, dashboard pokazuje karty, w `/hangfire` → Recurring jobs są `snapshot-refresh`, `digest-scheduler` i `digest-retention`.

Migracje są tylko dodawane (nigdy edytowane). Zmiana niekompatybilna wstecz = dwa deploye (najpierw kod obsługujący oba schematy).

## Rollback
- Kod: redeploy poprzedniego udanego deployu w panelu hostingu (obraz jest niezmienny) albo revert commita na `main`.
- Migracje: bundle nie cofa migracji. Przy rollbacku kodu po migracji dodającej kolumny/tabele – stary kod zwykle działa dalej
  (nie zna nowych kolumn). Migracje usuwające dane – tylko po backupie (poniżej).

## Backup bazy
Snapshoty/backupy w panelu bazy hostingu. Przed ryzykowną migracją: ręczny backup (`pg_dump` z publicznego URL-a bazy).
Odtworzenie bazy przywraca też klucze Data Protection (tabela `data_protection_keys`) – potrzebny jest **ten sam certyfikat PFX**.

## Rotacja sekretów
| Sekret | Jak | Skutek |
|---|---|---|
| GitHub OAuth Client secret | GitHub → OAuth App → *Generate a new client secret* → podmień `Auth__GitHub__ClientSecret` → redeploy → usuń stary sekret | Brak – sesje i tokeny działają dalej |
| Certyfikat Data Protection (PFX) | Nowy certyfikat (polecenie poniżej) → podmień `DataProtection__CertificateBase64` i `…Password` → redeploy | **Wszyscy zostają wylogowani, zapisane tokeny GitHub przestają się odszyfrowywać** (`token_invalid`, użytkownik loguje się ponownie – token zostaje nadpisany). Rób tylko przy podejrzeniu wycieku. |
| `ANTHROPIC_API_KEY` | Console Anthropic → nowy klucz → podmień → redeploy → unieważnij stary | Brak; bez klucza digest używa priorytetów regułowych |
| `SendGrid__ApiKey` | SendGrid → API Keys → nowy (uprawnienie *Mail Send*) → podmień → redeploy → usuń stary | Brak; bez klucza maile kończą się błędem `email-not-configured` |

Nowy certyfikat (PowerShell, OpenSSL z Git for Windows):
```powershell
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes -subj "/CN=DevPulse Data Protection" -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dataprotection.pfx -passout pass:<hasło>
[Convert]::ToBase64String([IO.File]::ReadAllBytes("dataprotection.pfx")) | Set-Clipboard   # → DataProtection__CertificateBase64
Remove-Item dp.key
```

## Incydenty
| Objaw | Gdzie patrzeć | Co zrobić |
|---|---|---|
| Kontener nie startuje | Logi deployu – pierwsza linia wyjątku | `Brak konfiguracji GitHub OAuth`, `Brak certyfikatu Data Protection`, `Brak connection stringa` / `ConnectionStrings__Default` → uzupełnij zmienną. |
| `/health/ready` = Unhealthy | Logi: `Health check postgresql/hangfire` | Baza niedostępna albo serwer Hangfire nie wysyła heartbeatu – sprawdź bazę; restart serwisu. |
| Karty dashboardu `Error`/`Stale` | `/hangfire` → Failed jobs (`SnapshotRefreshJob`), kolumna `last_error` w `repository_snapshots` | `github-rate-limited` – poczekaj (budżet 5000/h na użytkownika); `github-token-invalid` – użytkownik musi zalogować się ponownie (baner w UI). |
| Digest nie przyszedł | `/hangfire` → `digest-scheduler`, `SendDigestJob`; tabela `digests` (status, `error`) | `email-not-configured` – brak klucza/nadawcy SendGrid; `email-delivery-failed` – SendGrid odrzucił (sprawdź Activity w SendGrid, weryfikację domeny); brak wiersza – użytkownik bez potwierdzonego emaila, z wyłączonym digestem, `token_invalid` albo bez repo. |
| Ręczny test digestu | `/hangfire` | Ustaw w aplikacji godzinę wysyłki na bieżącą godzinę → w `/hangfire` → Recurring jobs → `digest-scheduler` → *Trigger now*. Digest wychodzi raz na dzień (UNIQUE użytkownik + data). |
| Fala 429 | Logi requestów | Limity w sekcji `RateLimiting` (pamięć jednej instancji – przy skalowaniu w poziomie trzeba magazynu współdzielonego). |
| Wyciek bazy | – | Tokeny GitHub są zaszyfrowane kluczami chronionymi certyfikatem z env – bez env nie do odczytania. Mimo to: rotacja certyfikatu i Client secret, komunikat do użytkowników (unieważnienie tokenów w GitHub → Settings → Applications). |

## Uwagi
- Aplikacja na produkcji działa wyłącznie przez HTTPS za proxy (cookie `Secure`). Żądanie po http wprost na port kontenera
  nie dostaje tokenu XSRF, a przyciski panelu Hangfire (retry, delete) zwracają wtedy błąd – zawsze używaj adresu `https://<domena>`.

## Znane ograniczenia
- Rate limiter i budżet GitHub w pamięci – poprawne przy jednej instancji.
- Błąd jednej paczki GraphQL oznacza błędem wszystkie snapshoty użytkownika w danym przebiegu (kolejny przebieg za 15 min).
- Limit 50 repo sprawdzany nieatomowo (dwa równoległe dodania przy 49 repo mogą dać 51).
