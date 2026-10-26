import { render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CiBadge } from './CiBadge'
import { PullRequestList } from './PullRequestList'
import { RepoCard } from './RepoCard'
import type { RepositoryCard } from './useDashboard'

const NOW = new Date('2026-10-26T12:00:00Z')
const SHA = 'f'.repeat(40)

const freshCard: RepositoryCard = {
  id: '0192f0c4-0000-7000-8000-0000000000a1',
  fullName: 'acme/api',
  htmlUrl: 'https://github.com/acme/api',
  isPrivate: true,
  state: 'Fresh',
  openPrCount: 12,
  openIssueCount: 4,
  openPullRequests: [
    {
      number: 7,
      title: 'Add dashboard',
      url: 'https://github.com/acme/api/pull/7',
      author: 'octocat',
      createdAt: '2026-10-26T10:00:00Z',
      isDraft: false,
    },
    {
      number: 6,
      title: 'WIP: digest',
      url: 'https://github.com/acme/api/pull/6',
      author: null,
      createdAt: '2026-10-20T10:00:00Z',
      isDraft: true,
    },
  ],
  ci: {
    status: 'Failure',
    commitSha: SHA,
    checksUrl: `https://github.com/acme/api/commit/${SHA}/checks`,
  },
  lastActivityAt: '2026-10-26T11:55:00Z',
  refreshedAt: '2026-10-26T11:50:00Z',
  lastError: null,
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(NOW)
})

afterEach(() => {
  vi.useRealTimers()
})

describe('RepoCard', () => {
  it('shows counts, CI and pull requests for fresh snapshot', () => {
    render(<RepoCard card={freshCard} />)

    const card = screen.getByRole('article', { name: 'acme/api' })
    expect(
      within(card).getByRole('link', { name: 'acme/api' }),
    ).toHaveAttribute('href', 'https://github.com/acme/api')
    expect(within(card).getByText('prywatne')).toBeInTheDocument()
    expect(within(card).getByText('12')).toBeInTheDocument()
    expect(within(card).getByText('4')).toBeInTheDocument()
    expect(within(card).getByText('5 minut temu')).toBeInTheDocument()
    expect(within(card).getByRole('link', { name: 'CI: błąd' })).toBeVisible()
    expect(
      within(card).getByRole('link', { name: '#7 Add dashboard' }),
    ).toBeInTheDocument()
    expect(within(card).queryByText(/nieaktualne/)).not.toBeInTheDocument()
  })

  it('shows loading message without numbers before first snapshot', () => {
    render(
      <RepoCard
        card={{
          ...freshCard,
          state: 'Loading',
          openPrCount: null,
          openIssueCount: null,
          openPullRequests: [],
          ci: { status: 'None', commitSha: null, checksUrl: null },
          lastActivityAt: null,
          refreshedAt: null,
        }}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent(
      'Pobieramy dane z GitHuba',
    )
    expect(screen.queryByText('Otwarte PR')).not.toBeInTheDocument()
  })

  it('warns that data may be outdated for stale snapshot', () => {
    render(
      <RepoCard
        card={{
          ...freshCard,
          state: 'Stale',
          refreshedAt: '2026-10-26T09:00:00Z',
        }}
      />,
    )

    expect(
      screen.getByText(
        /Dane mogą być nieaktualne – ostatnio odświeżone 3 godziny temu/,
      ),
    ).toBeInTheDocument()
  })

  it('explains failed refresh and keeps last known data', () => {
    render(
      <RepoCard
        card={{ ...freshCard, state: 'Error', lastError: 'Forbidden' }}
      />,
    )

    expect(
      screen.getByText(/Ostatnie odświeżenie się nie udało \(Forbidden\)/),
    ).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })
})

describe('CiBadge', () => {
  it.each([
    ['Success', 'CI: zielone'],
    ['Failure', 'CI: błąd'],
    ['Pending', 'CI: w toku'],
  ] as const)('links %s status to checks', (status, label) => {
    render(<CiBadge status={status} checksUrl="https://github.com/c" />)

    const link = screen.getByRole('link', { name: label })
    expect(link).toHaveAttribute('href', 'https://github.com/c')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('renders plain label without checks url', () => {
    render(<CiBadge status="None" checksUrl={null} />)

    expect(screen.getByText('Brak CI')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})

describe('PullRequestList', () => {
  it('links pull requests safely and marks drafts', () => {
    render(
      <PullRequestList
        pullRequests={freshCard.openPullRequests}
        totalCount={2}
      />,
    )

    const link = screen.getByRole('link', { name: '#6 WIP: digest' })
    expect(link).toHaveAttribute('href', 'https://github.com/acme/api/pull/6')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(screen.getByText('szkic')).toBeInTheDocument()
    expect(screen.queryByText(/więcej na GitHubie/)).not.toBeInTheDocument()
  })

  it('mentions pull requests beyond the stored top list', () => {
    render(
      <PullRequestList
        pullRequests={freshCard.openPullRequests}
        totalCount={12}
      />,
    )

    expect(screen.getByText('…i 10 więcej na GitHubie.')).toBeInTheDocument()
  })

  it('shows empty message without pull requests', () => {
    render(<PullRequestList pullRequests={[]} totalCount={0} />)

    expect(screen.getByText('Brak otwartych PR-ów.')).toBeInTheDocument()
  })
})
