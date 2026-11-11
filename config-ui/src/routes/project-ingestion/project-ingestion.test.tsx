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

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { message } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import API from '@/api';
import type { WorkstreamProject } from '@/api/workstream';
import { renderWithTheme } from '@/ui/__tests__/render-with-theme';

import { COPY } from './constants';
import { ProjectIngestion } from './project-ingestion';
import { filterProjects } from './utils';

vi.mock('antd', async (importOriginal) => {
  const { withMockedAntdMessage } = await import('../settings/__tests__/test-utils');
  return withMockedAntdMessage(importOriginal);
});

vi.mock('@/api', () => ({
  default: { workstream: { list: vi.fn(), load: vi.fn() } },
}));

const workstream = vi.mocked(API.workstream);

const project = (workstreamId: string, name: string, extra: Partial<WorkstreamProject> = {}): WorkstreamProject => ({
  workstreamId,
  name,
  teamLeadName: '',
  teamLeadEmail: '',
  lastLoadedAt: '2026-11-10T09:00:00Z',
  devlakeProject: '',
  ...extra,
});

const LINKED = project('7', 'DevLake', {
  teamLeadName: 'Lead One',
  teamLeadEmail: 'lead.one@arbisoft.com',
  devlakeProject: 'DevLake',
});
const UNLINKED = project('8', 'Sophia');

const setup = () =>
  renderWithTheme(
    <MemoryRouter>
      <ProjectIngestion />
    </MemoryRouter>,
  );

const table = () => screen.getByRole('table');

describe('ProjectIngestion', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    workstream.list.mockResolvedValue({ count: 2, projects: [LINKED, UNLINKED] });
  });

  it('lists projects with their team lead and DevLake link', async () => {
    setup();

    await waitFor(() => expect(within(table()).getByText('Lead One')).toBeTruthy());
    expect(within(table()).getByRole('link', { name: 'DevLake' }).getAttribute('href')).toContain('/projects/DevLake');
    expect(within(table()).getByText(COPY.notLinked)).toBeTruthy();
    expect(within(table()).getByText(COPY.noTeamLead)).toBeTruthy();
  });

  it('loads from Workstream, reports the counts and refreshes the list', async () => {
    workstream.load.mockResolvedValue({ loaded: 3, created: 1, updated: 2, skipped: 0 });
    setup();
    await waitFor(() => expect(workstream.list).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getAllByRole('button', { name: new RegExp(COPY.load) })[0]);

    await waitFor(() => expect(message.success).toHaveBeenCalledWith('Loaded 3 projects: 1 new, 2 updated.'));
    expect(workstream.load).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(workstream.list).toHaveBeenCalledTimes(2));
  });

  it("shows the server's error when loading fails", async () => {
    workstream.load.mockRejectedValue({
      response: { data: { message: 'loading from Workstream needs INTEGRATION_SERVICE_URL to be set' } },
    });
    setup();
    await waitFor(() => expect(workstream.list).toHaveBeenCalled());

    fireEvent.click(screen.getAllByRole('button', { name: new RegExp(COPY.load) })[0]);

    await waitFor(() =>
      expect(message.error).toHaveBeenCalledWith('loading from Workstream needs INTEGRATION_SERVICE_URL to be set'),
    );
    expect(workstream.list).toHaveBeenCalledTimes(1);
  });

  it('offers loading when nothing is loaded yet', async () => {
    workstream.list.mockResolvedValue({ count: 0, projects: [] });
    setup();

    await waitFor(() => expect(screen.getByText(COPY.empty.title)).toBeTruthy());
  });
});

describe('filterProjects', () => {
  it.each([
    ['devlake', ['7']],
    ['LEAD.ONE', ['7']],
    ['8', ['8']],
    ['  ', ['7', '8']],
    ['nothing', []],
  ])('keyword %j keeps %j', (keyword, ids) => {
    expect(filterProjects([LINKED, UNLINKED], keyword).map((p) => p.workstreamId)).toEqual(ids);
  });
});
