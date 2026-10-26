import { Link } from 'react-router'
import {
  formatDigestDate,
  prioritiesSourceLabel,
  statusLabels,
} from './digestLabels'
import { useDigestList, type DigestListItem } from './useDigests'
import './digests.css'

export function DigestHistoryPage() {
  const { data: digests, isPending, isError } = useDigestList()

  return (
    <section>
      <h1>Historia digestów</h1>
      {isPending && <p role="status">Ładowanie…</p>}
      {isError && (
        <p role="alert">Nie udało się wczytać historii. Odśwież stronę.</p>
      )}
      {digests && digests.length === 0 && (
        <p className="digest-empty">
          Brak digestów z ostatnich 30 dni. Pierwszy przyjdzie o godzinie
          ustawionej w <Link to="/settings">ustawieniach</Link>.
        </p>
      )}
      {digests && digests.length > 0 && (
        <ul className="digest-list" aria-label="Digesty z ostatnich 30 dni">
          {digests.map((digest) => (
            <DigestListEntry key={digest.id} digest={digest} />
          ))}
        </ul>
      )}
    </section>
  )
}

interface DigestListEntryProps {
  digest: DigestListItem
}

function DigestListEntry({ digest }: DigestListEntryProps) {
  const date = formatDigestDate(digest.digestDate)

  return (
    <li className="digest-list-item">
      <Link to={`/digests/${digest.id}`}>
        <strong>{digest.subject ?? `Digest z ${date}`}</strong>
        <span className="digest-meta">
          <span>{date}</span>
          <span
            className={`digest-status digest-status-${digest.status.toLowerCase()}`}
          >
            {statusLabels[digest.status]}
          </span>
          {digest.status !== 'Pending' && (
            <span>
              {prioritiesSourceLabel(digest.aiUsed)}: {digest.priorities.length}
            </span>
          )}
        </span>
      </Link>
    </li>
  )
}
