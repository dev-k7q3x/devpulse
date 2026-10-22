import { createBrowserRouter, RouterProvider } from 'react-router'
import { AppProviders } from '@/app/AppProviders'
import { appRoutes } from '@/app/routes'

const router = createBrowserRouter(appRoutes)

export default function App() {
  return (
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  )
}
