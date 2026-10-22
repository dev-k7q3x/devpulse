import { NavLink, Outlet } from 'react-router'

export function AppLayout() {
  return (
    <div className="app-layout">
      <header className="app-header">
        <span className="app-brand">DevPulse</span>
        <nav aria-label="Główna nawigacja">
          <NavLink to="/" end>
            Dashboard
          </NavLink>
          <NavLink to="/digests">Historia digestów</NavLink>
          <NavLink to="/settings">Ustawienia</NavLink>
        </nav>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
