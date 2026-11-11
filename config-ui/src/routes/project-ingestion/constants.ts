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

export const COPY = {
  title: 'Project ingestion',
  description:
    'Load projects and their team leads from Workstream through the Integration Service. Loading again updates the projects already loaded; it never creates DevLake projects.',
  load: 'Load from Workstream',
  loadSucceeded: ({
    loaded,
    created,
    updated,
    skipped,
  }: {
    loaded: number;
    created: number;
    updated: number;
    skipped: number;
  }) =>
    `Loaded ${loaded} project${loaded === 1 ? '' : 's'}: ${created} new, ${updated} updated${skipped ? `, ${skipped} skipped` : ''}.`,
  loadFailed: 'Loading from Workstream failed.',
  tableLabel: 'Workstream projects',
  searchPlaceholder: 'Search projects or team leads',
  notLinked: 'Not linked',
  noTeamLead: 'No team lead',
  never: 'Never',
  columns: {
    name: 'Workstream project',
    teamLead: 'Team lead',
    devlakeProject: 'DevLake project',
    lastLoaded: 'Last loaded',
  },
  empty: {
    title: 'No projects loaded yet',
    description: 'Load from Workstream to see its projects and team leads here.',
  },
  noResults: {
    title: 'No matching projects',
    description: 'Try a different search.',
  },
} as const;

export const COLUMN = {
  NAME: 'name',
  TEAM_LEAD: 'teamLead',
  DEVLAKE_PROJECT: 'devlakeProject',
  LAST_LOADED: 'lastLoaded',
} as const;

export const DATE_FORMAT = 'YYYY-MM-DD HH:mm';
