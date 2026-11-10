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

import type { TableColumnsType } from 'antd';
import { Link } from 'react-router-dom';

import type { WorkstreamProject } from '@/api/workstream';
import { PATHS } from '@/config';
import { formatTime } from '@/utils';

import { COLUMN, COPY, DATE_FORMAT } from './constants';
import { Muted, SubLine } from './styled';

export const getColumns = (): TableColumnsType<WorkstreamProject> => [
  {
    key: COLUMN.NAME,
    title: COPY.columns.name,
    render: (_, project) => (
      <span>
        {project.name || project.workstreamId}
        <SubLine>{project.workstreamId}</SubLine>
      </span>
    ),
  },
  {
    key: COLUMN.TEAM_LEAD,
    title: COPY.columns.teamLead,
    render: (_, project) =>
      project.teamLeadName || project.teamLeadEmail ? (
        <span>
          {project.teamLeadName}
          {project.teamLeadEmail && <SubLine>{project.teamLeadEmail}</SubLine>}
        </span>
      ) : (
        <Muted>{COPY.noTeamLead}</Muted>
      ),
  },
  {
    key: COLUMN.DEVLAKE_PROJECT,
    title: COPY.columns.devlakeProject,
    render: (_, project) =>
      project.devlakeProject ? (
        <Link to={PATHS.PROJECT(project.devlakeProject)}>{project.devlakeProject}</Link>
      ) : (
        <Muted>{COPY.notLinked}</Muted>
      ),
  },
  {
    key: COLUMN.LAST_LOADED,
    title: COPY.columns.lastLoaded,
    render: (_, project) => (project.lastLoadedAt ? formatTime(project.lastLoadedAt, DATE_FORMAT) : COPY.never),
  },
];
