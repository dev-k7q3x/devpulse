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
      return loggedOut ? unauthenticatedResponse() : jsonResponse(user)
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
