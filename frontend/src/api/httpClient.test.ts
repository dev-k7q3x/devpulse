import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient, ProblemError } from './httpClient'

const fetchMock = vi.fn<typeof fetch>()

function jsonResponse(
  body: unknown,
  status = 200,
  contentType = 'application/json',
) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': contentType },
  })
}

function lastRequest() {
  const [path, init] = fetchMock.mock.calls[0]
  return { path, init: init ?? {}, headers: new Headers(init?.headers) }
}

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  document.cookie = 'XSRF-TOKEN=token%2Fvalue'
})

afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
  document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT'
})

describe('httpClient', () => {
  it('sends cookies and skips XSRF header on GET', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ok: true }))

    const result = await httpClient.get<{ ok: boolean }>('/api/something')

    const { path, init, headers } = lastRequest()
    expect(result).toEqual({ ok: true })
    expect(path).toBe('/api/something')
    expect(init.credentials).toBe('include')
    expect(headers.has('X-XSRF-TOKEN')).toBe(false)
  })

  it.each(['post', 'put', 'delete'] as const)(
    'adds decoded XSRF header on %s',
    async (method) => {
      fetchMock.mockResolvedValue(new Response(null, { status: 204 }))

      await httpClient[method]('/api/something')

      const { init, headers } = lastRequest()
      expect(init.method).toBe(method.toUpperCase())
      expect(headers.get('X-XSRF-TOKEN')).toBe('token/value')
    },
  )

  it('serializes JSON body on POST', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: '1' }, 201))

    await httpClient.post('/api/something', { name: 'repo' })

    const { init, headers } = lastRequest()
    expect(init.body).toBe('{"name":"repo"}')
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it.each([
    ['201 without body', new Response(null, { status: 201 })],
    [
      '202 with empty JSON body',
      new Response('', {
        status: 202,
        headers: { 'Content-Type': 'application/json' },
      }),
    ],
    [
      '200 with non-JSON body',
      new Response('OK', {
        status: 200,
        headers: { 'Content-Type': 'text/plain' },
      }),
    ],
  ])('returns undefined for %s', async (_, response) => {
    fetchMock.mockResolvedValue(response)

    const result = await httpClient.post('/api/something')

    expect(result).toBeUndefined()
  })

  it('throws ProblemError with problem type on error response', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(
        { type: 'unauthenticated', title: 'Unauthorized', status: 401 },
        401,
        'application/problem+json',
      ),
    )

    const error = await httpClient.get('/api/auth/me').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ProblemError)
    expect((error as ProblemError).status).toBe(401)
    expect((error as ProblemError).problem.type).toBe('unauthenticated')
  })

  it('exposes Retry-After seconds on rate-limited response', async () => {
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({ type: 'github-rate-limited', status: 503 }),
        {
          status: 503,
          headers: {
            'Content-Type': 'application/problem+json',
            'Retry-After': '91',
          },
        },
      ),
    )

    const error = await httpClient
      .get('/api/repositories/available')
      .catch((e: unknown) => e)

    expect((error as ProblemError).retryAfterSeconds).toBe(91)
  })

  it('throws ProblemError without type when error body is not JSON', async () => {
    fetchMock.mockResolvedValue(
      new Response('Bad gateway', { status: 502, statusText: 'Bad Gateway' }),
    )

    const error = await httpClient
      .get('/api/dashboard')
      .catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ProblemError)
    expect((error as ProblemError).problem).toEqual({
      status: 502,
      title: 'Bad Gateway',
    })
  })
})
