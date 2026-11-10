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

package integration

import (
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/mock"

	"github.com/apache/incubator-devlake/core/dal"
	"github.com/apache/incubator-devlake/core/errors"
	dalmocks "github.com/apache/incubator-devlake/mocks/core/dal"
)

var fixedNow = time.Date(2026, 11, 10, 12, 0, 0, 0, time.UTC)

// query is one recorded All call.
type query struct {
	sel, from, where string
	params           []interface{}
}

// fakeDB answers All calls by matching the SELECT text, and records every call.
type fakeDB struct {
	t       *testing.T
	db      *dalmocks.Dal
	answers map[string]map[string]interface{} // select-text fragment -> column values of the single row
	lists   map[string][]map[string]interface{}
	queries []query
}

func newFakeDB(t *testing.T) *fakeDB {
	t.Helper()
	f := &fakeDB{t: t, db: dalmocks.NewDal(t), answers: map[string]map[string]interface{}{}, lists: map[string][]map[string]interface{}{}}
	// Queries pass two to five clauses (Select, From, then optionally Where, Groupby and Orderby).
	for clauses := 2; clauses <= 5; clauses++ {
		args := make([]interface{}, clauses+1)
		for i := range args {
			args[i] = mock.Anything
		}
		f.db.On("All", args...).Run(f.serve).Return(nil).Maybe()
	}
	return f
}

func (f *fakeDB) serve(args mock.Arguments) {
	var q query
	for _, arg := range args[1:] {
		clause := arg.(dal.Clause)
		switch data := clause.Data.(type) {
		case dal.DalClause:
			switch clause.Type {
			case dal.SelectClause:
				q.sel = data.Expr
			case dal.FromClause:
				q.from = data.Expr
			case dal.WhereClause:
				q.where = data.Expr
			}
			q.params = append(q.params, data.Params...)
		case string:
			if clause.Type == dal.FromClause {
				q.from = data
			}
		}
	}
	f.queries = append(f.queries, q)
	for fragment, rows := range f.lists {
		if strings.Contains(q.sel, fragment) {
			for _, row := range rows {
				appendRow(f.t, args[0], row)
			}
			return
		}
	}
	for fragment, row := range f.answers {
		if strings.Contains(q.sel, fragment) {
			appendRow(f.t, args[0], row)
			return
		}
	}
}

// appendRow appends one element to the slice dst points to, setting the named fields.
func appendRow(t *testing.T, dst interface{}, values map[string]interface{}) {
	t.Helper()
	slice := reflect.ValueOf(dst).Elem()
	row := reflect.New(slice.Type().Elem()).Elem()
	for name, value := range values {
		field := row.FieldByName(name)
		if !field.IsValid() {
			t.Fatalf("row type %s has no field %s", row.Type(), name)
		}
		field.Set(reflect.ValueOf(value))
	}
	slice.Set(reflect.Append(slice, row))
}

func newService(t *testing.T, db rowsReader) *StatsService {
	t.Helper()
	s, err := NewStatsService(db, WithClock(func() time.Time { return fixedNow }))
	if err != nil {
		t.Fatalf("NewStatsService() error = %v", err)
	}
	return s
}

func TestNewStatsServiceRejectsNilDependencies(t *testing.T) {
	if _, err := NewStatsService(nil); err == nil {
		t.Error("NewStatsService(nil) = nil error, want error")
	}
	if _, err := NewStatsService(dalmocks.NewDal(t), WithClock(nil)); err == nil {
		t.Error("NewStatsService(WithClock(nil)) = nil error, want error")
	}
}

func TestStatsServiceValidatesInputBeforeQuerying(t *testing.T) {
	tests := []struct {
		name, email, tf, wantField string
	}{
		{"bad email", "not-an-email", "1d", "email"},
		{"display name", "Abc <abc@arbisoft.com>", "1d", "email"},
		{"empty email", "", "1d", "email"},
		{"bad tf", "abc@arbisoft.com", "1D", "tf"},
		{"missing tf", "abc@arbisoft.com", "", "tf"},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			db := newFakeDB(t)
			s := newService(t, db.db)
			calls := []func() errors.Error{
				func() errors.Error { _, err := s.Jira(tc.email, tc.tf); return err },
				func() errors.Error { _, err := s.GitHub(tc.email, tc.tf); return err },
				func() errors.Error { _, err := s.AIUsage(tc.email, tc.tf); return err },
				func() errors.Error { _, err := s.AICost(tc.email, tc.tf); return err },
			}
			for _, call := range calls {
				err := call()
				if err == nil || err.GetType().GetHttpCode() != 400 || !strings.Contains(err.Error(), tc.wantField) {
					t.Errorf("error = %v, want a 400 naming %q", err, tc.wantField)
				}
			}
			if len(db.queries) != 0 {
				t.Errorf("ran %d queries for invalid input, want 0", len(db.queries))
			}
		})
	}
}

func TestJiraMapsRowsAndBindsTheWindow(t *testing.T) {
	db := newFakeDB(t)
	db.answers["issues_resolved"] = map[string]interface{}{"IssuesResolved": int64(4), "StoryPointsResolved": 7.5, "IssuesInProgress": int64(2), "IssuesTodo": int64(3)}
	db.answers["issues_created"] = map[string]interface{}{"IssuesCreated": int64(5)}

	got, err := newService(t, db.db).Jira(" ABC@Arbisoft.com ", "1w")
	if err != nil {
		t.Fatalf("Jira() error = %v", err)
	}

	from := fixedNow.AddDate(0, 0, -7)
	want := &JiraStats{StatsWindow: StatsWindow{Email: "abc@arbisoft.com", TimeFrame: "1w", From: from, To: fixedNow},
		IssuesResolved: 4, StoryPointsResolved: 7.5, IssuesCreated: 5, IssuesInProgress: 2, IssuesTodo: 3}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("Jira() = %+v, want %+v", got, want)
	}
	if len(db.queries) != 2 {
		t.Fatalf("queries = %d, want 2", len(db.queries))
	}
	for _, q := range db.queries {
		if !containsParam(q.params, "abc@arbisoft.com") || !containsParam(q.params, from) || !containsParam(q.params, fixedNow) {
			t.Errorf("query %q params = %v, want the lowercased email and the window", q.sel, q.params)
		}
		if !strings.Contains(q.where, "jira:%") || !strings.Contains(q.where, "LOWER(a.email) = ?") {
			t.Errorf("where = %q, want jira issues matched on the lowercased account email", q.where)
		}
	}
}

func TestStatsWithNoDataAreZeroAndNeverNull(t *testing.T) {
	db := newFakeDB(t) // answers nothing: every slice stays empty
	s := newService(t, db.db)

	jira, err := s.Jira("abc@arbisoft.com", "24h")
	if err != nil || jira.IssuesResolved != 0 || jira.IssuesCreated != 0 {
		t.Errorf("Jira() = %+v, %v; want zeros", jira, err)
	}
	github, err := s.GitHub("abc@arbisoft.com", "24h")
	if err != nil || github.Commits != 0 || github.Reviews != 0 {
		t.Errorf("GitHub() = %+v, %v; want zeros", github, err)
	}
	usage, err := s.AIUsage("abc@arbisoft.com", "24h")
	if err != nil || usage.Providers == nil || len(usage.Providers) != 0 {
		t.Errorf("AIUsage().Providers = %#v, %v; want an empty, non-nil slice", usage.Providers, err)
	}
	cost, err := s.AICost("abc@arbisoft.com", "24h")
	if err != nil || cost.Models == nil || len(cost.Models) != 0 || cost.TotalEstimatedCostUSD != "0.00000000" {
		t.Errorf("AICost() = %+v, %v; want empty models and a zero total", cost, err)
	}
}

func TestGitHubMapsEveryQuery(t *testing.T) {
	db := newFakeDB(t)
	db.answers["COUNT(*) AS commits"] = map[string]interface{}{"Commits": int64(9), "Additions": int64(120), "Deletions": int64(30)}
	db.answers["pull_requests_opened"] = map[string]interface{}{"PullRequestsOpened": int64(2), "PullRequestsMerged": int64(1)}
	db.answers["pull_requests_with_commits"] = map[string]interface{}{"PullRequestsWithCommits": int64(3)}
	db.answers["AS reviews"] = map[string]interface{}{"Reviews": int64(6)}

	got, err := newService(t, db.db).GitHub("abc@arbisoft.com", "30d")
	if err != nil {
		t.Fatalf("GitHub() error = %v", err)
	}
	if got.Commits != 9 || got.Additions != 120 || got.Deletions != 30 || got.PullRequestsOpened != 2 ||
		got.PullRequestsMerged != 1 || got.PullRequestsWithCommits != 3 || got.Reviews != 6 {
		t.Errorf("GitHub() = %+v", got)
	}
	for _, q := range db.queries {
		if !strings.Contains(q.where+q.from, "github:%") {
			t.Errorf("query %q isn't limited to GitHub", q.sel)
		}
	}
}

func TestAIUsageAndCostUseWholeDays(t *testing.T) {
	db := newFakeDB(t)
	db.lists["active_days"] = []map[string]interface{}{
		{"Provider": "claude", "ActiveDays": int64(2), "LinesAdded": int64(50)},
		{"Provider": "cursor", "ActiveDays": int64(1), "Acceptances": int64(4)},
	}
	db.answers["tool_edits_accepted"] = map[string]interface{}{"ToolEditsAccepted": int64(8), "ToolEditsRejected": int64(1)}
	db.lists["AS estimated_cost_usd"] = []map[string]interface{}{
		{"Provider": "claude", "Model": "claude-sonnet-5", "InputTokens": int64(10), "EstimatedCostUSD": "1.25000000"},
	}
	db.answers["total_estimated_cost_usd"] = map[string]interface{}{"TotalEstimatedCostUsd": "1.25000000"}
	s := newService(t, db.db)

	usage, err := s.AIUsage("abc@arbisoft.com", "24h")
	if err != nil {
		t.Fatalf("AIUsage() error = %v", err)
	}
	if len(usage.Providers) != 2 || usage.Providers[0].LinesAdded != 50 || usage.ToolEditsAccepted != 8 || usage.ToolEditsRejected != 1 {
		t.Errorf("AIUsage() = %+v", usage)
	}
	cost, err := s.AICost("abc@arbisoft.com", "24h")
	if err != nil {
		t.Fatalf("AICost() error = %v", err)
	}
	if len(cost.Models) != 1 || cost.Models[0].EstimatedCostUSD != "1.25000000" || cost.TotalEstimatedCostUSD != "1.25000000" {
		t.Errorf("AICost() = %+v", cost)
	}
	for _, q := range db.queries {
		if !containsParam(q.params, "2026-11-09") || !containsParam(q.params, "2026-11-10") {
			t.Errorf("query %q params = %v, want the day range 2026-11-09..2026-11-10", q.sel, q.params)
		}
	}
}

func TestQueryErrorsAreWrapped(t *testing.T) {
	db := dalmocks.NewDal(t)
	db.On("All", mock.Anything, mock.Anything, mock.Anything, mock.Anything).Return(errors.Default.New("connection refused"))

	_, err := newService(t, db).GitHub("abc@arbisoft.com", "1d")

	if err == nil || !strings.Contains(err.Error(), "read github commits") {
		t.Errorf("GitHub() error = %v, want it wrapped with the query it was running", err)
	}
}

func containsParam(params []interface{}, want interface{}) bool {
	for _, p := range params {
		if reflect.DeepEqual(p, want) {
			return true
		}
	}
	return false
}
