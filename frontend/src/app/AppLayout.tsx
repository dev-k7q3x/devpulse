import { NavLink, Outlet } from 'react-router'
import { ReauthBanner } from '@/features/auth/ReauthBanner'
import {
  needsReauthentication,
  useCurrentUser,
} from '@/features/auth/useCurrentUser'
import { useLogout } from '@/features/auth/useLogout'

export function AppLayout() {
  const { data: user } = useCurrentUser()
  const logout = useLogout()

  return (
    <div className="app-layout">
      <header className="app-header">
        <span className="app-brand">DevPulse</span>
        <nav aria-label="Główna nawigacja">
          <NavLink to="/" end>
            Dashboard
          </NavLink>
          <NavLink to="/digests">Historia digestów</NavLink>
          <NavLink to="/settings">Ustawienia</NavLink>
          {/* Panel serwera, nie trasa SPA – pełna nawigacja przeglądarki. */}
          {user?.isAdmin && <a href="/hangfire">Hangfire</a>}
        </nav>
        {user && (
          <div className="app-user">
            {user.avatarUrl && (
              <img src={user.avatarUrl} alt="" width={24} height={24} />
            )}
            <span>{user.login}</span>
            <button
              type="button"
              onClick={() => logout.mutate()}
              disabled={logout.isPending}
            >
              Wyloguj
            </button>
          </div>
        )}
      </header>
      {user && needsReauthentication(user) && <ReauthBanner />}
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
