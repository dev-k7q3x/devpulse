/**
 * Jedyne źródło kluczy TanStack Query – invalidateQueries wymaga identycznych kluczy.
 * Klucze dochodzą razem z endpointami (auth, dashboard, repositories, settings, digests).
 */
export const queryKeys = {} as const
