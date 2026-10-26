import { formatRelative } from '@/lib/dateTime'
import { CiBadge } from './CiBadge'
import { PullRequestList } from './PullRequestList'
import type { RepositoryCard } from './useDashboard'

interface RepoCardProps {
  card: RepositoryCard
}

/** Karta repozytorium: liczby PR i issue, status CI, ostatnia aktywność. Stan karty decyduje o komunikacie. */
export function RepoCard({ card }: RepoCardProps) {
  return (
    <article
      className={`repo-card repo-card--${card.state.toLowerCase()}`}
      aria-label={card.fullName}
    >
      <header className="repo-card__header">
        <h2>
          <a href={card.htmlUrl} target="_blank" rel="noopener noreferrer">
            {card.fullName}
          </a>
        </h2>
        {card.isPrivate && <span className="tag">prywatne</span>}
      </header>

      {card.state === 'Loading' ? (
        <p role="status">
          Pobieramy dane z GitHuba – karta uzupełni się za chwilę.
        </p>
      ) : (
        <>
          <StateNotice card={card} />
          <dl className="repo-card__stats">
            <div>
              <dt>Otwarte PR</dt>
              <dd>{String(card.openPrCount ?? 0)}</dd>
            </div>
            <div>
              <dt>Otwarte issue</dt>
              <dd>{String(card.openIssueCount ?? 0)}</dd>
            </div>
            <div>
              <dt>Ostatnia aktywność</dt>
              <dd>
                {card.lastActivityAt
                  ? formatRelative(card.lastActivityAt)
                  : 'brak danych'}
              </dd>
            </div>
          </dl>
          <CiBadge status={card.ci.status} checksUrl={card.ci.checksUrl} />
          <PullRequestList
            pullRequests={card.openPullRequests}
            totalCount={Number(card.openPrCount ?? 0)}
          />
        </>
      )}
    </article>
  )
}

interface StateNoticeProps {
  card: RepositoryCard
}

function StateNotice({ card }: StateNoticeProps) {
  if (card.state === 'Error') {
    return (
      <p className="notice notice--error">
        Ostatnie odświeżenie się nie udało ({card.lastError ?? 'błąd'}) –
        pokazujemy ostatnie znane dane
        {card.refreshedAt ? ` z ${formatRelative(card.refreshedAt)}` : ''}.
      </p>
    )
  }

  if (card.state === 'Stale') {
    return (
      <p className="notice">
        Dane mogą być nieaktualne
        {card.refreshedAt
          ? ` – ostatnio odświeżone ${formatRelative(card.refreshedAt)}`
          : ''}
        .
      </p>
    )
  }

  return null
}
