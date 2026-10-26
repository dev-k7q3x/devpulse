import type { RouteObject } from 'react-router'
import { DigestDetailPage } from './DigestDetailPage'
import { DigestHistoryPage } from './DigestHistoryPage'

export const digestsRoutes: RouteObject[] = [
  { path: 'digests', element: <DigestHistoryPage /> },
  { path: 'digests/:id', element: <DigestDetailPage /> },
]
