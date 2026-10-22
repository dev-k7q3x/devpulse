import type { RouteObject } from 'react-router'
import { authRoutes } from '@/features/auth/routes'
import { dashboardRoutes } from '@/features/dashboard/routes'
import { digestsRoutes } from '@/features/digests/routes'
import { settingsRoutes } from '@/features/settings/routes'
import { AppLayout } from './AppLayout'

// Każdy feature eksportuje własne routes.tsx – ścieżki równoległe nie edytują tego pliku.
export const appRoutes: RouteObject[] = [
  ...authRoutes,
  {
    path: '/',
    element: <AppLayout />,
    children: [...dashboardRoutes, ...digestsRoutes, ...settingsRoutes],
  },
]
