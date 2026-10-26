import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { appRoutes } from '@/app/routes'
import {
  jsonResponse,
  mockFetch,
  renderRoutes,
} from '@/test/renderWithProviders'
import type { DigestDetail } from './useDigests'
import {
  currentUser,
  detailOf,
  failedDigest,
  sentDigest,
} from './digestTestData'

function mockApi(id: string, response: Response) {
  return mockFetch((_method, path) => {
    if (path === '/api/auth/me') return jsonResponse(currentUser)
    if (path === `/api/digests/${id}`) return response
    return new Response(null, { status: 500 })
  })
}

function renderDetail(detail: DigestDetail) {
  mockApi(detail.id, jsonResponse(detail))
  renderRoutes(appRoutes, `/digests/${detail.id}`)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DigestDetailPage', () => {
  it('renders preview only in a sandboxed iframe', async () => {
    renderDetail(detailOf(sentDigest))

    const preview = await screen.findByTitle('Podgląd digestu z 21.10.2026')
    expect(preview.tagName).toBe('IFRAME')
    expect(preview).toHaveAttribute('sandbox', '')
    expect(preview).toHaveAttribute('src', `/api/digests/${sentDigest.id}/html`)
  })

  it('shows priorities with severity and AI source', async () => {
    renderDetail(detailOf(sentDigest))

    expect(
      await screen.findByRole('heading', { name: 'Priorytety AI' }),
    ).toBeInTheDocument()
    const items = screen.getAllByRole('listitem')
    expect(items[0]).toHaveTextContent(
      'Pilne · octocat/hello-world: CI na main nie przechodzi od wczoraj.',
    )
    expect(screen.getByText('Do: octocat@example.com')).toBeInTheDocument()
  })

  it('shows safe error and no preview for failed digest without content', async () => {
    renderDetail(
      detailOf(failedDigest, {
        error: 'Nie udało się wysłać digestu.',
        hasHtml: false,
      }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Nie udało się wysłać digestu.',
    )
    expect(
      screen.getByText('Ten digest nie ma treści do podglądu.'),
    ).toBeInTheDocument()
    expect(screen.queryByTitle(/Podgląd digestu/)).not.toBeInTheDocument()
  })

  it('explains missing digest on 404', async () => {
    mockApi(
      sentDigest.id,
      jsonResponse({ type: 'not-found', status: 404 }, 404),
    )

    renderRoutes(appRoutes, `/digests/${sentDigest.id}`)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Nie znaleziono digestu',
    )
  })
})
