import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import {
  createMemoryRouter,
  RouterProvider,
  type RouteObject,
} from 'react-router'
import { vi } from 'vitest'

/** Router w pamięci + świeży QueryClient bez ponowień – każdy test ma własny cache. */
export function renderRoutes(routes: RouteObject[], path: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return { router, queryClient }
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type':
        status >= 400 ? 'application/problem+json' : 'application/json',
    },
  })
}

export const unauthenticatedResponse = () =>
  jsonResponse({ type: 'unauthenticated', status: 401 }, 401)

/** Atrapa fetch rozróżniająca żądania po metodzie i ścieżce. */
export function mockFetch(
  handler: (method: string, path: string) => Response | Promise<Response>,
) {
  const fetchMock = vi.fn<typeof fetch>((input, init) =>
    Promise.resolve(
      handler((init?.method ?? 'GET').toUpperCase(), String(input)),
    ),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
