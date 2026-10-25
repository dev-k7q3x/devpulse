import type { RouteObject } from 'react-router'
import { ConfirmEmailPage } from './ConfirmEmailPage'
import { SettingsPage } from './SettingsPage'

/** Wewnątrz ProtectedRoute i AppLayout. */
export const settingsRoutes: RouteObject[] = [
  { path: 'settings', element: <SettingsPage /> },
]

/** Bez logowania – link z maila może zostać otwarty na innym urządzeniu. */
export const settingsPublicRoutes: RouteObject[] = [
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
]
