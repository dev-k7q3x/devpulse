import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
} from '@/test/renderWithProviders'
import { RepoPicker } from './RepoPicker'
import type { AvailableRepository, TrackedRepository } from './useRepositories'

const hello: AvailableRepository = {
  githubRepoId: 11,
  fullName: 'octocat/hello',
  htmlUrl: 'https://github.com/octocat/hello',
  isPrivate: false,
  description: 'Pierwsze repo',
  pushedAt: null,
  isTracked: false,
}

const api: AvailableRepository = {
  githubRepoId: 12,
  fullName: 'acme/api',
  htmlUrl: 'https://github.com/acme/api',
  isPrivate: true,
  description: null,
  pushedAt: null,
  isTracked: true,
}

const trackedApi: TrackedRepository = {
  id: '0192f0c4-0000-7000-8000-0000000000b2',
  githubRepoId: 12,
  fullName: 'acme/api',
  htmlUrl: 'https://github.com/acme/api',
  isPrivate: true,
  addedAt: '2026-10-25T10:00:00Z',
}

function availablePage(
  items: AvailableRepository[],
  totalCount = items.length,
) {
  return jsonResponse({
    items,
    page: 1,
    pageSize: 20,
    totalCount,
    fetchedAt: '2026-10-26T12:00:00Z',
  })
}

function problem(
  type: string,
  status: number,
  headers: Record<string, string> = {},
) {
  return new Response(JSON.stringify({ type, status, detail: null }), {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...headers },
  })
}

type Handler = (method: string, path: string) => Response | undefined

/** Lista available i śledzonych odpowiada domyślnie; `handler` nadpisuje wybrane żądania. */
function mockApi(handler: Handler = () => undefined) {
  return mockFetch((method, path) => {
    const custom = handler(method, path)
    if (custom) return custom
    if (method === 'GET' && path === '/api/repositories')
      return jsonResponse([trackedApi])
    if (method === 'GET' && path.startsWith('/api/repositories/available'))
      return availablePage([api, hello])
    return new Response(null, { status: 500 })
  })
}

function renderPicker() {
  return renderRoutes([{ path: '/', element: <RepoPicker /> }], '/')
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('RepoPicker', () => {
  it('lists repositories with track and untrack actions', async () => {
    mockApi()

    renderPicker()

    const list = await screen.findByRole('list', {
      name: 'Repozytoria na GitHubie',
    })
    expect(
      within(list).getByRole('button', { name: 'Śledź octocat/hello' }),
    ).toBeEnabled()
    expect(
      within(list).getByRole('button', {
        name: 'Przestań śledzić acme/api',
      }),
    ).toBeEnabled()
    expect(screen.getByText('Strona 1 z 1')).toBeInTheDocument()
  })

  it('sends search term to the server', async () => {
    const fetchMock = mockApi()
    renderPicker()
    await screen.findByRole('list', { name: 'Repozytoria na GitHubie' })

    await userEvent.type(
      screen.getByRole('searchbox', { name: 'Szukaj repozytorium' }),
      'acme',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Szukaj' }))

    await vi.waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/repositories/available?page=1&pageSize=20&search=acme',
        expect.anything(),
      ),
    )
  })

  it('adds repository by GitHub id and refreshes the lists', async () => {
    let added = false
    const fetchMock = mockApi((method, path) => {
      if (method === 'POST' && path === '/api/repositories') {
        added = true
        return jsonResponse({ ...trackedApi, id: 'new', githubRepoId: 11 }, 201)
      }
      if (
        added &&
        method === 'GET' &&
        path.startsWith('/api/repositories/available')
      )
        return availablePage([api, { ...hello, isTracked: true }])
      return undefined
    })
    renderPicker()

    await userEvent.click(
      await screen.findByRole('button', { name: 'Śledź octocat/hello' }),
    )

    const post = fetchMock.mock.calls.find(
      ([path, init]) => path === '/api/repositories' && init?.method === 'POST',
    )
    expect(post?.[1]?.body).toBe(JSON.stringify({ githubRepoId: 11 }))
    expect(
      await screen.findByRole('button', {
        name: 'Przestań śledzić octocat/hello',
      }),
    ).toBeInTheDocument()
  })

  it('removes tracked repository by its DevPulse id', async () => {
    const fetchMock = mockApi((method, path) =>
      method === 'DELETE' && path === `/api/repositories/${trackedApi.id}`
        ? new Response(null, { status: 204 })
        : undefined,
    )
    renderPicker()

    await userEvent.click(
      await screen.findByRole('button', { name: 'Przestań śledzić acme/api' }),
    )

    await vi.waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/api/repositories/${trackedApi.id}`,
        expect.objectContaining({ method: 'DELETE' }),
      ),
    )
  })

  it.each([
    ['conflict', 409, 'To repozytorium jest już śledzone.'],
    ['limit-exceeded', 422, 'Osiągnięto limit śledzonych repozytoriów'],
  ])('explains %s when adding fails', async (type, status, message) => {
    mockApi((method) => (method === 'POST' ? problem(type, status) : undefined))
    renderPicker()

    await userEvent.click(
      await screen.findByRole('button', { name: 'Śledź octocat/hello' }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(message)
  })

  it('shows retry time when GitHub budget is exhausted', async () => {
    mockApi((method, path) =>
      method === 'GET' && path.startsWith('/api/repositories/available')
        ? problem('github-rate-limited', 503, { 'Retry-After': '120' })
        : undefined,
    )

    renderPicker()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Wyczerpany limit zapytań do GitHuba. Spróbuj ponownie za 2 minuty.',
    )
  })

  it('asks to sign in again when GitHub token is invalid', async () => {
    mockApi((method, path) =>
      method === 'GET' && path.startsWith('/api/repositories/available')
        ? problem('github-token-invalid', 401)
        : undefined,
    )

    renderPicker()

    const alert = await screen.findByRole('alert')
    expect(
      within(alert).getByRole('link', { name: 'Zaloguj się ponownie' }),
    ).toHaveAttribute('href', '/api/auth/login')
  })
})
