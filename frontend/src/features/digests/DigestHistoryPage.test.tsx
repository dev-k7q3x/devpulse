import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
} from '@/test/renderWithProviders'
import {
  currentUser,
  detailOf,
  failedDigest,
  sentDigest,
} from './digestTestData'

function mockApi(digests: unknown, status = 200) {
  return mockFetch((_method, path) => {
    if (path === '/api/auth/me') return jsonResponse(currentUser)
    if (path === '/api/digests') return jsonResponse(digests, status)
    if (path === `/api/digests/${sentDigest.id}`)
      return jsonResponse(detailOf(sentDigest))
    return new Response(null, { status: 500 })
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DigestHistoryPage', () => {
  it('lists digests with status and priority source', async () => {
    mockApi([sentDigest, failedDigest])

    renderRoutes(appRoutes, '/digests')

    const list = await screen.findByRole('list', {
      name: 'Digesty z ostatnich 30 dni',
    })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('DevPulse – 2 rzeczy na dziś')
    expect(items[0]).toHaveTextContent('21.10.2026')
    expect(items[0]).toHaveTextContent('Wysłany')
    expect(items[0]).toHaveTextContent('Priorytety AI: 2')
    expect(items[1]).toHaveTextContent('Digest z 20.10.2026')
    expect(items[1]).toHaveTextContent('Nieudany')
    expect(items[1]).toHaveTextContent('Priorytety regułowe: 0')
  })

  it('shows empty state with link to settings', async () => {
    mockApi([])

    renderRoutes(appRoutes, '/digests')

    expect(
      await screen.findByText(/Brak digestów z ostatnich 30 dni/),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'ustawieniach' })).toHaveAttribute(
      'href',
      '/settings',
    )
  })

  it('shows error when history cannot be loaded', async () => {
    mockApi({ type: 'about:blank', status: 500 }, 500)

    renderRoutes(appRoutes, '/digests')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Nie udało się wczytać historii',
    )
  })

  it('opens digest detail from the list', async () => {
    mockApi([sentDigest])
    const { router } = renderRoutes(appRoutes, '/digests')

    await userEvent.click(
      await screen.findByRole('link', { name: /DevPulse – 2 rzeczy na dziś/ }),
    )

    expect(router.state.location.pathname).toBe(`/digests/${sentDigest.id}`)
    expect(
      await screen.findByRole('heading', {
        name: 'DevPulse – 2 rzeczy na dziś',
      }),
    ).toBeInTheDocument()
  })
})
