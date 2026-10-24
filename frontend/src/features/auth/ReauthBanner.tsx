import { useLocation } from 'react-router'
import { loginUrl } from './loginUrl'

/** Token GitHub odwołany albo bez wymaganych scopes – dashboard i historia nadal działają (dane z bazy). */
export function ReauthBanner() {
  const location = useLocation()

  return (
    <div className="reauth-banner" role="alert">
      <span>
        Utraciliśmy dostęp do Twojego konta GitHub – dane mogą być nieaktualne.
      </span>
      <a href={loginUrl(location.pathname + location.search)}>
        Zaloguj się ponownie
      </a>
    </div>
  )
}
