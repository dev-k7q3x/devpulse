import { formatRelative } from '@/lib/dateTime'
import type { PullRequestItem } from './useDashboard'

interface PullRequestListProps {
  pullRequests: PullRequestItem[]
  totalCount: number
}

/** Najnowsze otwarte PR (snapshot trzyma maks. 10) z linkami do GitHuba. */
export function PullRequestList({
  pullRequests,
  totalCount,
}: PullRequestListProps) {
  if (pullRequests.length === 0) {
    return <p className="muted">Brak otwartych PR-ów.</p>
  }

  const hidden = totalCount - pullRequests.length

  return (
    <>
      <ul className="pull-request-list" aria-label="Otwarte pull requesty">
        {pullRequests.map((pr) => (
          <li key={String(pr.number)}>
            <a href={pr.url} target="_blank" rel="noopener noreferrer">
              #{String(pr.number)} {pr.title}
            </a>
            {pr.isDraft && <span className="tag">szkic</span>}
            <span className="muted">
              {pr.author ? `${pr.author}, ` : ''}
              {formatRelative(pr.createdAt)}
            </span>
          </li>
        ))}
      </ul>
      {hidden > 0 && <p className="muted">…i {hidden} więcej na GitHubie.</p>}
    </>
  )
}
