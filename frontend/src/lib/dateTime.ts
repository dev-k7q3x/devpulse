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

/** Data i godzina z API (ISO 8601 UTC) w czasie lokalnym wskazanej strefy, np. „25.10.2026, 07:00”. */
export function formatDateTime(isoUtc: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pl-PL', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone,
  }).format(new Date(isoUtc))
}
