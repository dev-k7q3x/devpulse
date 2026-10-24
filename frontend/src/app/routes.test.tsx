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
      mockFetch(() => jsonResponse(user))

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
    mockFetch(() => jsonResponse({ ...user, githubTokenValid: false }))

    renderRoutes(appRoutes, '/')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj się ponownie' }),
    ).toHaveAttribute('href', '/api/auth/login')
  })

  it('shows re-login banner when scopes are missing', async () => {
    mockFetch(() => jsonResponse({ ...user, missingScopes: ['repo'] }))

    renderRoutes(appRoutes, '/digests')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj się ponownie' }),
    ).toHaveAttribute('href', '/api/auth/login?returnUrl=%2Fdigests')
  })

  it('does not show re-login banner for healthy token', async () => {
    mockFetch(() => jsonResponse(user))

    renderRoutes(appRoutes, '/')

    await screen.findByText('octocat')
    expect(
      screen.queryByRole('link', { name: 'Zaloguj się ponownie' }),
    ).not.toBeInTheDocument()
  })
})
