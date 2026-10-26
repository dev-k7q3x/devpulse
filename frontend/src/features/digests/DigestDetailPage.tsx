import { Link, useParams } from 'react-router'
import { ProblemError } from '@/api/httpClient'
import { formatDateTime } from '@/lib/dateTime'
import {
  formatDigestDate,
  prioritiesSourceLabel,
  severityLabels,
  statusLabels,
} from './digestLabels'
import { digestHtmlUrl, useDigest, type DigestDetail } from './useDigests'
import './digests.css'

export function DigestDetailPage() {
  const { id = '' } = useParams()
  const { data: digest, isPending, error } = useDigest(id)

  return (
    <section>
      <p>
        <Link to="/digests">← Historia digestów</Link>
      </p>
      {isPending && <p role="status">Ładowanie…</p>}
      {error && (
        <p role="alert">
          {error instanceof ProblemError && error.problem.type === 'not-found'
            ? 'Nie znaleziono digestu – mógł zostać usunięty po 30 dniach.'
            : 'Nie udało się wczytać digestu. Odśwież stronę.'}
        </p>
      )}
      {digest && <DigestDetailView digest={digest} />}
    </section>
  )
}

interface DigestDetailViewProps {
  digest: DigestDetail
}

function DigestDetailView({ digest }: DigestDetailViewProps) {
  const date = formatDigestDate(digest.digestDate)

  return (
    <>
      <h1>{digest.subject ?? `Digest z ${date}`}</h1>
      <p className="digest-meta">
        <span>{date}</span>
        <span
          className={`digest-status digest-status-${digest.status.toLowerCase()}`}
        >
          {statusLabels[digest.status]}
        </span>
        {digest.sentAt && (
          <span>
            Wysłano {formatDateTime(digest.sentAt, digest.timeZoneId)}
          </span>
        )}
        {digest.recipientEmail && <span>Do: {digest.recipientEmail}</span>}
      </p>
      {digest.error && <p role="alert">{digest.error}</p>}

      {digest.priorities.length > 0 && (
        <>
          <h2>{prioritiesSourceLabel(digest.aiUsed)}</h2>
          <ol>
            {digest.priorities.map((priority) => (
              <li key={`${priority.repoFullName}-${priority.text}`}>
                <strong>{severityLabels[priority.severity]}</strong> ·{' '}
                {priority.repoFullName}: {priority.text}
              </li>
            ))}
          </ol>
        </>
      )}

      <h2>Podgląd maila</h2>
      {digest.hasHtml ? (
        // Pusty sandbox: bez skryptów, formularzy i dostępu do origin aplikacji – plus CSP z serwera.
        <iframe
          className="digest-preview"
          sandbox=""
          src={digestHtmlUrl(digest.id)}
          title={`Podgląd digestu z ${date}`}
        />
      ) : (
        <p className="digest-notice">Ten digest nie ma treści do podglądu.</p>
      )}
    </>
  )
}
