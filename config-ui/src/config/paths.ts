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

import { encodeName } from '@/routes/project/utils';

import type { BlueprintView, ProjectTab } from './types';

const PATH_PREFIX = import.meta.env.DEVLAKE_PATH_PREFIX ?? '';

export const ROUTE_SEGMENTS = {
  ROOT: '/',
  WILDCARD: '*',
  DB_MIGRATE: 'db-migrate',
  LOGIN: 'login',
  CHANGE_PASSWORD: 'change-password',
  ONBOARD: 'onboard',
  PROJECTS: 'projects',
  PROJECT: 'projects/:pname',
  PROJECT_TAB: (tab: ProjectTab) => `projects/:pname/${tab}`,
  PROJECT_BLUEPRINT_VIEW: (view: BlueprintView) => `projects/:pname/blueprint/${view}`,
  PROJECT_CONNECTION: 'projects/:pname/blueprint/connections/:unique',
  PROJECT_CONNECTION_LEGACY: 'projects/:pname/:unique',
  CONNECTIONS: 'connections',
  CONNECTION: 'connections/:plugin/:id',
  ADVANCED: 'advanced',
  BLUEPRINTS: 'blueprints',
  BLUEPRINT: 'blueprints/:id',
  BLUEPRINT_VIEW: (view: BlueprintView) => `blueprints/:id/${view}`,
  BLUEPRINT_CONNECTION: 'blueprints/:bid/connections/:unique',
  BLUEPRINT_CONNECTION_LEGACY: 'blueprints/:bid/:unique',
  PIPELINES: 'pipelines',
  PIPELINE: 'pipeline/:id',
  KEYS: 'keys',
  PROJECT_INGESTION: 'project-ingestion',
  ACCESS: 'access',
  OTEL: 'otel',
  SETTINGS: 'settings',
  SETTINGS_USERS: 'users',
  SETTINGS_GRAFANA_USERS: 'users/grafana',
  SETTINGS_AUTHENTICATION: 'authentication',
  SETTINGS_ACTIVITY: 'activity',
} as const;

export const PATHS = {
  ROOT: () => `${PATH_PREFIX}/`,
  LOGIN: () => `${PATH_PREFIX}/login`,
  CHANGE_PASSWORD: () => `${PATH_PREFIX}/change-password`,
  ONBOARD: () => `${PATH_PREFIX}/onboard`,
  DB_MIGRATE: () => `${PATH_PREFIX}/db-migrate`,
  CONNECTIONS: () => `${PATH_PREFIX}/connections`,
  CONNECTION: (plugin: string, connectionId: ID) => `${PATH_PREFIX}/connections/${plugin}/${connectionId}`,
  PROJECTS: () => `${PATH_PREFIX}/projects`,
  PROJECT: (pname: string) => `${PATH_PREFIX}/projects/${encodeName(pname)}`,
  PROJECT_TAB: (pname: string, tab: ProjectTab) => `${PATH_PREFIX}/projects/${encodeName(pname)}/${tab}`,
  PROJECT_BLUEPRINT_VIEW: (pname: string, view: BlueprintView) =>
    `${PATH_PREFIX}/projects/${encodeName(pname)}/blueprint/${view}`,
  PROJECT_BLUEPRINT_CONNECTION: (pname: string, unique: string) =>
    `${PATH_PREFIX}/projects/${encodeName(pname)}/blueprint/connections/${unique}`,
  PROJECT_CONNECTION: (pname: string, plugin: string, connectionId: ID) =>
    `${PATH_PREFIX}/projects/${encodeName(pname)}/blueprint/connections/${plugin}-${connectionId}`,
  BLUEPRINTS: () => `${PATH_PREFIX}/advanced/blueprints`,
  BLUEPRINT: (bid: ID) => `${PATH_PREFIX}/advanced/blueprints/${bid}`,
  BLUEPRINT_VIEW: (bid: ID, view: BlueprintView) => `${PATH_PREFIX}/advanced/blueprints/${bid}/${view}`,
  BLUEPRINT_CONNECTION_UNIQUE: (bid: ID, unique: string) =>
    `${PATH_PREFIX}/advanced/blueprints/${bid}/connections/${unique}`,
  BLUEPRINT_CONNECTION: (bid: ID, plugin: string, connectionId: ID) =>
    `${PATH_PREFIX}/advanced/blueprints/${bid}/connections/${plugin}-${connectionId}`,
  PIPELINES: () => `${PATH_PREFIX}/advanced/pipelines`,
  PIPELINE: (id: ID) => `${PATH_PREFIX}/advanced/pipeline/${id}`,
  OTEL: () => `${PATH_PREFIX}/otel`,
  APIKEYS: () => `${PATH_PREFIX}/keys`,
  PROJECT_INGESTION: () => `${PATH_PREFIX}/project-ingestion`,
  SETTINGS_USERS: () => `${PATH_PREFIX}/settings/users`,
  SETTINGS_GRAFANA_USERS: () => `${PATH_PREFIX}/settings/users/grafana`,
  SETTINGS_AUTHENTICATION: () => `${PATH_PREFIX}/settings/authentication`,
  SETTINGS_ACTIVITY: () => `${PATH_PREFIX}/settings/activity`,
  DASHBOARDS: () => '/api/access/grafana-login',
  // Dev only: the path is empty in production builds so the string never ships.
  UI_KIT: () => (import.meta.env.DEV ? `${PATH_PREFIX}/ui-kit` : ''),
};
