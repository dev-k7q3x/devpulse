import { Navigate, useSearchParams } from 'react-router'
import { loginUrl, toLocalPath } from './loginUrl'
import { useCurrentUser } from './useCurrentUser'

const ERROR_MESSAGES: Record<string, string> = {
  access_denied:
    'Logowanie zostało anulowane na GitHubie. DevPulse potrzebuje zgody, żeby czytać stan Twoich repozytoriów.',
  login_failed: 'Nie udało się zalogować przez GitHub. Spróbuj ponownie.',
}

export function LoginPage() {
  const [searchParams] = useSearchParams()
  const { data: user } = useCurrentUser()
  const returnUrl = toLocalPath(searchParams.get('returnUrl'))
  const error = searchParams.get('error')
  const errorMessage = error ? ERROR_MESSAGES[error] : undefined

  if (user) {
    return <Navigate to={returnUrl} replace />
  }

  return (
    <main className="login-page">
      <h1>Zaloguj się</h1>
      <p>
        DevPulse używa konta GitHub – nie zakładasz osobnego hasła. Poprosimy o
        dostęp do profilu, adresu email i repozytoriów.
      </p>
      {errorMessage && <p role="alert">{errorMessage}</p>}
      <a className="button" href={loginUrl(returnUrl)}>
        Zaloguj przez GitHub
      </a>
    </main>
  )
}
