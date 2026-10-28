import { createBrowserRouter } from 'react-router-dom'
import type { RouteObject } from 'react-router-dom'
import { HomePage } from '../../features/home/pages/home-page'
import { RootLayout } from '../layouts/root-layout'
import { NotFoundPage } from './not-found-page'
import { RouteErrorPage } from './route-error-page'

export const appRoutes: RouteObject[] = [{
  path: '/',
  element: <RootLayout />,
  errorElement: <RouteErrorPage />,
  children: [
    { index: true, element: <HomePage /> },
    { path: '*', element: <NotFoundPage /> },
  ],
}]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
