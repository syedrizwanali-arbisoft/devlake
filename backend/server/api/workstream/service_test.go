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
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/mock"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models"
	dalmocks "github.com/apache/incubator-devlake/mocks/core/dal"
)

var loadTime = time.Date(2026, 11, 10, 12, 0, 0, 0, time.UTC)

type fakeFetcher struct {
	projects []FetchedProject
	err      errors.Error
}

func (f fakeFetcher) FetchProjects(context.Context) ([]FetchedProject, errors.Error) {
	return f.projects, f.err
}

func lead(name, email string) *struct {
	Name  string `json:"name"`
	Email string `json:"email"`
} {
	return &struct {
		Name  string `json:"name"`
		Email string `json:"email"`
	}{name, email}
}

func TestNormalizeProjects(t *testing.T) {
	rows, skipped := normalizeProjects([]FetchedProject{
		{ID: " 7 ", Name: " DevLake ", TeamLead: lead(" Lead ", " Lead@Arbisoft.com ")},
		{ID: "", Name: "no id"},
		{ID: "8", Name: "Sophia"},
		{ID: "7", Name: "DevLake renamed"},
	}, loadTime)

	want := []models.WorkstreamProject{
		{WorkstreamId: "7", Name: "DevLake renamed", LastLoadedAt: &loadTime},
		{WorkstreamId: "8", Name: "Sophia", LastLoadedAt: &loadTime},
	}
	if !reflect.DeepEqual(rows, want) || skipped != 2 {
		t.Errorf("normalizeProjects() = %+v, %d; want %+v, 2", rows, skipped, want)
	}
	first, _ := normalizeProjects([]FetchedProject{{ID: "7", Name: "x", TeamLead: lead(" Lead ", " Lead@Arbisoft.com ")}}, loadTime)
	if first[0].TeamLeadName != "Lead" || first[0].TeamLeadEmail != "lead@arbisoft.com" {
		t.Errorf("team lead = %q %q, want trimmed and the email lowercased", first[0].TeamLeadName, first[0].TeamLeadEmail)
	}
}

func newTestService(t *testing.T, fetcher projectsFetcher, store projectStore) *Service {
	t.Helper()
	s, err := NewService(fetcher, store, WithClock(func() time.Time { return loadTime }))
	if err != nil {
		t.Fatalf("NewService() error = %v", err)
	}
	return s
}

func TestLoadUpsertsAndCountsNewAndExisting(t *testing.T) {
	store := dalmocks.NewDal(t)
	store.On("Pluck", "workstream_id", mock.Anything, mock.Anything, mock.Anything).Run(func(args mock.Arguments) {
		*args.Get(1).(*[]string) = []string{"7"}
	}).Return(nil).Once()
	var saved []models.WorkstreamProject
	store.On("CreateOrUpdate", mock.Anything).Run(func(args mock.Arguments) {
		saved = *args.Get(0).(*[]models.WorkstreamProject)
	}).Return(nil).Once()
	fetcher := fakeFetcher{projects: []FetchedProject{{ID: "7", Name: "DevLake"}, {ID: "8", Name: "Sophia"}, {Name: "no id"}}}

	result, err := newTestService(t, fetcher, store).Load(context.Background())

	if err != nil {
		t.Fatalf("Load() error = %v", err)
	}
	if *result != (LoadResult{Loaded: 2, Created: 1, Updated: 1, Skipped: 1}) {
		t.Errorf("Load() = %+v", result)
	}
	if len(saved) != 2 || saved[1].WorkstreamId != "8" || !saved[0].LastLoadedAt.Equal(loadTime) {
		t.Errorf("saved = %+v", saved)
	}
}

func TestLoadWithNothingToSaveTouchesNoRows(t *testing.T) {
	store := dalmocks.NewDal(t) // any DAL call would fail the test

	result, err := newTestService(t, fakeFetcher{}, store).Load(context.Background())

	if err != nil || *result != (LoadResult{}) {
		t.Errorf("Load() = %+v, %v; want an empty result", result, err)
	}
}

func TestLoadErrors(t *testing.T) {
	t.Run("not configured", func(t *testing.T) {
		_, err := newTestService(t, nil, dalmocks.NewDal(t)).Load(context.Background())
		if err == nil || err.GetType().GetHttpCode() != 503 || !strings.Contains(err.Error(), EnvURL) {
			t.Errorf("Load() error = %v, want 503 naming the settings", err)
		}
	})
	t.Run("fetch failure is returned as is", func(t *testing.T) {
		_, err := newTestService(t, fakeFetcher{err: errors.Timeout.New("slow")}, dalmocks.NewDal(t)).Load(context.Background())
		if err == nil || err.GetType().GetHttpCode() != 504 {
			t.Errorf("Load() error = %v, want the fetcher's 504", err)
		}
	})
	t.Run("save failure", func(t *testing.T) {
		store := dalmocks.NewDal(t)
		store.On("Pluck", "workstream_id", mock.Anything, mock.Anything, mock.Anything).Return(nil)
		store.On("CreateOrUpdate", mock.Anything).Return(errors.Default.New("deadlock"))
		_, err := newTestService(t, fakeFetcher{projects: []FetchedProject{{ID: "1"}}}, store).Load(context.Background())
		if err == nil || !strings.Contains(err.Error(), "save workstream projects") {
			t.Errorf("Load() error = %v", err)
		}
	})
}

func TestListLinksDevLakeProjectsAndIsNeverNull(t *testing.T) {
	store := dalmocks.NewDal(t)
	store.On("All", mock.Anything, mock.Anything, mock.Anything, mock.Anything).Return(nil).Once()

	list, err := newTestService(t, nil, store).List()

	if err != nil || list.Count != 0 || list.Projects == nil {
		t.Errorf("List() = %+v, %v; want an empty, non-nil list", list, err)
	}

	store = dalmocks.NewDal(t)
	store.On("All", mock.Anything, mock.Anything, mock.Anything, mock.Anything).Run(func(args mock.Arguments) {
		*args.Get(0).(*[]ProjectView) = []ProjectView{{WorkstreamId: "7", Name: "DevLake", DevlakeProject: "DevLake"}}
	}).Return(nil).Once()

	list, err = newTestService(t, nil, store).List()

	if err != nil || list.Count != 1 || list.Projects[0].DevlakeProject != "DevLake" {
		t.Errorf("List() = %+v, %v", list, err)
	}
}

func TestNewServiceRejectsNilStore(t *testing.T) {
	if _, err := NewService(nil, nil); err == nil {
		t.Error("NewService(nil store) = nil error")
	}
}
