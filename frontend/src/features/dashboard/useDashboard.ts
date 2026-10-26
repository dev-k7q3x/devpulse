import { useQuery } from '@tanstack/react-query'
import { httpClient } from '@/api/httpClient'
import { queryKeys } from '@/api/queryKeys'
import type { components } from '@/api/schema'

export type Dashboard = components['schemas']['DashboardResponse']
export type RepositoryCard = components['schemas']['RepositoryCard']
export type PullRequestItem = components['schemas']['PullRequestItem']
export type CiStatus = components['schemas']['CiStatus']

const FIFTEEN_MINUTES = 15 * 60 * 1000
const LOADING_POLL = 30 * 1000

/**
 * Dashboard czyta tylko snapshoty z bazy, które job odświeża co 15 min – częstsze odpytanie nic nie da.
 * Wyjątek: karta w stanie Loading (świeżo dodane repo, pierwszy snapshot leci od razu) – wtedy co 30 s, aż się
 * uzupełni. To tylko odczyt z bazy, bez budżetu GitHuba. Przeglądarka sama wysyła If-None-Match (zwykle 304).
 */
export function useDashboard() {
  return useQuery({
    queryKey: queryKeys.dashboard.current,
    queryFn: () => httpClient.get<Dashboard>('/api/dashboard'),
    staleTime: FIFTEEN_MINUTES,
    refetchInterval: (query) =>
      query.state.data?.repositories.some((card) => card.state === 'Loading')
        ? LOADING_POLL
        : FIFTEEN_MINUTES,
  })
}
