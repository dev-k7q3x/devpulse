import { useQuery } from '@tanstack/react-query'
import { httpClient } from '@/api/httpClient'
import { queryKeys } from '@/api/queryKeys'
import type { components } from '@/api/schema'

export type DigestListItem = components['schemas']['DigestListItem']
export type DigestDetail = components['schemas']['DigestDetailResponse']
export type DigestStatus = components['schemas']['DigestStatus']
export type PrioritySeverity = components['schemas']['PrioritySeverity']

/** Historia z ostatnich 30 dni, od najnowszego (bez HTML – podgląd idzie osobnym endpointem do iframe). */
export function useDigestList() {
  return useQuery({
    queryKey: queryKeys.digests.list,
    queryFn: () => httpClient.get<DigestListItem[]>('/api/digests'),
  })
}

export function useDigest(id: string) {
  return useQuery({
    queryKey: queryKeys.digests.detail(id),
    queryFn: () =>
      httpClient.get<DigestDetail>(`/api/digests/${encodeURIComponent(id)}`),
  })
}

/** Adres podglądu HTML – wyłącznie jako `src` sandboxowanego iframe. */
export function digestHtmlUrl(id: string): string {
  return `/api/digests/${encodeURIComponent(id)}/html`
}
