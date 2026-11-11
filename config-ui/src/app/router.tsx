/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom';

import { PageLoading } from '@/components';
import { BLUEPRINT_VIEW, PATHS, PROJECT_TAB, ROUTE_SEGMENTS } from '@/config';
import {
  DBMigrate,
  Onboard,
  Otel,
  ErrorPage,
  Layout,
  layoutLoader,
  Login,
  ChangePassword,
  Connections,
  Connection,
  ProjectHomePage,
  ProjectDetailPage,
  BlueprintHomePage,
  BlueprintDetailPage,
  BlueprintConnectionDetailPage,
  Pipelines,
  Pipeline,
  ApiKeys,
  ProjectIngestion,
  SettingsActivity,
  SettingsAuthentication,
  SettingsUserManagement,
  accessLoader,
  adminFeaturesLoader,
  NotFound,
  ShellNotFound,
  ParamRedirect,
} from '@/routes';

const PATH_PREFIX = import.meta.env.DEVLAKE_PATH_PREFIX ?? '';

const devRoutes: RouteObject[] = import.meta.env.DEV
  ? [
      {
        path: `${PATHS.UI_KIT()}/${ROUTE_SEGMENTS.WILDCARD}`,
        hydrateFallbackElement: <PageLoading />,
        lazy: async () => ({ Component: (await import('@/routes/ui-kit')).UiKit }),
      },
    ]
  : [];

const projectTabRoutes: RouteObject[] = Object.values(PROJECT_TAB).map((tab) => ({
  path: ROUTE_SEGMENTS.PROJECT_TAB(tab),
  element: <ProjectDetailPage />,
}));

const projectBlueprintViewRoutes: RouteObject[] = [BLUEPRINT_VIEW.CONFIGURATION].map((view) => ({
  path: ROUTE_SEGMENTS.PROJECT_BLUEPRINT_VIEW(view),
  element: <ProjectDetailPage />,
}));

const blueprintViewRoutes: RouteObject[] = [BLUEPRINT_VIEW.CONFIGURATION].map((view) => ({
  path: ROUTE_SEGMENTS.BLUEPRINT_VIEW(view),
  element: <BlueprintDetailPage />,
}));

export const routes: RouteObject[] = [
  {
    path: ROUTE_SEGMENTS.ROOT,
    element: <Navigate to={PATH_PREFIX ? PATH_PREFIX : PATHS.CONNECTIONS()} />,
  },
  {
    path: `${PATH_PREFIX}/${ROUTE_SEGMENTS.DB_MIGRATE}`,
    element: <DBMigrate />,
  },
  {
    path: `${PATH_PREFIX}/${ROUTE_SEGMENTS.LOGIN}`,
    element: <Login />,
  },
  {
    path: `${PATH_PREFIX}/${ROUTE_SEGMENTS.CHANGE_PASSWORD}`,
    element: <ChangePassword />,
  },
  {
    path: `${PATH_PREFIX}/${ROUTE_SEGMENTS.ONBOARD}`,
    element: <Onboard />,
  },
  {
    path: `${PATH_PREFIX}`,
    element: <Layout />,
    loader: layoutLoader,
    errorElement: <ErrorPage />,
    children: [
      {
        index: true,
        element: <Navigate to={ROUTE_SEGMENTS.PROJECTS} />,
      },
      {
        path: ROUTE_SEGMENTS.PROJECTS,
        element: <ProjectHomePage />,
      },
      {
        path: ROUTE_SEGMENTS.PROJECT,
        element: <ParamRedirect to={({ pname = '' }) => PATHS.PROJECT_TAB(pname, PROJECT_TAB.BLUEPRINT)} />,
      },
      ...projectTabRoutes,
      ...projectBlueprintViewRoutes,
      {
        path: ROUTE_SEGMENTS.PROJECT_CONNECTION,
        element: <BlueprintConnectionDetailPage />,
      },
      {
        path: ROUTE_SEGMENTS.PROJECT_CONNECTION_LEGACY,
        element: (
          <ParamRedirect to={({ pname = '', unique = '' }) => PATHS.PROJECT_BLUEPRINT_CONNECTION(pname, unique)} />
        ),
      },
      {
        path: ROUTE_SEGMENTS.CONNECTIONS,
        element: <Connections />,
      },
      {
        path: ROUTE_SEGMENTS.CONNECTION,
        element: <Connection />,
      },
      {
        path: ROUTE_SEGMENTS.ADVANCED,
        children: [
          {
            path: ROUTE_SEGMENTS.BLUEPRINTS,
            element: <BlueprintHomePage />,
          },
          {
            path: ROUTE_SEGMENTS.BLUEPRINT,
            element: <BlueprintDetailPage />,
          },
          ...blueprintViewRoutes,
          {
            path: ROUTE_SEGMENTS.BLUEPRINT_CONNECTION,
            element: <BlueprintConnectionDetailPage />,
          },
          {
            path: ROUTE_SEGMENTS.BLUEPRINT_CONNECTION_LEGACY,
            element: (
              <ParamRedirect to={({ bid = '', unique = '' }) => PATHS.BLUEPRINT_CONNECTION_UNIQUE(bid, unique)} />
            ),
          },
          {
            path: ROUTE_SEGMENTS.PIPELINES,
            element: <Pipelines />,
          },
          {
            path: ROUTE_SEGMENTS.PIPELINE,
            element: <Pipeline />,
          },
        ],
      },
      {
        path: ROUTE_SEGMENTS.KEYS,
        loader: adminFeaturesLoader,
        element: <ApiKeys />,
      },
      {
        path: ROUTE_SEGMENTS.PROJECT_INGESTION,
        loader: adminFeaturesLoader,
        element: <ProjectIngestion />,
      },
      {
        path: ROUTE_SEGMENTS.ACCESS,
        element: <ParamRedirect to={PATHS.SETTINGS_USERS} />,
      },
      {
        path: ROUTE_SEGMENTS.SETTINGS,
        loader: accessLoader,
        children: [
          {
            index: true,
            element: <ParamRedirect to={PATHS.SETTINGS_USERS} />,
          },
          {
            path: ROUTE_SEGMENTS.SETTINGS_USERS,
            element: <SettingsUserManagement />,
          },
          {
            path: ROUTE_SEGMENTS.SETTINGS_GRAFANA_USERS,
            element: <SettingsUserManagement />,
          },
          {
            path: ROUTE_SEGMENTS.SETTINGS_AUTHENTICATION,
            element: <SettingsAuthentication />,
          },
          {
            path: ROUTE_SEGMENTS.SETTINGS_ACTIVITY,
            element: <SettingsActivity />,
          },
          {
            path: ROUTE_SEGMENTS.WILDCARD,
            element: <ShellNotFound />,
          },
        ],
      },
      {
        path: ROUTE_SEGMENTS.OTEL,
        element: <Otel />,
      },
      {
        path: ROUTE_SEGMENTS.WILDCARD,
        element: <ShellNotFound />,
      },
    ],
  },
  ...devRoutes,
  {
    path: ROUTE_SEGMENTS.WILDCARD,
    element: <NotFound />,
  },
];

export const router = createBrowserRouter(routes);
