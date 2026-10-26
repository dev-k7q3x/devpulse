import { Link } from 'react-router'
import { EmptyState } from '@/components/EmptyState'
import { RepoPicker } from '@/features/repositories/RepoPicker'
import { formatRelative } from '@/lib/dateTime'
import { RepoCard } from './RepoCard'
import { useDashboard } from './useDashboard'

export function DashboardPage() {
  const { data: dashboard, isPending, isError } = useDashboard()

  return (
    <>
      <h1>Dashboard</h1>
      {isPending && <p role="status">Ładowanie…</p>}
      {isError && (
        <p role="alert">Nie udało się wczytać dashboardu. Odśwież stronę.</p>
      )}
      {dashboard && dashboard.repositories.length === 0 && (
        <EmptyState
          title="Nie śledzisz jeszcze żadnych repozytoriów"
          description="Wybierz repozytoria z GitHuba – karty pojawią się tutaj, a dane odświeżają się co 15 minut."
        >
          <RepoPicker />
        </EmptyState>
      )}
      {dashboard && dashboard.repositories.length > 0 && (
        <>
          <section className="dashboard-summary" aria-label="Podsumowanie">
            <p>
              {String(dashboard.summary.repositoryCount)} repozytoriów ·{' '}
              {String(dashboard.summary.totalOpenPrs)} otwartych PR ·{' '}
              {String(dashboard.summary.totalOpenIssues)} otwartych issue ·{' '}
              {String(dashboard.summary.failingCiCount)} z błędem CI
            </p>
            <p className="muted">
              {dashboard.oldestRefreshAt
                ? `Odświeżono ${formatRelative(dashboard.oldestRefreshAt)}.`
                : 'Pierwsze dane pojawią się za chwilę.'}{' '}
              <Link to="/settings">Zarządzaj repozytoriami</Link>
            </p>
          </section>
          <div className="repo-grid">
            {dashboard.repositories.map((card) => (
              <RepoCard key={card.id} card={card} />
            ))}
          </div>
        </>
      )}
    </>
  )
}
