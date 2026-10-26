import { RepoPicker } from '@/features/repositories/RepoPicker'
import { formatDateTime } from '@/lib/dateTime'
import { EmailSection } from './EmailSection'
import { ScheduleForm } from './ScheduleForm'
import { useSettings } from './useSettings'

export function SettingsPage() {
  const { data: settings, isPending, isError } = useSettings()

  return (
    <>
      <h1>Ustawienia</h1>
      {isPending && <p role="status">Ładowanie…</p>}
      {isError && (
        <p role="alert">Nie udało się wczytać ustawień. Odśwież stronę.</p>
      )}
      {settings && (
        <>
          <p className="next-digest">
            {settings.nextDigestAt
              ? `Następny digest: ${formatDateTime(settings.nextDigestAt, settings.timeZoneId)} (${settings.timeZoneId})`
              : 'Digest nie jest zaplanowany.'}
          </p>
          <ScheduleForm settings={settings} />
          <EmailSection settings={settings} />
        </>
      )}
      <section aria-labelledby="repositories-heading">
        <h2 id="repositories-heading">Repozytoria</h2>
        <RepoPicker />
      </section>
    </>
  )
}
