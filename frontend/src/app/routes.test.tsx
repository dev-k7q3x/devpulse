import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CurrentUser } from '@/features/auth/useCurrentUser'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
  unauthenticatedResponse,
} from '@/test/renderWithProviders'
import { appRoutes } from './routes'

const user: CurrentUser = {
  id: '0192f0c4-0000-7000-8000-000000000001',
  login: 'octocat',
  avatarUrl: null,
  githubTokenValid: true,
  missingScopes: [],
  isAdmin: false,
}

const emptyDashboard = {
  summary: {
    repositoryCount: 0,
    totalOpenPrs: 0,
    totalOpenIssues: 0,
    failingCiCount: 0,
  },
  oldestRefreshAt: null,
  repositories: [],
}

/** Zalogowany użytkownik `me`; dashboard pusty, reszta API (ustawienia, repo) odpowiada 500 – testy patrzą na układ. */
function signedInApi(me: CurrentUser) {
  return mockFetch((_method, path) => {
    if (path === '/api/auth/me') return jsonResponse(me)
    if (path === '/api/dashboard') return jsonResponse(emptyDashboard)
    return new Response(null, { status: 500 })
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('appRoutes', () => {
  it.each([
    ['/', 'Dashboard'],
    ['/settings', 'Ustawienia'],
    ['/digests', 'Historia digestów'],
  ])(
    'renders %s page inside app layout for signed-in user',
    async (path, heading) => {
      signedInApi(user)

      renderRoutes(appRoutes, path)

      expect(
        await screen.findByRole('heading', { name: heading }),
      ).toBeInTheDocument()
      expect(
        screen.getByRole('navigation', { name: 'Główna nawigacja' }),
      ).toBeInTheDocument()
      expect(screen.getByText('octocat')).toBeInTheDocument()
    },
  )

  it('redirects anonymous user to login with return url', async () => {
    mockFetch(() => unauthenticatedResponse())

    const { router } = renderRoutes(appRoutes, '/settings')

    expect(
      await screen.findByRole('heading', { name: 'Zaloguj się' }),
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(router.state.location.search).toBe('?returnUrl=%2Fsettings')
    expect(
      screen.queryByRole('navigation', { name: 'Główna nawigacja' }),
    ).not.toBeInTheDocument()
  })

  it('shows re-login banner when GitHub token is invalid', async () => {
    signedInApi({ ...user, githubTokenValid: false })

    renderRoutes(appRoutes, '/')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj się ponownie' }),
    ).toHaveAttribute('href', '/api/auth/login')
  })

  it('shows re-login banner when scopes are missing', async () => {
    signedInApi({ ...user, missingScopes: ['repo'] })

    renderRoutes(appRoutes, '/digests')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj się ponownie' }),
    ).toHaveAttribute('href', '/api/auth/login?returnUrl=%2Fdigests')
  })

  it('does not show re-login banner for healthy token', async () => {
    signedInApi(user)

    renderRoutes(appRoutes, '/')

    await screen.findByText('octocat')
    expect(
      screen.queryByRole('link', { name: 'Zaloguj się ponownie' }),
    ).not.toBeInTheDocument()
  })
})
