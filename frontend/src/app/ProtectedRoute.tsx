import { Navigate, Outlet, useLocation } from 'react-router'
import {
  isUnauthenticated,
  useCurrentUser,
} from '@/features/auth/useCurrentUser'

/** Strony wymagające sesji. Brak sesji (401) → /login z powrotem na bieżącą stronę. */
export function ProtectedRoute() {
  const { data: user, error, isPending } = useCurrentUser()
  const location = useLocation()

  if (isUnauthenticated(error)) {
    const current = location.pathname + location.search
    const target =
      current === '/'
        ? '/login'
        : `/login?returnUrl=${encodeURIComponent(current)}`
    return <Navigate to={target} replace />
  }

  if (isPending) {
    return <p role="status">Ładowanie…</p>
  }

  if (!user) {
    return <p role="alert">Nie udało się wczytać konta. Odśwież stronę.</p>
  }

  return <Outlet />
}
