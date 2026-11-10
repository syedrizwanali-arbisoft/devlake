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

import { THEME_MODE } from '@/theme/constants';
import type { ThemeMode } from '@/theme/tokens';

export const NAV_KEY = {
  PROJECTS: 'projects',
  CONNECTIONS: 'connections',
  ADVANCED: 'advanced',
  BLUEPRINTS: 'blueprints',
  PIPELINES: 'pipelines',
  API_KEYS: 'api-keys',
  PROJECT_INGESTION: 'project-ingestion',
  SETTINGS: 'settings',
  USERS: 'users',
  AUTHENTICATION: 'authentication',
  ACTIVITY: 'activity',
  RESOURCES: 'resources',
  DOCS: 'docs',
  API: 'api',
  GITHUB: 'github',
  SLACK: 'slack',
  DIVIDER: 'divider',
  DASHBOARDS: 'dashboards',
} as const;

export const ACCESS_NAV_KEYS: string[] = [NAV_KEY.SETTINGS, NAV_KEY.USERS, NAV_KEY.AUTHENTICATION, NAV_KEY.ACTIVITY];

export const ADMIN_NAV_KEYS: string[] = [NAV_KEY.API_KEYS, NAV_KEY.PROJECT_INGESTION];

export const COPYRIGHT_HIDDEN_NAV_KEYS: string[] = [NAV_KEY.DASHBOARDS, NAV_KEY.GITHUB, NAV_KEY.SLACK];

export const ACCOUNT_MENU_KEY = {
  VERSION: 'version',
  VERSION_DIVIDER: 'version-divider',
  THEME: 'theme',
  THEME_PREFIX: 'theme-',
  ACCOUNT_DIVIDER: 'account-divider',
  CHANGE_PASSWORD: 'change-password',
  LINK_IDENTITY_PREFIX: 'link-identity-',
  NO_LINKABLE_PROVIDERS: 'no-linkable-providers',
  LINKABLE_PROVIDERS_FAILED: 'linkable-providers-failed',
  SIGN_OUT_DIVIDER: 'sign-out-divider',
  SIGN_OUT: 'logout',
} as const;

export const FAILURE_COOLDOWN_MS = 10_000;

export const INIT_STATUS = { IDLE: 'idle', LOADING: 'loading', FAILED: 'failed' } as const;

export const LINK_IDENTITY = {
  PATH: '/auth/link-identity',
  PROVIDER_PARAM: 'provider',
  RETURN_URL_PARAM: 'return_url',
  RESULT_PARAM: 'identity_link',
  RESULT_LINKED: 'linked',
} as const;

export const COPY = {
  initializationFailed: 'The application could not be initialized. Reload and try again.',
  nav: {
    projects: 'Projects',
    connections: 'Connections',
    advanced: 'Advanced',
    blueprints: 'Blueprints',
    pipelines: 'Pipelines',
    apiKeys: 'API Keys',
    projectIngestion: 'Project Ingestion',
    settings: 'Settings',
    users: 'User Management',
    authentication: 'Authentication',
    activity: 'Recent Activities',
    resources: 'Resources',
    docs: 'Docs',
    api: 'API',
    github: 'GitHub',
    slack: 'Slack',
    dashboards: 'Dashboards',
  },
  account: {
    fallbackName: 'Account',
    version: (version: string) => `Version ${version}`,
    theme: 'Theme',
    themeLight: 'Light',
    themeDark: 'Dark',
    themeSystem: 'System',
    selected: 'Selected',
    changePassword: 'Change password',
    addProviderSignIn: (provider: string) => `Add ${provider} sign-in`,
    noProviders: 'No additional sign-in providers',
    providersUnavailable: 'Additional sign-in methods are unavailable',
    signOut: 'Sign out',
  },
  identityLink: {
    linked: 'Additional sign-in method added.',
    failed: 'The additional sign-in method could not be added. Please try again.',
  },
} as const;

export const THEME_LABEL: Record<ThemeMode, string> = {
  [THEME_MODE.LIGHT]: COPY.account.themeLight,
  [THEME_MODE.DARK]: COPY.account.themeDark,
  [THEME_MODE.SYSTEM]: COPY.account.themeSystem,
};
