import type { RouteObject } from 'react-router'
import { LoginPage } from './LoginPage'

export const authRoutes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
]
