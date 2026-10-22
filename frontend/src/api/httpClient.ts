/** RFC 9457 – kształt błędów API. `type` to stały slug (plan 03, tabela 0.1). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  [extension: string]: unknown
}

export class ProblemError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.title ?? `HTTP ${status}`)
    this.name = 'ProblemError'
    this.status = status
    this.problem = problem
  }
}

const XSRF_COOKIE = 'XSRF-TOKEN'
const XSRF_HEADER = 'X-XSRF-TOKEN'
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

function readCookie(name: string): string | undefined {
  const prefix = `${name}=`
  const cookie = document.cookie.split('; ').find((c) => c.startsWith(prefix))
  return cookie ? decodeURIComponent(cookie.slice(prefix.length)) : undefined
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  const contentType = response.headers.get('Content-Type') ?? ''
  if (contentType.includes('json')) {
    try {
      return (await response.json()) as ProblemDetails
    } catch {
      // Uszkodzone body – zwracamy minimalny problem poniżej.
    }
  }
  return { status: response.status, title: response.statusText }
}

/** Jedyny punkt wyjścia SPA do API: cookie sesyjne + nagłówek antiforgery dla mutacji. */
export async function request<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')

  if (MUTATING_METHODS.has(method)) {
    const xsrfToken = readCookie(XSRF_COOKIE)
    if (xsrfToken) {
      headers.set(XSRF_HEADER, xsrfToken)
    }
  }

  const response = await fetch(path, {
    ...init,
    method,
    headers,
    credentials: 'include',
  })

  if (!response.ok) {
    throw new ProblemError(response.status, await readProblem(response))
  }

  // 204, 201/202 bez treści albo odpowiedź nie-JSON – brak danych do sparsowania.
  const contentType = response.headers.get('Content-Type') ?? ''
  if (response.status === 204 || !contentType.includes('json')) {
    return undefined as T
  }
  const text = await response.text()
  return (text === '' ? undefined : JSON.parse(text)) as T
}

function withJsonBody(method: string, body: unknown): RequestInit {
  return body === undefined
    ? { method }
    : {
        method,
        body: JSON.stringify(body),
        headers: { 'Content-Type': 'application/json' },
      }
}

export const httpClient = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, withJsonBody('POST', body)),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, withJsonBody('PUT', body)),
  delete: <T = void>(path: string) => request<T>(path, { method: 'DELETE' }),
}
