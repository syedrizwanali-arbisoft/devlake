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

package repositories

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

var (
	repoFrom = time.Date(2026, 11, 3, 0, 0, 0, 0, time.UTC)
	repoTo   = time.Date(2026, 11, 10, 0, 0, 0, 0, time.UTC)
)

// repoCall runs one repository method and returns its error, ignoring the result.
type repoCall struct {
	name    string
	run     func(db rowsReader) errors.Error
	wantMsg string
}

func mustRepo[T any](t *testing.T, newRepo func(rowsReader) (*T, errors.Error), db rowsReader) *T {
	t.Helper()
	r, err := newRepo(db)
	if err != nil {
		t.Fatalf("new repository error = %v", err)
	}
	return r
}

func issuesRepo(t *testing.T, db rowsReader, source string) *Issues {
	t.Helper()
	r, err := NewIssues(db, source)
	if err != nil {
		t.Fatalf("NewIssues(%q) error = %v", source, err)
	}
	return r
}

func codeRepo(t *testing.T, db rowsReader, source string) *Code {
	t.Helper()
	r, err := NewCode(db, source)
	if err != nil {
		t.Fatalf("NewCode(%q) error = %v", source, err)
	}
	return r
}

func repoCalls(t *testing.T) []repoCall {
	t.Helper()
	const email, fromDay, toDay = "abc@arbisoft.com", "2026-11-03", "2026-11-10"
	jira := func(db rowsReader) *Issues { return issuesRepo(t, db, SourceJira) }
	github := func(db rowsReader) *Code { return codeRepo(t, db, SourceGitHub) }
	ai := func(db rowsReader) *AI { return mustRepo(t, NewAI, db) }
	return []repoCall{
		{"Assigned", func(db rowsReader) errors.Error { _, err := jira(db).Assigned(email, repoFrom, repoTo); return err }, "read jira issues assigned"},
		{"Created", func(db rowsReader) errors.Error { _, err := jira(db).Created(email, repoFrom, repoTo); return err }, "read jira issues created"},
		{"ResolvedByType", func(db rowsReader) errors.Error {
			_, err := jira(db).ResolvedByType(email, repoFrom, repoTo)
			return err
		}, "read jira issues resolved by type"},
		{"ResolvedByTeam", func(db rowsReader) errors.Error {
			_, err := jira(db).ResolvedByTeam(email, repoFrom, repoTo)
			return err
		}, "read jira issues resolved by team"},
		{"Commits", func(db rowsReader) errors.Error { _, err := github(db).Commits(email, repoFrom, repoTo); return err }, "read github commits"},
		{"Authored", func(db rowsReader) errors.Error { _, err := github(db).Authored(email, repoFrom, repoTo); return err }, "read github pull requests authored"},
		{"PullRequestsWithCommits", func(db rowsReader) errors.Error {
			_, err := github(db).PullRequestsWithCommits(email, repoFrom, repoTo)
			return err
		}, "read github pull requests with the engineer's commits"},
		{"Reviews", func(db rowsReader) errors.Error { _, err := github(db).Reviews(email, repoFrom, repoTo); return err }, "read github reviews"},
		{"CommitsByGroup", func(db rowsReader) errors.Error {
			_, err := github(db).CommitsByGroup(email, repoFrom, repoTo, GroupByTeam)
			return err
		}, "read github commits by group"},
		{"PullRequestsByGroup", func(db rowsReader) errors.Error {
			_, err := github(db).PullRequestsByGroup(email, repoFrom, repoTo, GroupByRepo)
			return err
		}, "read github pull requests by group"},
		{"Providers", func(db rowsReader) errors.Error { _, err := ai(db).Providers(email, fromDay, toDay); return err }, "read ai activities"},
		{"ToolDecisions", func(db rowsReader) errors.Error { _, err := ai(db).ToolDecisions(email, fromDay, toDay); return err }, "read ai tool decisions"},
		{"Monthly", func(db rowsReader) errors.Error { _, err := ai(db).Monthly(email, fromDay, toDay); return err }, "read ai monthly activity"},
		{"ModelCosts", func(db rowsReader) errors.Error { _, err := ai(db).ModelCosts(email, fromDay, toDay); return err }, "read ai model costs"},
		{"TotalCost", func(db rowsReader) errors.Error { _, err := ai(db).TotalCost(email, fromDay, toDay); return err }, "read ai total cost"},
		{"MonthlyCosts", func(db rowsReader) errors.Error { _, err := ai(db).MonthlyCosts(email, fromDay, toDay); return err }, "read ai monthly costs"},
	}
}

// anyAll accepts an All call with any destination and up to six clauses.
func anyAll(db *dalmocks.Dal, err errors.Error) {
	for clauses := 1; clauses <= 6; clauses++ {
		args := make([]interface{}, clauses+1)
		for i := range args {
			args[i] = mock.Anything
		}
		db.On("All", args...).Return(err).Maybe()
	}
}

func TestRepositoryConstructorsRejectNil(t *testing.T) {
	tests := map[string]func() errors.Error{
		"issues": func() errors.Error { _, err := NewIssues(nil, SourceJira); return err },
		"code":   func() errors.Error { _, err := NewCode(nil, SourceGitHub); return err },
		"ai":     func() errors.Error { _, err := NewAI(nil); return err },
	}
	for name, newRepo := range tests {
		t.Run(name, func(t *testing.T) {
			if err := newRepo(); err == nil {
				t.Error("constructor(nil) = nil error, want error")
			}
		})
	}
}

func TestRepositoryErrorsSayWhatWasRead(t *testing.T) {
	for _, tc := range repoCalls(t) {
		t.Run(tc.name, func(t *testing.T) {
			db := dalmocks.NewDal(t)
			anyAll(db, errors.Default.New("connection refused"))

			err := tc.run(db)

			if err == nil || !strings.Contains(err.Error(), tc.wantMsg) || !strings.Contains(err.Error(), "connection refused") {
				t.Errorf("error = %v, want %q wrapping the cause", err, tc.wantMsg)
			}
		})
	}
}

func TestRepositoryWithNoRowsReturnsZeros(t *testing.T) {
	for _, tc := range repoCalls(t) {
		t.Run(tc.name, func(t *testing.T) {
			db := dalmocks.NewDal(t)
			anyAll(db, nil) // reads no rows

			if err := tc.run(db); err != nil {
				t.Errorf("error = %v, want nil", err)
			}
		})
	}
	db := dalmocks.NewDal(t)
	anyAll(db, nil)
	if got, _ := codeRepo(t, db, SourceGitHub).Reviews("abc@arbisoft.com", repoFrom, repoTo); got != (ReviewCounts{}) {
		t.Errorf("Reviews() = %+v, want zeros", got)
	}
	if got, _ := mustRepo(t, NewAI, db).TotalCost("abc@arbisoft.com", "2026-11-03", "2026-11-10"); got != "" {
		t.Errorf("TotalCost() = %q, want empty", got)
	}
}

func TestGitHubRepositoryRejectsAnUnknownGroup(t *testing.T) {
	db := dalmocks.NewDal(t) // no expectations: any query fails the test
	r := codeRepo(t, db, SourceGitHub)

	if _, err := r.CommitsByGroup("abc@arbisoft.com", repoFrom, repoTo, Group(99)); err == nil {
		t.Error("CommitsByGroup(99) = nil error, want error")
	}
	if _, err := r.PullRequestsByGroup("abc@arbisoft.com", repoFrom, repoTo, Group(99)); err == nil {
		t.Error("PullRequestsByGroup(99) = nil error, want error")
	}
}

func TestIDLikeEscapesUnderscores(t *testing.T) {
	for source, want := range map[string]string{"github": "github:%", "github_graphql": `github\_graphql:%`, "a_b_c": `a\_b\_c:%`} {
		if got, err := idLike(source); err != nil || got != want {
			t.Errorf("idLike(%q) = (%q, %v), want %q", source, got, err, want)
		}
	}
}

func TestIssuesAndCodeRejectABadSource(t *testing.T) {
	db := dalmocks.NewDal(t)
	tests := []struct {
		source string
		valid  bool
	}{
		{"jira", true}, {"github", true}, {"gitlab", true}, {"plane", true}, {"github_graphql", true},
		{"", false}, {"Jira", false}, {"git hub", false}, {"jira:%", false}, {"jira'--", false}, {"9jira", false},
	}
	for _, tc := range tests {
		t.Run(tc.source, func(t *testing.T) {
			_, issuesErr := NewIssues(db, tc.source)
			_, codeErr := NewCode(db, tc.source)
			if (issuesErr == nil) != tc.valid || (codeErr == nil) != tc.valid {
				t.Errorf("NewIssues error = %v, NewCode error = %v; want valid = %v", issuesErr, codeErr, tc.valid)
			}
		})
	}
}

// sqlAndParams joins the SQL text and collects the params of an All call's clauses.
func sqlAndParams(clauses []interface{}) (string, []interface{}) {
	var sql []string
	var params []interface{}
	for _, arg := range clauses {
		clause := arg.(dal.Clause)
		switch data := clause.Data.(type) {
		case dal.DalClause:
			sql = append(sql, data.Expr)
			params = append(params, data.Params...)
		case string:
			sql = append(sql, data)
		}
	}
	return strings.Join(sql, " "), params
}

func TestIssuesAndCodeBindTheirSource(t *testing.T) {
	const email = "abc@arbisoft.com"
	tests := []struct {
		name, source string
		run          func(db rowsReader) errors.Error
	}{
		{"Assigned", "plane", func(db rowsReader) errors.Error {
			_, err := issuesRepo(t, db, "plane").Assigned(email, repoFrom, repoTo)
			return err
		}},
		{"Created", "plane", func(db rowsReader) errors.Error {
			_, err := issuesRepo(t, db, "plane").Created(email, repoFrom, repoTo)
			return err
		}},
		{"ResolvedByType", "plane", func(db rowsReader) errors.Error {
			_, err := issuesRepo(t, db, "plane").ResolvedByType(email, repoFrom, repoTo)
			return err
		}},
		{"ResolvedByTeam", "plane", func(db rowsReader) errors.Error {
			_, err := issuesRepo(t, db, "plane").ResolvedByTeam(email, repoFrom, repoTo)
			return err
		}},
		{"Commits", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").Commits(email, repoFrom, repoTo)
			return err
		}},
		{"Authored", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").Authored(email, repoFrom, repoTo)
			return err
		}},
		{"PullRequestsWithCommits", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").PullRequestsWithCommits(email, repoFrom, repoTo)
			return err
		}},
		{"Reviews", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").Reviews(email, repoFrom, repoTo)
			return err
		}},
		{"CommitsByGroup", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").CommitsByGroup(email, repoFrom, repoTo, GroupByRepo)
			return err
		}},
		{"PullRequestsByGroup", "gitlab", func(db rowsReader) errors.Error {
			_, err := codeRepo(t, db, "gitlab").PullRequestsByGroup(email, repoFrom, repoTo, GroupByTeam)
			return err
		}},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			var sql string
			var params []interface{}
			db := dalmocks.NewDal(t)
			for clauses := 1; clauses <= 6; clauses++ {
				args := make([]interface{}, clauses+1)
				for i := range args {
					args[i] = mock.Anything
				}
				db.On("All", args...).Run(func(args mock.Arguments) { sql, params = sqlAndParams(args[1:]) }).Return(nil).Maybe()
			}

			if err := tc.run(db); err != nil {
				t.Fatalf("error = %v", err)
			}

			found := false
			for _, p := range params {
				found = found || reflect.DeepEqual(p, tc.source+":%")
			}
			if !found {
				t.Errorf("params = %v, want the bound pattern %q", params, tc.source+":%")
			}
			for _, literal := range []string{"jira:", "github:", tc.source + ":"} {
				if strings.Contains(sql, literal) {
					t.Errorf("SQL contains %q; the source must be bound, not written: %s", literal, sql)
				}
			}
		})
	}
}
