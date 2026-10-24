import {
  hashKey,
  MutationCache,
  QueryCache,
  QueryClient,
} from '@tanstack/react-query'
import { queryKeys } from '@/api/queryKeys'
import { isUnauthenticated } from '@/features/auth/useCurrentUser'

export function createQueryClient(): QueryClient {
  // `unauthenticated` z dowolnego zapytania = sesja wygasła: odświeżenie /api/auth/me przełącza ProtectedRoute na /login.
  const refreshSessionOnUnauthenticated = (error: Error) => {
    if (isUnauthenticated(error)) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.auth.me })
    }
  }

  const queryClient: QueryClient = new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        // Po hashu, nie referencji – 401 z samego /me nie może go unieważniać (pętla zapytań).
        if (query.queryHash !== hashKey(queryKeys.auth.me)) {
          refreshSessionOnUnauthenticated(error)
        }
      },
    }),
    mutationCache: new MutationCache({
      onError: refreshSessionOnUnauthenticated,
    }),
    defaultOptions: {
      queries: { retry: 1, refetchOnWindowFocus: false },
    },
  })
  return queryClient
}
