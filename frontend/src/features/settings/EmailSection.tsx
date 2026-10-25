import { useState, type FormEvent } from 'react'
import { formatDateTime } from '@/lib/dateTime'
import { problemMessage } from './problemMessage'
import {
  useCancelEmailChange,
  useStartEmailChange,
  type Settings,
} from './useSettings'

interface EmailSectionProps {
  settings: Settings
}

/** Adres digestu: aktualny, oczekujący na potwierdzenie i formularz zmiany (link wysyłany na nowy adres). */
export function EmailSection({ settings }: EmailSectionProps) {
  const [email, setEmail] = useState('')
  const startChange = useStartEmailChange()
  const cancelChange = useCancelEmailChange()

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    startChange.mutate(
      { email: email.trim() },
      { onSuccess: () => setEmail('') },
    )
  }

  return (
    <section className="settings-form" aria-labelledby="email-heading">
      <h2 id="email-heading">Adres email</h2>
      {settings.email && settings.emailConfirmed ? (
        <p>
          Digest trafia na <strong>{settings.email}</strong>.
        </p>
      ) : (
        <p role="alert">
          Brak potwierdzonego adresu – digest nie będzie wysyłany, dopóki nie
          potwierdzisz adresu.
        </p>
      )}

      {settings.pendingEmail && (
        <div className="pending-email">
          <p>
            Czeka na potwierdzenie: <strong>{settings.pendingEmail}</strong>
            {settings.pendingEmailExpiresAt &&
              ` (link ważny do ${formatDateTime(settings.pendingEmailExpiresAt)})`}
            . Sprawdź skrzynkę i kliknij link w wiadomości.
          </p>
          <button
            type="button"
            onClick={() => cancelChange.mutate()}
            disabled={cancelChange.isPending}
          >
            Anuluj zmianę
          </button>
        </div>
      )}

      <form onSubmit={handleSubmit}>
        <label>
          Nowy adres email
          <input
            type="email"
            required
            maxLength={254}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </label>
        {startChange.isError && (
          <p role="alert">
            {problemMessage(
              startChange.error,
              'Nie udało się rozpocząć zmiany adresu.',
            )}
          </p>
        )}
        <button type="submit" disabled={startChange.isPending}>
          Wyślij link potwierdzający
        </button>
      </form>
    </section>
  )
}
