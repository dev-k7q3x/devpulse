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

const TOKEN = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ConfirmEmailPage', () => {
  it('does not confirm on page open – only GET for XSRF token', async () => {
    const fetchMock = mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, `/confirm-email?token=${TOKEN}`)

    const button = await screen.findByRole('button', {
      name: 'Potwierdź adres',
    })
    await vi.waitFor(() => expect(button).toBeEnabled())
    const methods = fetchMock.mock.calls.map(([, init]) =>
      (init?.method ?? 'GET').toUpperCase(),
    )
    expect(methods).not.toContain('POST')
  })

  it('confirms email with POST without being signed in', async () => {
    const fetchMock = mockFetch((method, path) =>
      method === 'POST' && path === '/api/settings/email/confirm'
        ? jsonResponse({ email: 'new@example.com' })
        : unauthenticatedResponse(),
    )
    renderRoutes(appRoutes, `/confirm-email?token=${TOKEN}`)

    await userEvent.click(
      await screen.findByRole('button', { name: 'Potwierdź adres' }),
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Adres new@example.com został potwierdzony.',
    )
    const post = fetchMock.mock.calls.find(
      ([, init]) => init?.method === 'POST',
    )
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ token: TOKEN })
  })

  it('shows one generic error for invalid or expired token', async () => {
    mockFetch((method) =>
      method === 'POST'
        ? jsonResponse(
            {
              type: 'invalid-token',
              status: 400,
              detail: 'Link potwierdzający jest nieprawidłowy albo wygasł.',
            },
            400,
          )
        : unauthenticatedResponse(),
    )
    renderRoutes(appRoutes, `/confirm-email?token=${TOKEN}`)

    await userEvent.click(
      await screen.findByRole('button', { name: 'Potwierdź adres' }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Link jest nieprawidłowy, wygasł albo został już użyty.',
    )
  })

  it('reports missing token without calling the API', async () => {
    const fetchMock = mockFetch(() => unauthenticatedResponse())

    renderRoutes(appRoutes, '/confirm-email')

    expect(await screen.findByRole('alert')).toHaveTextContent('brakuje tokenu')
    expect(
      fetchMock.mock.calls.some(([, init]) => init?.method === 'POST'),
    ).toBe(false)
  })
})
