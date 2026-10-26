import type { CiStatus } from './useDashboard'

interface CiBadgeProps {
  status: CiStatus
  checksUrl: string | null
}

const LABELS: Record<CiStatus, string> = {
  Success: 'CI: zielone',
  Failure: 'CI: błąd',
  Pending: 'CI: w toku',
  None: 'Brak CI',
}

/** Status ostatniego przebiegu CI; z linkiem do checków na GitHubie, gdy znany jest commit. */
export function CiBadge({ status, checksUrl }: CiBadgeProps) {
  const label = LABELS[status]
  const className = `ci-badge ci-badge--${status.toLowerCase()}`

  return checksUrl ? (
    <a
      className={className}
      href={checksUrl}
      target="_blank"
      rel="noopener noreferrer"
    >
      {label}
    </a>
  ) : (
    <span className={className}>{label}</span>
  )
}
