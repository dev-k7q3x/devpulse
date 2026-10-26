/** Strefa IANA przeglądarki (np. `Europe/Warsaw`), `undefined`, gdy środowisko jej nie podaje. */
export function browserTimeZone(): string | undefined {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined
  } catch {
    return undefined
  }
}

/** Lista stref IANA do wyboru; starsze przeglądarki bez `Intl.supportedValuesOf` dostają tylko podane strefy. */
export function availableTimeZones(
  ...include: (string | undefined)[]
): string[] {
  const zones = new Set<string>()
  try {
    for (const zone of Intl.supportedValuesOf('timeZone')) {
      zones.add(zone)
    }
  } catch {
    // brak API – zostają tylko strefy z argumentów
  }
  zones.add('UTC')
  for (const zone of include) {
    if (zone) {
      zones.add(zone)
    }
  }
  return [...zones].sort()
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
]

/** Czas względny po polsku, np. „5 minut temu”, „wczoraj”; poniżej minuty – „przed chwilą”. */
export function formatRelative(
  isoUtc: string,
  now: number = Date.now(),
): string {
  const seconds = Math.round((new Date(isoUtc).getTime() - now) / 1000)
  const format = new Intl.RelativeTimeFormat('pl-PL', { numeric: 'auto' })
  for (const [unit, unitSeconds] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= unitSeconds) {
      return format.format(Math.trunc(seconds / unitSeconds), unit)
    }
  }
  return 'przed chwilą'
}

/** Sekundy z nagłówka Retry-After jako „za 2 minuty” / „za 30 sekund”; bez wartości – „za chwilę”. */
export function formatRetryAfter(seconds: number | undefined): string {
  if (!seconds) {
    return 'za chwilę'
  }
  const format = new Intl.RelativeTimeFormat('pl-PL', { numeric: 'auto' })
  return seconds < 60
    ? format.format(Math.ceil(seconds), 'second')
    : format.format(Math.ceil(seconds / 60), 'minute')
}

/** Data i godzina z API (ISO 8601 UTC) w czasie lokalnym wskazanej strefy, np. „25.10.2026, 07:00”. */
export function formatDateTime(isoUtc: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pl-PL', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone,
  }).format(new Date(isoUtc))
}
