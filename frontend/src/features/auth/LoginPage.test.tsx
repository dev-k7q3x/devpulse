import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
  unauthenticatedResponse,
} from '@/test/renderWithProviders'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('LoginPage', () => {
  it('links to GitHub login with local return url', async () => {
    mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, '/login?returnUrl=%2Fsettings')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj przez GitHub' }),
    ).toHaveAttribute('href', '/api/auth/login?returnUrl=%2Fsettings')
  })

  it('ignores external return url', async () => {
    mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, '/login?returnUrl=%2F%2Fevil.example')

    expect(
      await screen.findByRole('link', { name: 'Zaloguj przez GitHub' }),
    ).toHaveAttribute('href', '/api/auth/login')
  })

  it('explains access denied on GitHub', async () => {
    mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, '/login?error=access_denied')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Logowanie zostało anulowane na GitHubie',
    )
  })

  it('shows no error without error parameter', async () => {
    mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, '/login')

    await screen.findByRole('link', { name: 'Zaloguj przez GitHub' })
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('redirects signed-in user to return url', async () => {
    mockFetch(() =>
      jsonResponse({
        id: '0192f0c4-0000-7000-8000-000000000001',
        login: 'octocat',
        avatarUrl: null,
        githubTokenValid: true,
        missingScopes: [],
        isAdmin: false,
      }),
    )

    const { router } = renderRoutes(appRoutes, '/login?returnUrl=%2Fdigests')

    expect(
      await screen.findByRole('heading', { name: 'Historia digestów' }),
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/digests')
  })
})
