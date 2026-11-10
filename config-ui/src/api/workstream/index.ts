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

import { request } from '@/utils';

/** A Workstream project with its team lead, and the DevLake project of the same name if one exists. */
export type WorkstreamProject = {
  workstreamId: string;
  name: string;
  teamLeadName: string;
  teamLeadEmail: string;
  lastLoadedAt: string | null;
  /** The linked DevLake project's name, or an empty string when none has this name. */
  devlakeProject: string;
};

export type WorkstreamProjectList = {
  count: number;
  projects: WorkstreamProject[];
};

export type LoadResult = {
  loaded: number;
  created: number;
  updated: number;
  skipped: number;
};

export const list = (signal?: AbortSignal): Promise<WorkstreamProjectList> =>
  request('/workstream-projects', { signal });

/** Loads every Workstream project through the Integration Service. DevLake holds the credentials; the UI sends none. */
export const load = (): Promise<LoadResult> => request('/projects/load', { method: 'POST' });
