import { useState, type FormEvent } from 'react'
import { availableTimeZones, browserTimeZone } from '@/lib/dateTime'
import { problemMessage } from './problemMessage'
import { useUpdateSettings, type Settings } from './useSettings'

interface ScheduleFormProps {
  settings: Settings
}

const HOURS = Array.from({ length: 24 }, (_, hour) => hour)
const DEFAULT_TIME_ZONE = 'UTC'

/** Godzina wysyłki, strefa i włączenie digestu. Nowe konto (strefa domyślna UTC) dostaje propozycję strefy przeglądarki. */
export function ScheduleForm({ settings }: ScheduleFormProps) {
  const detectedTimeZone = browserTimeZone()
  const suggestBrowserZone =
    settings.timeZoneId === DEFAULT_TIME_ZONE &&
    detectedTimeZone !== undefined &&
    detectedTimeZone !== DEFAULT_TIME_ZONE

  const [sendHour, setSendHour] = useState(Number(settings.sendHour))
  const [timeZoneId, setTimeZoneId] = useState(
    suggestBrowserZone ? detectedTimeZone : settings.timeZoneId,
  )
  const [digestEnabled, setDigestEnabled] = useState(settings.digestEnabled)
  const update = useUpdateSettings()

  const timeZones = availableTimeZones(settings.timeZoneId, detectedTimeZone)

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    update.mutate({ sendHour, timeZoneId, digestEnabled })
  }

  return (
    <form className="settings-form" onSubmit={handleSubmit}>
      <h2>Digest</h2>
      <label>
        <input
          type="checkbox"
          checked={digestEnabled}
          onChange={(event) => setDigestEnabled(event.target.checked)}
        />
        Wysyłaj codzienny digest
      </label>
      <label>
        Godzina wysyłki
        <select
          value={sendHour}
          onChange={(event) => setSendHour(Number(event.target.value))}
        >
          {HOURS.map((hour) => (
            <option key={hour} value={hour}>
              {`${String(hour).padStart(2, '0')}:00`}
            </option>
          ))}
        </select>
      </label>
      <label>
        Strefa czasowa
        <select
          value={timeZoneId}
          onChange={(event) => setTimeZoneId(event.target.value)}
        >
          {timeZones.map((zone) => (
            <option key={zone} value={zone}>
              {zone}
            </option>
          ))}
        </select>
      </label>
      {suggestBrowserZone && timeZoneId === detectedTimeZone && (
        <p className="hint">
          Proponujemy strefę z Twojej przeglądarki – zapisz, aby ją ustawić.
        </p>
      )}
      {update.isError && (
        <p role="alert">
          {problemMessage(update.error, 'Nie udało się zapisać ustawień.')}
        </p>
      )}
      {update.isSuccess && <p role="status">Zapisano ustawienia.</p>}
      <button type="submit" disabled={update.isPending}>
        Zapisz
      </button>
    </form>
  )
}
