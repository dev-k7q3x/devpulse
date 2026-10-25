import { Link, useSearchParams } from 'react-router'
import { useCurrentUser } from '@/features/auth/useCurrentUser'
import { useConfirmEmail } from './useSettings'

/**
 * Strona z linku w mailu. Samo otwarcie (GET) niczego nie potwierdza – skanery linków w skrzynkach
 * mogłyby to zrobić bez użytkownika. Potwierdza dopiero POST po kliknięciu przycisku.
 * Działa bez logowania; `GET /api/auth/me` wydaje cookie XSRF-TOKEN potrzebne do POST.
 */
export function ConfirmEmailPage() {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')
  const session = useCurrentUser()
  const confirm = useConfirmEmail()

  // Token antiforgery jest gotowy, gdy /me odpowiedziało (także 401 bez sesji).
  const xsrfReady = !session.isPending

  return (
    <main className="login-page">
      <h1>Potwierdzenie adresu email</h1>
      {!token ? (
        <p role="alert">Link jest niepełny – brakuje tokenu.</p>
      ) : confirm.isSuccess ? (
        <>
          <p role="status">
            Adres <strong>{confirm.data.email}</strong> został potwierdzony. Na
            ten adres będzie wysyłany digest.
          </p>
          <Link to="/settings">Przejdź do ustawień</Link>
        </>
      ) : (
        <>
          <p>Kliknij przycisk, aby potwierdzić nowy adres digestu.</p>
          {confirm.isError && (
            <p role="alert">
              Link jest nieprawidłowy, wygasł albo został już użyty. Rozpocznij
              zmianę adresu ponownie w ustawieniach.
            </p>
          )}
          <button
            type="button"
            className="button"
            disabled={!xsrfReady || confirm.isPending}
            onClick={() => confirm.mutate({ token })}
          >
            Potwierdź adres
          </button>
        </>
      )}
    </main>
  )
}
