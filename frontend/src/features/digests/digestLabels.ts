import type { DigestStatus, PrioritySeverity } from './useDigests'

export const statusLabels: Record<DigestStatus, string> = {
  Sent: 'Wysłany',
  Failed: 'Nieudany',
  Pending: 'W przygotowaniu',
}

export const severityLabels: Record<PrioritySeverity, string> = {
  High: 'Pilne',
  Medium: 'Ważne',
  Low: 'Do rozważenia',
}

/** Skąd pochodzą priorytety – AI (Claude) albo fallback regułowy. */
export function prioritiesSourceLabel(aiUsed: boolean): string {
  return aiUsed ? 'Priorytety AI' : 'Priorytety regułowe'
}

/**
 * `digestDate` to data lokalna użytkownika (`yyyy-MM-dd`), nie moment w czasie – formatujemy ją bez konwersji stref,
 * inaczej `new Date('2026-10-21')` (północ UTC) mogłoby pokazać poprzedni dzień.
 */
export function formatDigestDate(digestDate: string): string {
  const [year, month, day] = digestDate.split('-')
  return year && month && day ? `${day}.${month}.${year}` : digestDate
}
