/**
 * Jedyne źródło kluczy TanStack Query – invalidateQueries wymaga identycznych kluczy.
 * Klucze dochodzą razem z endpointami (auth, dashboard, repositories, settings, digests).
 */
export const queryKeys = {
  auth: {
    me: ['auth', 'me'] as const,
  },
  settings: {
    current: ['settings'] as const,
  },
  dashboard: {
    current: ['dashboard'] as const,
  },
  repositories: {
    /** Prefiks – unieważnia listę śledzonych i wszystkie strony `available`. */
    all: ['repositories'] as const,
    tracked: ['repositories', 'tracked'] as const,
    available: (search: string, page: number) =>
      ['repositories', 'available', { search, page }] as const,
  },
} as const
