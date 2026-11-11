/*
Licensed to the Apache Software Foundation (ASF) under one or more
contributor license agreements.  See the NOTICE file distributed with
this work for additional information regarding copyright ownership.
The ASF licenses this file to You under the Apache License, Version 2.0
(the "License"); you may not use this file except in compliance with
the License.  You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

package workstream

import (
	"context"
	"strings"
	"time"

	"github.com/apache/incubator-devlake/core/dal"
	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models"
)

// projectsFetcher returns every Workstream project. Client satisfies it.
type projectsFetcher interface {
	FetchProjects(ctx context.Context) ([]FetchedProject, errors.Error)
}

// projectStore is the DAL subset the service uses. dal.Dal satisfies it.
type projectStore interface {
	All(dst interface{}, clauses ...dal.Clause) errors.Error
	Pluck(column string, dest interface{}, clauses ...dal.Clause) errors.Error
	CreateOrUpdate(entity interface{}, clauses ...dal.Clause) errors.Error
}

var (
	_ projectsFetcher = (*Client)(nil)
	_ projectStore    = (dal.Dal)(nil)
)

// LoadResult summarizes one load.
type LoadResult struct {
	// Loaded is how many projects were saved.
	Loaded int `json:"loaded"`
	// Created is how many of them were new.
	Created int `json:"created"`
	// Updated is how many already existed and were updated.
	Updated int `json:"updated"`
	// Skipped is how many items had no ID, or repeated an ID, and were not saved.
	Skipped int `json:"skipped"`
}

// ProjectView is a catalog row with its DevLake project, if one has the same name.
type ProjectView struct {
	WorkstreamId   string     `json:"workstreamId"`
	Name           string     `json:"name"`
	TeamLeadName   string     `json:"teamLeadName"`
	TeamLeadEmail  string     `json:"teamLeadEmail"`
	LastLoadedAt   *time.Time `json:"lastLoadedAt"`
	DevlakeProject string     `json:"devlakeProject"`
}

// ProjectList is the catalog, sorted by name.
type ProjectList struct {
	Count    int           `json:"count"`
	Projects []ProjectView `json:"projects"`
}

// Service loads Workstream projects into the catalog and lists them.
type Service struct {
	fetcher projectsFetcher
	store   projectStore
	now     func() time.Time
}

// Option configures a Service.
type Option func(*Service)

// WithClock sets the clock that stamps lastLoadedAt. The default is time.Now.
func WithClock(now func() time.Time) Option { return func(s *Service) { s.now = now } }

// NewService returns a service. A nil fetcher means the Integration Service isn't configured: List still works and
// Load returns Unavailable.
func NewService(fetcher projectsFetcher, store projectStore, opts ...Option) (*Service, errors.Error) {
	if store == nil {
		return nil, errors.Default.New("workstream service: nil store")
	}
	s := &Service{fetcher: fetcher, store: store, now: time.Now}
	for _, opt := range opts {
		opt(s)
	}
	if s.now == nil {
		return nil, errors.Default.New("workstream service: nil clock")
	}
	return s, nil
}

// Load fetches every Workstream project and upserts it by Workstream ID, in one statement. Projects that are no
// longer in Workstream keep their row; their older lastLoadedAt shows they weren't in the latest load.
func (s *Service) Load(ctx context.Context) (*LoadResult, errors.Error) {
	if s.fetcher == nil {
		return nil, errors.Unavailable.New("loading from Workstream needs " + EnvURL + ", " + EnvClientID + " and " + EnvClientSecret + " to be set")
	}
	fetched, err := s.fetcher.FetchProjects(ctx)
	if err != nil {
		return nil, err
	}
	now := s.now().UTC()
	rows, skipped := normalizeProjects(fetched, now)
	result := &LoadResult{Loaded: len(rows), Skipped: skipped}
	if len(rows) == 0 {
		return result, nil
	}
	ids := make([]string, len(rows))
	for i := range rows {
		ids[i] = rows[i].WorkstreamId
	}
	var existing []string
	if err := s.store.Pluck("workstream_id", &existing, dal.From(&models.WorkstreamProject{}), dal.Where("workstream_id IN ?", ids)); err != nil {
		return nil, errors.Default.Wrap(err, "read existing workstream projects")
	}
	if err := s.store.CreateOrUpdate(&rows); err != nil {
		return nil, errors.Default.Wrap(err, "save workstream projects")
	}
	result.Updated = len(existing)
	result.Created = len(rows) - len(existing)
	return result, nil
}

// normalizeProjects trims names, lowercases emails, drops items without an ID and keeps the last item for a
// repeated ID. It returns the rows to save and how many items it skipped.
func normalizeProjects(fetched []FetchedProject, loadedAt time.Time) ([]models.WorkstreamProject, int) {
	rows := make([]models.WorkstreamProject, 0, len(fetched))
	index := make(map[string]int, len(fetched))
	skipped := 0
	for _, item := range fetched {
		id := strings.TrimSpace(item.ID)
		if id == "" {
			skipped++
			continue
		}
		row := models.WorkstreamProject{WorkstreamId: id, Name: strings.TrimSpace(item.Name), LastLoadedAt: &loadedAt}
		if item.TeamLead != nil {
			row.TeamLeadName = strings.TrimSpace(item.TeamLead.Name)
			row.TeamLeadEmail = strings.ToLower(strings.TrimSpace(item.TeamLead.Email))
		}
		if at, seen := index[id]; seen {
			rows[at] = row
			skipped++
			continue
		}
		index[id] = len(rows)
		rows = append(rows, row)
	}
	return rows, skipped
}

// List returns the catalog with each project's DevLake project, matched by name.
func (s *Service) List() (*ProjectList, errors.Error) {
	var projects []ProjectView
	if err := s.store.All(&projects,
		dal.Select(`w.workstream_id, w.name, w.team_lead_name, w.team_lead_email, w.last_loaded_at,
			COALESCE(p.name, '') AS devlake_project`),
		dal.From("workstream_projects w LEFT JOIN projects p ON p.name = w.name"),
		dal.Orderby("w.name, w.workstream_id"),
	); err != nil {
		return nil, errors.Default.Wrap(err, "list workstream projects")
	}
	if projects == nil {
		projects = []ProjectView{}
	}
	return &ProjectList{Count: len(projects), Projects: projects}, nil
}
