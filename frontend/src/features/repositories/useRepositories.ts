import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { httpClient } from '@/api/httpClient'
import { queryKeys } from '@/api/queryKeys'
import type { components } from '@/api/schema'

export type AvailableRepositories =
  components['schemas']['AvailableRepositoriesResponse']
export type AvailableRepository =
  components['schemas']['AvailableRepositoryItem']
export type TrackedRepository =
  components['schemas']['TrackedRepositoryResponse']

export const PAGE_SIZE = 20

function availableUrl(search: string, page: number, refresh: boolean) {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(PAGE_SIZE),
  })
  if (search) {
    query.set('search', search)
  }
  if (refresh) {
    query.set('refresh', 'true')
  }
  return `/api/repositories/available?${query.toString()}`
}

/** Repo dostępne na GitHubie – serwer trzyma pełną listę 10 min w cache, filtruje i paginuje w pamięci. */
export function useAvailableRepositories(search: string, page: number) {
  return useQuery({
    queryKey: queryKeys.repositories.available(search, page),
    queryFn: () =>
      httpClient.get<AvailableRepositories>(availableUrl(search, page, false)),
    placeholderData: keepPreviousData,
    retry: false,
  })
}

/**
 * Jednorazowe pominięcie cache serwera (`refresh=true`, osobny limit 2/min). Potem zwykłe zapytania czytają
 * już świeży cache, więc unieważniamy je zamiast trzymać `refresh` w kluczu.
 */
export function useRefreshAvailableRepositories() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () =>
      httpClient.get<AvailableRepositories>(availableUrl('', 1, true)),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.repositories.all }),
  })
}

/** Śledzone repo z bazy – potrzebne do `DELETE /api/repositories/{id}` (lista available zna tylko id GitHuba). */
export function useTrackedRepositories() {
  return useQuery({
    queryKey: queryKeys.repositories.tracked,
    queryFn: () => httpClient.get<TrackedRepository[]>('/api/repositories'),
  })
}

/** Po dodaniu/usunięciu odświeżamy listy repo i dashboard – te same klucze z queryKeys.ts. */
function useRepositoryMutation<TVariables, TResult>(
  mutationFn: (variables: TVariables) => Promise<TResult>,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: queryKeys.repositories.all,
        }),
        queryClient.invalidateQueries({
          queryKey: queryKeys.dashboard.current,
        }),
      ])
    },
  })
}

export function useAddRepository() {
  return useRepositoryMutation((githubRepoId: number) =>
    httpClient.post<TrackedRepository>('/api/repositories', { githubRepoId }),
  )
}

export function useRemoveRepository() {
  return useRepositoryMutation((id: string) =>
    httpClient.delete(`/api/repositories/${id}`),
  )
}
