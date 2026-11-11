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

import { CloudDownloadOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import { useMemo, useState } from 'react';

import API from '@/api';
import type { LoadResult, WorkstreamProject } from '@/api/workstream';
import { useRefreshData } from '@/hooks';
import { DataTable, ListPage, ListToolbar, PageHeader, buildListEmpty, useListState, useRefreshVersion } from '@/ui';
import { operator } from '@/utils';

import { getColumns } from './columns';
import { COPY } from './constants';
import { filterProjects } from './utils';

export const ProjectIngestion = () => {
  const list = useListState<string, Record<string, never>>({ filters: {} });
  const { version, refresh } = useRefreshVersion();
  const [loading, setLoading] = useState(false);
  const { data, ready, error } = useRefreshData((signal) => API.workstream.list(signal), [version]);

  const columns = useMemo(() => getColumns(), []);
  const projects = useMemo(() => filterProjects(data?.projects ?? [], list.keyword), [data, list.keyword]);

  const handleLoad = async () => {
    let result: LoadResult | undefined;
    const [success] = await operator(
      async () => {
        result = await API.workstream.load();
        return result;
      },
      {
        setOperating: setLoading,
        formatMessage: () => (result ? COPY.loadSucceeded(result) : COPY.loadFailed),
        formatReason: (err) =>
          (err as { response?: { data?: { message?: string } } } | undefined)?.response?.data?.message ??
          COPY.loadFailed,
      },
    );
    if (success) refresh();
  };

  const loadButton = (
    <Button type="primary" icon={<CloudDownloadOutlined />} loading={loading} onClick={handleLoad}>
      {COPY.load}
    </Button>
  );

  const empty = buildListEmpty({
    failed: error !== undefined,
    onRetry: refresh,
    filtered: list.keyword !== '',
    empty: { ...COPY.empty, action: loadButton },
    noResults: COPY.noResults,
  });

  return (
    <ListPage>
      <PageHeader title={COPY.title} description={COPY.description} />
      <ListToolbar list={list} searchPlaceholder={COPY.searchPlaceholder} end={loadButton} />
      <DataTable<WorkstreamProject>
        rowKey="workstreamId"
        ariaLabel={COPY.tableLabel}
        loading={!ready && error === undefined}
        columns={columns}
        dataSource={projects}
        empty={empty}
      />
    </ListPage>
  );
};
