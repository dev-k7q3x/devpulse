import { useQuery } from '@tanstack/react-query'
import { httpClient, ProblemError } from '@/api/httpClient'
import { queryKeys } from '@/api/queryKeys'
import type { components } from '@/api/schema'

export type CurrentUser = components['schemas']['CurrentUserResponse']

/**
 * `unauthenticated` – brak sesji albo sesja wygasła. Rozpoznawane po `type`, nie po statusie:
 * 401 `github-token-invalid` oznacza baner „Zaloguj się ponownie”, a nie wylogowanie.
 */
export function isUnauthenticated(error: unknown): boolean {
  return (
    error instanceof ProblemError && error.problem.type === 'unauthenticated'
  )
}

/** Zalogowany użytkownik (`GET /api/auth/me`). Wywołanie wydaje też cookie XSRF-TOKEN dla mutacji. */
export function useCurrentUser() {
  return useQuery({
    queryKey: queryKeys.auth.me,
    queryFn: () => httpClient.get<CurrentUser>('/api/auth/me'),
    staleTime: 5 * 60 * 1000,
    retry: (failureCount, error) =>
      !isUnauthenticated(error) && failureCount < 1,
  })
}

/** Wymaga ponownego logowania: token odwołany na GitHubie albo brakuje scopes. */
export function needsReauthentication(user: CurrentUser): boolean {
  return !user.githubTokenValid || user.missingScopes.length > 0
}
