import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
  unauthenticatedResponse,
} from '@/test/renderWithProviders'

const user = {
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

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('logout', () => {
  it('posts logout and returns to login page', async () => {
    let loggedOut = false
    const fetchMock = mockFetch((method, path) => {
      if (method === 'POST' && path === '/api/auth/logout') {
        loggedOut = true
        return new Response(null, { status: 204 })
      }
      if (loggedOut) return unauthenticatedResponse()
      if (path === '/api/auth/me') return jsonResponse(user)
      if (path === '/api/dashboard') return jsonResponse(emptyDashboard)
      return new Response(null, { status: 500 })
    })
    const { router } = renderRoutes(appRoutes, '/')

    await userEvent.click(
      await screen.findByRole('button', { name: 'Wyloguj' }),
    )

    expect(
      await screen.findByRole('heading', { name: 'Zaloguj się' }),
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/logout',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
