import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
} from '@/test/renderWithProviders'
import type { Dashboard } from './useDashboard'

const user = {
  id: '0192f0c4-0000-7000-8000-000000000001',
  login: 'octocat',
  avatarUrl: null,
  githubTokenValid: true,
  missingScopes: [],
  isAdmin: false,
}

const emptyDashboard: Dashboard = {
  summary: {
    repositoryCount: 0,
    totalOpenPrs: 0,
    totalOpenIssues: 0,
    failingCiCount: 0,
  },
  oldestRefreshAt: null,
  repositories: [],
}

function mockApi(dashboard: Dashboard) {
  return mockFetch((_method, path) => {
    if (path === '/api/auth/me') return jsonResponse(user)
    if (path === '/api/dashboard') return jsonResponse(dashboard)
    if (path === '/api/repositories') return jsonResponse([])
    if (path.startsWith('/api/repositories/available'))
      return jsonResponse({
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        fetchedAt: '2026-10-26T12:00:00Z',
      })
    return new Response(null, { status: 500 })
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DashboardPage', () => {
  it('offers repository picker when nothing is tracked', async () => {
    mockApi(emptyDashboard)

    renderRoutes(appRoutes, '/')

    expect(
      await screen.findByRole('heading', {
        name: 'Nie śledzisz jeszcze żadnych repozytoriów',
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('region', { name: 'Wybór repozytoriów' }),
    ).toBeInTheDocument()
  })

  it('shows summary and a card per repository', async () => {
    mockApi({
      summary: {
        repositoryCount: 2,
        totalOpenPrs: 5,
        totalOpenIssues: 3,
        failingCiCount: 1,
      },
      oldestRefreshAt: new Date(Date.now() - 10 * 60 * 1000).toISOString(),
      repositories: [
        {
          id: '0192f0c4-0000-7000-8000-0000000000a1',
          fullName: 'acme/api',
          htmlUrl: 'https://github.com/acme/api',
          isPrivate: false,
          state: 'Fresh',
          openPrCount: 5,
          openIssueCount: 3,
          openPullRequests: [],
          ci: { status: 'Failure', commitSha: null, checksUrl: null },
          lastActivityAt: null,
          refreshedAt: null,
          lastError: null,
        },
        {
          id: '0192f0c4-0000-7000-8000-0000000000a2',
          fullName: 'acme/web',
          htmlUrl: 'https://github.com/acme/web',
          isPrivate: false,
          state: 'Loading',
          openPrCount: null,
          openIssueCount: null,
          openPullRequests: [],
          ci: { status: 'None', commitSha: null, checksUrl: null },
          lastActivityAt: null,
          refreshedAt: null,
          lastError: null,
        },
      ],
    })

    renderRoutes(appRoutes, '/')

    expect(
      await screen.findByRole('article', { name: 'acme/api' }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('article', { name: 'acme/web' }),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        /2 repozytoriów · 5 otwartych PR · 3 otwartych issue · 1 z błędem CI/,
      ),
    ).toBeInTheDocument()
    expect(screen.getByText(/Odświeżono 10 minut temu/)).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Zarządzaj repozytoriami' }),
    ).toHaveAttribute('href', '/settings')
  })
})
