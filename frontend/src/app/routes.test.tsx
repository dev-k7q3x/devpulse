import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { appRoutes } from './routes'

function renderAt(path: string) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  render(<RouterProvider router={router} />)
}

describe('appRoutes', () => {
  it.each([
    ['/', 'Dashboard'],
    ['/settings', 'Ustawienia'],
    ['/digests', 'Historia digestów'],
  ])('renders %s page inside app layout', (path, heading) => {
    renderAt(path)

    expect(
      screen.getByRole('navigation', { name: 'Główna nawigacja' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: heading })).toBeInTheDocument()
  })

  it('renders login page without app navigation', () => {
    renderAt('/login')

    expect(
      screen.getByRole('heading', { name: 'Zaloguj się' }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('navigation', { name: 'Główna nawigacja' }),
    ).not.toBeInTheDocument()
  })
})
