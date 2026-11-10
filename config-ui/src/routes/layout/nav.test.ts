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

import { describe, expect, it } from 'vitest';

// Load the routes barrel first so the config/routes import cycle resolves as it does in the app.
import '@/routes';

import { ACCESS_ROLE, type AccessCurrent } from '@/api/access';
import { LINKS, PATHS } from '@/config';
import { NAV_ITEM_KIND, type NavItem } from '@/ui';
import { findActiveKey, visibleItems } from '@/ui/sidebar-nav/utils';

import { COPY, NAV_KEY } from './constants';
import { getNavItems } from './nav';

const ADMIN: AccessCurrent = { enabled: true, role: ACCESS_ROLE.CUSTOMER_ADMIN };
const MEMBER: AccessCurrent = { enabled: true, role: ACCESS_ROLE.MEMBER };

const SETTINGS_CHILD_KEYS: string[] = [NAV_KEY.USERS, NAV_KEY.AUTHENTICATION, NAV_KEY.ACTIVITY];

const flatten = (items: NavItem[]): NavItem[] =>
  items.flatMap((item) =>
    item.kind === NAV_ITEM_KIND.ROUTE && item.children ? [item, ...flatten(item.children)] : [item],
  );

const visibleKeys = (access: AccessCurrent | null, copyrightHide = false) =>
  flatten(visibleItems(getNavItems({ access, copyrightHide }))).map((item) => item.key);

const activeKey = (pathname: string, access: AccessCurrent | null = ADMIN) =>
  findActiveKey(visibleItems(getNavItems({ access, copyrightHide: false })), pathname);

describe('getNavItems', () => {
  it('orders the sidebar as Projects, Project Ingestion, Connections, Advanced, API Keys, Settings, Resources, divider, Dashboards', () => {
    const top = visibleItems(getNavItems({ access: ADMIN, copyrightHide: false })).map((item) => item.key);
    expect(top).toEqual([
      NAV_KEY.PROJECTS,
      NAV_KEY.PROJECT_INGESTION,
      NAV_KEY.CONNECTIONS,
      NAV_KEY.ADVANCED,
      NAV_KEY.API_KEYS,
      NAV_KEY.SETTINGS,
      NAV_KEY.RESOURCES,
      NAV_KEY.DIVIDER,
      NAV_KEY.DASHBOARDS,
    ]);
  });

  it('nests Blueprints and Pipelines under Advanced, and the four resource links under Resources', () => {
    const items = getNavItems({ access: ADMIN, copyrightHide: false });
    const childKeys = (key: string) => {
      const group = items.find((item) => item.key === key);
      return group?.kind === NAV_ITEM_KIND.ROUTE ? group.children?.map((child) => child.key) : undefined;
    };
    expect(childKeys(NAV_KEY.ADVANCED)).toEqual([NAV_KEY.BLUEPRINTS, NAV_KEY.PIPELINES]);
    expect(childKeys(NAV_KEY.RESOURCES)).toEqual([NAV_KEY.DOCS, NAV_KEY.API, NAV_KEY.GITHUB, NAV_KEY.SLACK]);
  });

  it('shows Settings and Users only to a customer administrator', () => {
    expect(visibleKeys(ADMIN)).toEqual(expect.arrayContaining([NAV_KEY.SETTINGS, NAV_KEY.USERS]));
    [MEMBER, { enabled: false }, null].forEach((access) => {
      const keys = visibleKeys(access);
      expect(keys).not.toContain(NAV_KEY.SETTINGS);
      expect(keys).not.toContain(NAV_KEY.USERS);
      expect(keys).toContain(NAV_KEY.PROJECTS);
    });
  });

  it('shows API Keys to an administrator and when access is disabled, but not to a member or a failed lookup', () => {
    [ADMIN, { enabled: false }].forEach((access) => expect(visibleKeys(access)).toContain(NAV_KEY.API_KEYS));
    [MEMBER, null].forEach((access) => expect(visibleKeys(access)).not.toContain(NAV_KEY.API_KEYS));
    [ADMIN, { enabled: false }].forEach((access) => expect(visibleKeys(access)).toContain(NAV_KEY.PROJECT_INGESTION));
    [MEMBER, null].forEach((access) => expect(visibleKeys(access)).not.toContain(NAV_KEY.PROJECT_INGESTION));
  });

  it('lists Users, Authentication and Recent Activities under Settings, in that order', () => {
    const items = getNavItems({ access: ADMIN, copyrightHide: false });
    const settings = items.find((item) => item.key === NAV_KEY.SETTINGS);
    expect(settings?.kind === NAV_ITEM_KIND.ROUTE && settings.children?.map((child) => child.key)).toEqual([
      NAV_KEY.USERS,
      NAV_KEY.AUTHENTICATION,
      NAV_KEY.ACTIVITY,
    ]);
    const children = flatten(items).filter((item) => SETTINGS_CHILD_KEYS.includes(item.key));
    expect(children.map((item) => (item.kind === NAV_ITEM_KIND.ROUTE ? item.label : ''))).toEqual([
      COPY.nav.users,
      COPY.nav.authentication,
      COPY.nav.activity,
    ]);
  });

  it('shows Authentication and Recent Activities only to a customer administrator', () => {
    expect(visibleKeys(ADMIN)).toEqual(expect.arrayContaining([NAV_KEY.AUTHENTICATION, NAV_KEY.ACTIVITY]));
    [MEMBER, { enabled: false }, null].forEach((access) => {
      const keys = visibleKeys(access);
      expect(keys).not.toContain(NAV_KEY.AUTHENTICATION);
      expect(keys).not.toContain(NAV_KEY.ACTIVITY);
    });
  });

  it('opens the new Settings pages from their paths', () => {
    expect(activeKey(PATHS.SETTINGS_AUTHENTICATION())).toBe(NAV_KEY.AUTHENTICATION);
    expect(activeKey(PATHS.SETTINGS_ACTIVITY())).toBe(NAV_KEY.ACTIVITY);
  });

  it('hides Dashboards, GitHub and Slack, and the divider before them, when the copyright is hidden', () => {
    const keys = visibleKeys(ADMIN, true);
    [NAV_KEY.DASHBOARDS, NAV_KEY.GITHUB, NAV_KEY.SLACK, NAV_KEY.DIVIDER].forEach((key) =>
      expect(keys).not.toContain(key),
    );
    expect(keys).toEqual(expect.arrayContaining([NAV_KEY.DOCS, NAV_KEY.API, NAV_KEY.RESOURCES]));
  });

  it('uses the configured links, opens external items by href and labels from COPY', () => {
    const external = flatten(getNavItems({ access: ADMIN, copyrightHide: false })).filter(
      (item) => item.kind === NAV_ITEM_KIND.EXTERNAL,
    );
    expect(external.map((item) => (item.kind === NAV_ITEM_KIND.EXTERNAL ? item.href : ''))).toEqual([
      LINKS.DOCS,
      LINKS.API,
      LINKS.GITHUB,
      LINKS.SLACK,
      PATHS.DASHBOARDS(),
    ]);
    expect(external.map((item) => (item.kind === NAV_ITEM_KIND.EXTERNAL ? item.label : ''))).toEqual([
      COPY.nav.docs,
      COPY.nav.api,
      COPY.nav.github,
      COPY.nav.slack,
      COPY.nav.dashboards,
    ]);
  });
});

describe('active item', () => {
  it.each([
    [PATHS.PROJECTS(), NAV_KEY.PROJECTS],
    [PATHS.PROJECT_TAB('my project', 'webhooks'), NAV_KEY.PROJECTS],
    [PATHS.PROJECT_BLUEPRINT_CONNECTION('x', 'github-1'), NAV_KEY.PROJECTS],
    [PATHS.CONNECTIONS(), NAV_KEY.CONNECTIONS],
    [PATHS.CONNECTION('github', 1), NAV_KEY.CONNECTIONS],
    [PATHS.BLUEPRINTS(), NAV_KEY.BLUEPRINTS],
    [PATHS.BLUEPRINT(3), NAV_KEY.BLUEPRINTS],
    [PATHS.BLUEPRINT_CONNECTION(3, 'github', 1), NAV_KEY.BLUEPRINTS],
    [PATHS.PIPELINES(), NAV_KEY.PIPELINES],
    [PATHS.PIPELINE(3), NAV_KEY.PIPELINES],
    [PATHS.APIKEYS(), NAV_KEY.API_KEYS],
    [PATHS.SETTINGS_USERS(), NAV_KEY.USERS],
    [PATHS.SETTINGS_GRAFANA_USERS(), NAV_KEY.USERS],
  ])('highlights the right item for %s', (pathname, key) => {
    expect(activeKey(pathname)).toBe(key);
  });

  it('highlights nothing for a path outside the sidebar, or Users when Settings is hidden', () => {
    expect(activeKey(PATHS.OTEL())).toBeUndefined();
    expect(activeKey(PATHS.SETTINGS_USERS(), MEMBER)).toBeUndefined();
  });

  it('does not treat a longer sibling name as a nested path', () => {
    expect(activeKey('/projectsx')).toBeUndefined();
  });
});
