import type { RouteObject } from 'react-router'
import { authRoutes } from '@/features/auth/routes'
import { dashboardRoutes } from '@/features/dashboard/routes'
import { digestsRoutes } from '@/features/digests/routes'
import {
  settingsPublicRoutes,
  settingsRoutes,
} from '@/features/settings/routes'
import { AppLayout } from './AppLayout'
import { ProtectedRoute } from './ProtectedRoute'

// Każdy feature eksportuje własne routes.tsx – ścieżki równoległe nie edytują tego pliku.
export const appRoutes: RouteObject[] = [
  ...authRoutes,
  ...settingsPublicRoutes,
  {
    element: <ProtectedRoute />,
    children: [
      {
        path: '/',
        element: <AppLayout />,
        children: [...dashboardRoutes, ...digestsRoutes, ...settingsRoutes],
      },
    ],
  },
]
