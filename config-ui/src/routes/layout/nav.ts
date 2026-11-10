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

import {
  ApiOutlined,
  CloudDownloadOutlined,
  BarChartOutlined,
  BookOutlined,
  BuildOutlined,
  ExperimentOutlined,
  GithubOutlined,
  HistoryOutlined,
  KeyOutlined,
  LockOutlined,
  PartitionOutlined,
  ProjectOutlined,
  SettingOutlined,
  SlackOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import { createElement } from 'react';

import { LINKS, PATHS } from '@/config';
import { canManageAccess, canManageAdminFeatures } from '@/routes/settings/guard';
import { NAV_ITEM_KIND, type NavItem } from '@/ui';

import { ACCESS_NAV_KEYS, ADMIN_NAV_KEYS, COPY, COPYRIGHT_HIDDEN_NAV_KEYS, NAV_KEY } from './constants';
import type { NavVisibility } from './types';

const isNavKeyVisible = (key: string, { access, copyrightHide }: NavVisibility) => {
  if (ACCESS_NAV_KEYS.includes(key)) return canManageAccess(access);
  if (ADMIN_NAV_KEYS.includes(key)) return canManageAdminFeatures(access);
  return !(copyrightHide && COPYRIGHT_HIDDEN_NAV_KEYS.includes(key));
};

export const getNavItems = (visibility: NavVisibility): NavItem[] => {
  const visible = (key: string) => isNavKeyVisible(key, visibility);

  return [
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.PROJECTS,
      label: COPY.nav.projects,
      icon: createElement(ProjectOutlined),
      path: PATHS.PROJECTS(),
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.PROJECT_INGESTION,
      label: COPY.nav.projectIngestion,
      icon: createElement(CloudDownloadOutlined),
      path: PATHS.PROJECT_INGESTION(),
      visible: visible(NAV_KEY.PROJECT_INGESTION),
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.CONNECTIONS,
      label: COPY.nav.connections,
      icon: createElement(ApiOutlined),
      path: PATHS.CONNECTIONS(),
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.ADVANCED,
      label: COPY.nav.advanced,
      icon: createElement(ExperimentOutlined),
      path: PATHS.BLUEPRINTS(),
      children: [
        {
          kind: NAV_ITEM_KIND.ROUTE,
          key: NAV_KEY.BLUEPRINTS,
          label: COPY.nav.blueprints,
          icon: createElement(BuildOutlined),
          path: PATHS.BLUEPRINTS(),
        },
        {
          kind: NAV_ITEM_KIND.ROUTE,
          key: NAV_KEY.PIPELINES,
          label: COPY.nav.pipelines,
          icon: createElement(PartitionOutlined),
          path: PATHS.PIPELINES(),
          // The empty id leaves a trailing slash, so any pipeline id matches by prefix.
          matchPaths: [PATHS.PIPELINE('')],
        },
      ],
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.API_KEYS,
      label: COPY.nav.apiKeys,
      icon: createElement(KeyOutlined),
      path: PATHS.APIKEYS(),
      visible: visible(NAV_KEY.API_KEYS),
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.SETTINGS,
      label: COPY.nav.settings,
      icon: createElement(SettingOutlined),
      path: PATHS.SETTINGS_USERS(),
      visible: visible(NAV_KEY.SETTINGS),
      children: [
        {
          kind: NAV_ITEM_KIND.ROUTE,
          key: NAV_KEY.USERS,
          label: COPY.nav.users,
          icon: createElement(TeamOutlined),
          path: PATHS.SETTINGS_USERS(),
          visible: visible(NAV_KEY.USERS),
        },
        {
          kind: NAV_ITEM_KIND.ROUTE,
          key: NAV_KEY.AUTHENTICATION,
          label: COPY.nav.authentication,
          icon: createElement(LockOutlined),
          path: PATHS.SETTINGS_AUTHENTICATION(),
          visible: visible(NAV_KEY.AUTHENTICATION),
        },
        {
          kind: NAV_ITEM_KIND.ROUTE,
          key: NAV_KEY.ACTIVITY,
          label: COPY.nav.activity,
          icon: createElement(HistoryOutlined),
          path: PATHS.SETTINGS_ACTIVITY(),
          visible: visible(NAV_KEY.ACTIVITY),
        },
      ],
    },
    {
      kind: NAV_ITEM_KIND.ROUTE,
      key: NAV_KEY.RESOURCES,
      label: COPY.nav.resources,
      icon: createElement(BookOutlined),
      path: PATHS.ROOT(),
      children: [
        {
          kind: NAV_ITEM_KIND.EXTERNAL,
          key: NAV_KEY.DOCS,
          label: COPY.nav.docs,
          icon: createElement(BookOutlined),
          href: LINKS.DOCS,
        },
        {
          kind: NAV_ITEM_KIND.EXTERNAL,
          key: NAV_KEY.API,
          label: COPY.nav.api,
          icon: createElement(ApiOutlined),
          href: LINKS.API,
        },
        {
          kind: NAV_ITEM_KIND.EXTERNAL,
          key: NAV_KEY.GITHUB,
          label: COPY.nav.github,
          icon: createElement(GithubOutlined),
          href: LINKS.GITHUB,
          visible: visible(NAV_KEY.GITHUB),
        },
        {
          kind: NAV_ITEM_KIND.EXTERNAL,
          key: NAV_KEY.SLACK,
          label: COPY.nav.slack,
          icon: createElement(SlackOutlined),
          href: LINKS.SLACK,
          visible: visible(NAV_KEY.SLACK),
        },
      ],
    },
    { kind: NAV_ITEM_KIND.DIVIDER, key: NAV_KEY.DIVIDER },
    {
      kind: NAV_ITEM_KIND.EXTERNAL,
      key: NAV_KEY.DASHBOARDS,
      label: COPY.nav.dashboards,
      icon: createElement(BarChartOutlined),
      href: PATHS.DASHBOARDS(),
      visible: visible(NAV_KEY.DASHBOARDS),
    },
  ];
};
