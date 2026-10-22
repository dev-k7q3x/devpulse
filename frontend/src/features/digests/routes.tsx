import type { RouteObject } from 'react-router'
import { DigestHistoryPage } from './DigestHistoryPage'

export const digestsRoutes: RouteObject[] = [
  { path: 'digests', element: <DigestHistoryPage /> },
]
