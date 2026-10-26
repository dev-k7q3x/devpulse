import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
} from '@/test/renderWithProviders'
import type { Settings } from './useSettings'

const user = {
  id: '0192f0c4-0000-7000-8000-000000000001',
  login: 'octocat',
  avatarUrl: null,
  githubTokenValid: true,
  missingScopes: [],
  isAdmin: false,
}

const settings: Settings = {
  email: 'octocat@example.com',
  emailConfirmed: true,
  pendingEmail: null,
  pendingEmailExpiresAt: null,
  sendHour: 7,
  timeZoneId: 'Europe/Warsaw',
  digestEnabled: true,
  nextDigestAt: '2026-10-25T06:00:00Z',
}

type Handler = (
  method: string,
  path: string,
  body: unknown,
) => Response | undefined

/** /me i GET ustawień obsłużone domyślnie; `handler` nadpisuje pozostałe żądania. */
function mockApi(handler: Handler = () => undefined, initial = settings) {
  const fetchMock = mockFetch(() => new Response(null, { status: 500 }))
  fetchMock.mockImplementation((input, init) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const path = String(input)
    const body: unknown =
      typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
    const custom = handler(method, path, body)
    if (custom) return Promise.resolve(custom)
    if (path === '/api/auth/me') return Promise.resolve(jsonResponse(user))
    if (method === 'GET' && path === '/api/settings')
      return Promise.resolve(jsonResponse(initial))
    // Wybór repozytoriów (E4) osadzony na stronie – pusta lista, żeby nie zgłaszał własnych błędów.
    if (method === 'GET' && path === '/api/repositories')
      return Promise.resolve(jsonResponse([]))
    if (method === 'GET' && path.startsWith('/api/repositories/available'))
      return Promise.resolve(
        jsonResponse({
          items: [],
          page: 1,
          pageSize: 20,
          totalCount: 0,
          fetchedAt: '2026-10-25T06:00:00Z',
        }),
      )
    return Promise.resolve(new Response(null, { status: 500 }))
  })
  return fetchMock
}

function requestsTo(fetchMock: ReturnType<typeof mockApi>, method: string) {
  return fetchMock.mock.calls.filter(
    ([, init]) => (init?.method ?? 'GET').toUpperCase() === method,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('SettingsPage', () => {
  it('shows current schedule, email and next digest', async () => {
    mockApi()

    renderRoutes(appRoutes, '/settings')

    expect(
      await screen.findByText(/Następny digest: 25 paź 2026, 07:00/),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Godzina wysyłki')).toHaveValue('7')
    expect(screen.getByLabelText('Strefa czasowa')).toHaveValue('Europe/Warsaw')
    expect(screen.getByLabelText('Wysyłaj codzienny digest')).toBeChecked()
    expect(screen.getByText('octocat@example.com')).toBeInTheDocument()
  })

  it('saves schedule with PUT and shows confirmation', async () => {
    const fetchMock = mockApi((method, path, body) =>
      method === 'PUT' && path === '/api/settings'
        ? jsonResponse({ ...settings, ...(body as object) })
        : undefined,
    )
    renderRoutes(appRoutes, '/settings')

    await userEvent.selectOptions(
      await screen.findByLabelText('Godzina wysyłki'),
      '21',
    )
    await userEvent.click(screen.getByLabelText('Wysyłaj codzienny digest'))
    await userEvent.click(screen.getByRole('button', { name: 'Zapisz' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Zapisano ustawienia.',
    )
    const [[, init]] = requestsTo(fetchMock, 'PUT')
    expect(JSON.parse(String(init?.body))).toEqual({
      sendHour: 21,
      timeZoneId: 'Europe/Warsaw',
      digestEnabled: false,
    })
  })

  it('shows validation message from the API', async () => {
    mockApi((method) =>
      method === 'PUT'
        ? jsonResponse(
            {
              type: 'validation',
              status: 400,
              errors: {
                timeZoneId: [
                  'Nieznana strefa czasowa IANA (np. Europe/Warsaw).',
                ],
              },
            },
            400,
          )
        : undefined,
    )
    renderRoutes(appRoutes, '/settings')

    await userEvent.click(await screen.findByRole('button', { name: 'Zapisz' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Nieznana strefa czasowa IANA',
    )
  })

  it('proposes browser time zone for a new account', async () => {
    vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({
      ...new Intl.DateTimeFormat().resolvedOptions(),
      timeZone: 'America/New_York',
    })
    mockApi(undefined, { ...settings, timeZoneId: 'UTC' })

    renderRoutes(appRoutes, '/settings')

    expect(await screen.findByLabelText('Strefa czasowa')).toHaveValue(
      'America/New_York',
    )
    expect(screen.getByText(/strefę z Twojej przeglądarki/)).toBeInTheDocument()
  })

  it('starts email change and shows pending address', async () => {
    const fetchMock = mockApi((method, path, body) =>
      method === 'POST' && path === '/api/settings/email'
        ? jsonResponse(
            {
              ...settings,
              pendingEmail: (body as { email: string }).email,
              pendingEmailExpiresAt: '2026-10-25T10:00:00Z',
            },
            202,
          )
        : undefined,
    )
    renderRoutes(appRoutes, '/settings')

    await userEvent.type(
      await screen.findByLabelText('Nowy adres email'),
      'new@example.com',
    )
    await userEvent.click(
      screen.getByRole('button', { name: 'Wyślij link potwierdzający' }),
    )

    expect(await screen.findByText('new@example.com')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Anuluj zmianę' }),
    ).toBeInTheDocument()
    expect(requestsTo(fetchMock, 'POST')).toHaveLength(1)
  })

  it('explains conflict when new email is the current one', async () => {
    mockApi((method) =>
      method === 'POST'
        ? jsonResponse(
            {
              type: 'conflict',
              status: 409,
              detail: 'Ten adres jest już aktualnym adresem digestu.',
            },
            409,
          )
        : undefined,
    )
    renderRoutes(appRoutes, '/settings')

    await userEvent.type(
      await screen.findByLabelText('Nowy adres email'),
      'octocat@example.com',
    )
    await userEvent.click(
      screen.getByRole('button', { name: 'Wyślij link potwierdzający' }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ten adres jest już aktualnym adresem digestu.',
    )
  })

  it('cancels pending email change with DELETE', async () => {
    let cancelled = false
    const fetchMock = mockApi(
      (method, path) => {
        if (method === 'DELETE' && path === '/api/settings/email/pending') {
          cancelled = true
          return new Response(null, { status: 204 })
        }
        if (method === 'GET' && path === '/api/settings' && cancelled) {
          return jsonResponse(settings)
        }
        return undefined
      },
      {
        ...settings,
        pendingEmail: 'pending@example.com',
        pendingEmailExpiresAt: '2026-10-25T10:00:00Z',
      },
    )
    renderRoutes(appRoutes, '/settings')

    const pending = await screen.findByText('pending@example.com')
    await userEvent.click(
      within(pending.closest('div')!).getByRole('button', {
        name: 'Anuluj zmianę',
      }),
    )

    expect(await screen.findByLabelText('Nowy adres email')).toBeInTheDocument()
    await vi.waitFor(() =>
      expect(screen.queryByText('pending@example.com')).not.toBeInTheDocument(),
    )
    expect(requestsTo(fetchMock, 'DELETE')).toHaveLength(1)
  })

  it('warns when there is no confirmed email', async () => {
    mockApi(undefined, {
      ...settings,
      email: null,
      emailConfirmed: false,
      nextDigestAt: null,
    })

    renderRoutes(appRoutes, '/settings')

    expect(
      await screen.findByText(/Brak potwierdzonego adresu/),
    ).toBeInTheDocument()
    expect(screen.getByText('Digest nie jest zaplanowany.')).toBeInTheDocument()
  })
})
