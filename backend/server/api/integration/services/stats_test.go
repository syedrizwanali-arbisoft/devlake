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

package services

import (
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/server/api/integration/repositories"
	"github.com/apache/incubator-devlake/server/api/integration/utils"
)

var fixedNow = time.Date(2026, 11, 10, 12, 0, 0, 0, time.UTC)

// call is one recorded repository call: the method, the email and the window or day range it was given.
type call struct {
	method, email string
	args          []interface{}
}

// fakeRepos implements every repository interface with canned answers, records each call, and fails the method
// named in failOn.
type fakeRepos struct {
	assigned     repositories.AssignedCounts
	created      int64
	byType       []repositories.IssueTypeRow
	byTeam       []repositories.IssueTeamRow
	commits      repositories.CommitCounts
	authored     repositories.AuthoredCounts
	withCommits  int64
	reviews      repositories.ReviewCounts
	commitGroups map[repositories.Group][]repositories.CommitGroupRow
	prGroups     map[repositories.Group][]repositories.PullRequestGroupRow
	providers    []repositories.AIProviderRow
	decisions    repositories.AIToolDecisionCounts
	monthly      []repositories.AIMonthlyRow
	modelCosts   []repositories.AIModelCostRow
	totalCost    string
	monthlyCosts []repositories.AIMonthlyCostRow
	failOn       string
	calls        []call
}

var (
	_ ticketStatsReader         = (*fakeRepos)(nil)
	_ versionControlStatsReader = (*fakeRepos)(nil)
	_ aiReader                  = (*fakeRepos)(nil)
)

// errRepo is the error a failing fake method returns.
var errRepo = errors.Default.New("read something: connection refused")

func (f *fakeRepos) record(method, email string, args ...interface{}) errors.Error {
	f.calls = append(f.calls, call{method: method, email: email, args: args})
	if f.failOn == method {
		return errRepo
	}
	return nil
}

func (f *fakeRepos) Assigned(email string, from, to time.Time) (repositories.AssignedCounts, errors.Error) {
	return f.assigned, f.record("Assigned", email, from, to)
}

func (f *fakeRepos) Created(email string, from, to time.Time) (int64, errors.Error) {
	return f.created, f.record("Created", email, from, to)
}

func (f *fakeRepos) ResolvedByType(email string, from, to time.Time) ([]repositories.IssueTypeRow, errors.Error) {
	return f.byType, f.record("ResolvedByType", email, from, to)
}

func (f *fakeRepos) ResolvedByTeam(email string, from, to time.Time) ([]repositories.IssueTeamRow, errors.Error) {
	return f.byTeam, f.record("ResolvedByTeam", email, from, to)
}

func (f *fakeRepos) Commits(email string, from, to time.Time) (repositories.CommitCounts, errors.Error) {
	return f.commits, f.record("Commits", email, from, to)
}

func (f *fakeRepos) Authored(email string, from, to time.Time) (repositories.AuthoredCounts, errors.Error) {
	return f.authored, f.record("Authored", email, from, to)
}

func (f *fakeRepos) PullRequestsWithCommits(email string, from, to time.Time) (int64, errors.Error) {
	return f.withCommits, f.record("PullRequestsWithCommits", email, from, to)
}

func (f *fakeRepos) Reviews(email string, from, to time.Time) (repositories.ReviewCounts, errors.Error) {
	return f.reviews, f.record("Reviews", email, from, to)
}

func (f *fakeRepos) CommitsByGroup(email string, from, to time.Time, group repositories.Group) ([]repositories.CommitGroupRow, errors.Error) {
	return f.commitGroups[group], f.record("CommitsByGroup", email, from, to, group)
}

func (f *fakeRepos) PullRequestsByGroup(email string, from, to time.Time, group repositories.Group) ([]repositories.PullRequestGroupRow, errors.Error) {
	return f.prGroups[group], f.record("PullRequestsByGroup", email, from, to, group)
}

func (f *fakeRepos) Providers(email, fromDay, toDay string) ([]repositories.AIProviderRow, errors.Error) {
	return f.providers, f.record("Providers", email, fromDay, toDay)
}

func (f *fakeRepos) ToolDecisions(email, fromDay, toDay string) (repositories.AIToolDecisionCounts, errors.Error) {
	return f.decisions, f.record("ToolDecisions", email, fromDay, toDay)
}

func (f *fakeRepos) Monthly(email, fromDay, toDay string) ([]repositories.AIMonthlyRow, errors.Error) {
	return f.monthly, f.record("Monthly", email, fromDay, toDay)
}

func (f *fakeRepos) ModelCosts(email, fromDay, toDay string) ([]repositories.AIModelCostRow, errors.Error) {
	return f.modelCosts, f.record("ModelCosts", email, fromDay, toDay)
}

func (f *fakeRepos) TotalCost(email, fromDay, toDay string) (string, errors.Error) {
	return f.totalCost, f.record("TotalCost", email, fromDay, toDay)
}

func (f *fakeRepos) MonthlyCosts(email, fromDay, toDay string) ([]repositories.AIMonthlyCostRow, errors.Error) {
	return f.monthlyCosts, f.record("MonthlyCosts", email, fromDay, toDay)
}

func newService(t *testing.T, repos *fakeRepos) *StatsService {
	t.Helper()
	s, err := NewStatsService(repos, repos, repos, WithClock(func() time.Time { return fixedNow }))
	if err != nil {
		t.Fatalf("NewStatsService() error = %v", err)
	}
	return s
}

func TestNewStatsServiceRejectsNilDependencies(t *testing.T) {
	f := &fakeRepos{}
	tests := []struct {
		name                string
		ticketStats         ticketStatsReader
		versionControlStats versionControlStatsReader
		ai                  aiReader
		opts                []Option
	}{
		{name: "nil ticket stats", versionControlStats: f, ai: f},
		{name: "nil version control stats", ticketStats: f, ai: f},
		{name: "nil ai", ticketStats: f, versionControlStats: f},
		{name: "nil clock", ticketStats: f, versionControlStats: f, ai: f, opts: []Option{WithClock(nil)}},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			if _, err := NewStatsService(tc.ticketStats, tc.versionControlStats, tc.ai, tc.opts...); err == nil {
				t.Error("NewStatsService() = nil error, want error")
			}
		})
	}
}

// statsCalls runs each stats method, so a test can check all four the same way.
func statsCalls(s *StatsService, email string, p utils.Period) map[string]func() errors.Error {
	return map[string]func() errors.Error{
		"Jira":    func() errors.Error { _, err := s.Jira(email, p); return err },
		"GitHub":  func() errors.Error { _, err := s.GitHub(email, p); return err },
		"AIUsage": func() errors.Error { _, err := s.AIUsage(email, p); return err },
		"AICost":  func() errors.Error { _, err := s.AICost(email, p); return err },
	}
}

func TestStatsServiceValidatesInputBeforeReading(t *testing.T) {
	tests := []struct {
		name, email, wantField string
		p                      utils.Period
	}{
		{"bad email", "not-an-email", "email", utils.Period{TimeFrame: "1d"}},
		{"display name", "Abc <abc@arbisoft.com>", "email", utils.Period{TimeFrame: "1d"}},
		{"empty email", "", "email", utils.Period{TimeFrame: "1d"}},
		{"bad tf", "abc@arbisoft.com", "tf", utils.Period{TimeFrame: "1D"}},
		{"missing period", "abc@arbisoft.com", "tf", utils.Period{}},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			repos := &fakeRepos{}
			for method, run := range statsCalls(newService(t, repos), tc.email, tc.p) {
				err := run()
				if err == nil || err.GetType().GetHttpCode() != 400 || !strings.Contains(err.Error(), tc.wantField) {
					t.Errorf("%s() error = %v, want a 400 naming %q", method, err, tc.wantField)
				}
			}
			if len(repos.calls) != 0 {
				t.Errorf("made %d repository calls for invalid input, want 0", len(repos.calls))
			}
		})
	}
}

func TestStatsServicePassesTheLowercasedEmailAndWindow(t *testing.T) {
	repos := &fakeRepos{}
	from := fixedNow.AddDate(0, 0, -7)
	for method, run := range statsCalls(newService(t, repos), " ABC@Arbisoft.com ", utils.Period{TimeFrame: "1w"}) {
		if err := run(); err != nil {
			t.Fatalf("%s() error = %v", method, err)
		}
	}
	for _, c := range repos.calls {
		if c.email != "abc@arbisoft.com" {
			t.Errorf("%s got email %q, want abc@arbisoft.com", c.method, c.email)
		}
		// AI methods get the half-open range of days that start in the window; the others get the exact window.
		want := []interface{}{from, fixedNow}
		if _, isDay := c.args[0].(string); isDay {
			want = []interface{}{"2026-11-04", "2026-11-11"}
		}
		if !reflect.DeepEqual(c.args[:2], want) {
			t.Errorf("%s got %v, want %v", c.method, c.args[:2], want)
		}
	}
}

func TestStatsServiceReturnsRepositoryErrorsUnchanged(t *testing.T) {
	tests := []struct{ stats, failOn string }{
		{"Jira", "Assigned"}, {"Jira", "Created"}, {"Jira", "ResolvedByType"}, {"Jira", "ResolvedByTeam"},
		{"GitHub", "Commits"}, {"GitHub", "Authored"}, {"GitHub", "PullRequestsWithCommits"}, {"GitHub", "Reviews"},
		{"GitHub", "CommitsByGroup"}, {"GitHub", "PullRequestsByGroup"},
		{"AIUsage", "Providers"}, {"AIUsage", "ToolDecisions"}, {"AIUsage", "Monthly"},
		{"AICost", "ModelCosts"}, {"AICost", "TotalCost"}, {"AICost", "MonthlyCosts"},
	}
	for _, tc := range tests {
		t.Run(tc.stats+"/"+tc.failOn, func(t *testing.T) {
			err := statsCalls(newService(t, &fakeRepos{failOn: tc.failOn}), "abc@arbisoft.com", utils.Period{TimeFrame: "1d"})[tc.stats]()
			if err != errRepo {
				t.Errorf("%s() error = %v, want the repository error %v", tc.stats, err, errRepo)
			}
		})
	}
}

func TestJiraMapsTheRepositoryRows(t *testing.T) {
	repos := &fakeRepos{
		assigned: repositories.AssignedCounts{IssuesResolved: 4, StoryPointsResolved: 7.5, IssuesInProgress: 2, IssuesTodo: 3, AvgResolutionHours: 36.5},
		created:  5,
		byType:   []repositories.IssueTypeRow{{IssueType: "BUG", IssuesResolved: 4, StoryPoints: 7.5}},
		byTeam:   []repositories.IssueTeamRow{{Team: "Akina", IssuesResolved: 4, StoryPoints: 7.5}},
	}

	got, err := newService(t, repos).Jira("abc@arbisoft.com", utils.Period{TimeFrame: "1w"})
	if err != nil {
		t.Fatalf("Jira() error = %v", err)
	}

	want := &JiraStats{StatsWindow: StatsWindow{Email: "abc@arbisoft.com", TimeFrame: "1w", From: fixedNow.AddDate(0, 0, -7), To: fixedNow},
		IssuesResolved: 4, StoryPointsResolved: 7.5, IssuesCreated: 5, IssuesInProgress: 2, IssuesTodo: 3, AvgResolutionHours: 36.5,
		ByType: []JiraTypeStats{{IssueType: "BUG", IssuesResolved: 4, StoryPoints: 7.5}},
		ByTeam: []JiraTeamStats{{Team: "Akina", IssuesResolved: 4, StoryPoints: 7.5}}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("Jira() = %+v, want %+v", got, want)
	}
}

func TestGitHubMapsTheRepositoryRows(t *testing.T) {
	repoRows := []repositories.CommitGroupRow{{Name: "org/a", RepoCommits: 4, Contributors: 2, EngineerCommits: 1}}
	teamRows := []repositories.CommitGroupRow{{Name: "Akina", RepoCommits: 10, Contributors: 5, EngineerCommits: 1}}
	repos := &fakeRepos{
		commits:      repositories.CommitCounts{Commits: 9, Additions: 120, Deletions: 30},
		authored:     repositories.AuthoredCounts{PullRequestsOpened: 2, PullRequestsMerged: 1, AvgPrMergeHours: 4.5},
		withCommits:  3,
		reviews:      repositories.ReviewCounts{Reviews: 6, ReviewComments: 4, PullRequestsReviewed: 5},
		commitGroups: map[repositories.Group][]repositories.CommitGroupRow{repositories.GroupByRepo: repoRows, repositories.GroupByTeam: teamRows},
	}

	got, err := newService(t, repos).GitHub("abc@arbisoft.com", utils.Period{TimeFrame: "30d"})
	if err != nil {
		t.Fatalf("GitHub() error = %v", err)
	}

	want := &GitHubStats{StatsWindow: StatsWindow{Email: "abc@arbisoft.com", TimeFrame: "30d", From: fixedNow.AddDate(0, 0, -30), To: fixedNow},
		Commits: 9, Additions: 120, Deletions: 30, PullRequestsOpened: 2, PullRequestsMerged: 1, PullRequestsWithCommits: 3,
		Reviews: 6, ReviewComments: 4, PullRequestsReviewed: 5, AvgPrMergeHours: 4.5,
		ByRepo: []GitHubBreakdown{{Name: "org/a", Commits: 1, RepoCommits: 4, Contributors: 2, CommitSharePct: 25}},
		ByTeam: []GitHubBreakdown{{Name: "Akina", Commits: 1, RepoCommits: 10, Contributors: 5, CommitSharePct: 10}}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("GitHub() = %+v, want %+v", got, want)
	}
}

func TestMergeBreakdown(t *testing.T) {
	tests := []struct {
		name    string
		commits []repositories.CommitGroupRow
		prs     []repositories.PullRequestGroupRow
		want    []GitHubBreakdown
	}{
		{name: "no data is empty, not null", want: []GitHubBreakdown{}},
		{
			name: "commits and pull requests of a group merge, groups sort by name, shares use every contributor",
			commits: []repositories.CommitGroupRow{
				{Name: "org/b", RepoCommits: 3, Contributors: 1, EngineerCommits: 3},
				{Name: "org/a", RepoCommits: 20, RepoLines: 1000, Contributors: 4, EngineerCommits: 5, EngineerLines: 250},
			},
			prs: []repositories.PullRequestGroupRow{
				{Name: "org/a", RepoMerged: 4, EngineerOpened: 2, EngineerMerged: 1},
				{Name: "org/c", EngineerOpened: 1},
			},
			want: []GitHubBreakdown{
				{Name: "org/a", Commits: 5, LinesChanged: 250, PullRequestsOpened: 2, PullRequestsMerged: 1, RepoCommits: 20, RepoLinesChanged: 1000,
					RepoPullRequestsMerged: 4, Contributors: 4, CommitSharePct: 25, LinesSharePct: 25, PullRequestsMergedSharePct: 25},
				{Name: "org/b", Commits: 3, RepoCommits: 3, Contributors: 1, CommitSharePct: 100},
				{Name: "org/c", PullRequestsOpened: 1},
			},
		},
		{
			name: "a group with only the engineer's pull requests keeps everyone's commit totals, and the reverse",
			commits: []repositories.CommitGroupRow{
				{Name: "org/a", RepoCommits: 10, RepoLines: 400, Contributors: 3},
				{Name: "org/b", RepoCommits: 2, Contributors: 1, EngineerCommits: 2},
			},
			prs: []repositories.PullRequestGroupRow{
				{Name: "org/a", RepoMerged: 2, EngineerMerged: 1},
				{Name: "org/b", RepoMerged: 5},
			},
			want: []GitHubBreakdown{
				{Name: "org/a", PullRequestsMerged: 1, RepoCommits: 10, RepoLinesChanged: 400, RepoPullRequestsMerged: 2,
					Contributors: 3, PullRequestsMergedSharePct: 50},
				{Name: "org/b", Commits: 2, RepoCommits: 2, RepoPullRequestsMerged: 5, Contributors: 1, CommitSharePct: 100},
			},
		},
		{
			name:    "a group where only others worked is left out",
			commits: []repositories.CommitGroupRow{{Name: "org/x", RepoCommits: 7, Contributors: 2}},
			prs:     []repositories.PullRequestGroupRow{{Name: "org/x", RepoMerged: 3}},
			want:    []GitHubBreakdown{},
		},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			if got := mergeBreakdown(tc.commits, tc.prs); !reflect.DeepEqual(got, tc.want) {
				t.Errorf("mergeBreakdown() = %+v, want %+v", got, tc.want)
			}
		})
	}
}

func TestSharePct(t *testing.T) {
	for _, tc := range []struct {
		part, total int64
		want        float64
	}{{1, 3, 33.3}, {2, 3, 66.7}, {0, 0, 0}, {5, 0, 0}, {7, 7, 100}} {
		if got := sharePct(tc.part, tc.total); got != tc.want {
			t.Errorf("sharePct(%d, %d) = %v, want %v", tc.part, tc.total, got, tc.want)
		}
	}
}

func TestAIUsageMapsTheRepositoryRows(t *testing.T) {
	repos := &fakeRepos{
		providers: []repositories.AIProviderRow{{Provider: "copilot", ActiveDays: 2, Suggestions: 640, Acceptances: 210, CommitsByAI: 1}},
		decisions: repositories.AIToolDecisionCounts{ToolEditsAccepted: 8, ToolEditsRejected: 1},
		monthly:   []repositories.AIMonthlyRow{{Provider: "copilot", Month: "2026-10", ActiveDays: 2, Sessions: 9, Suggestions: 640, Acceptances: 210}},
	}

	got, err := newService(t, repos).AIUsage("abc@arbisoft.com", utils.Period{TimeFrame: "30d"})
	if err != nil {
		t.Fatalf("AIUsage() error = %v", err)
	}

	want := &AIUsageStats{StatsWindow: StatsWindow{Email: "abc@arbisoft.com", TimeFrame: "30d", From: fixedNow.AddDate(0, 0, -30), To: fixedNow},
		Providers:         []AIProviderUsage{{Provider: "copilot", ActiveDays: 2, Suggestions: 640, Acceptances: 210, CommitsByAI: 1}},
		ToolEditsAccepted: 8, ToolEditsRejected: 1,
		Monthly: []AIMonthlyUsage{{Provider: "copilot", Month: "2026-10", ActiveDays: 2, Sessions: 9, Suggestions: 640, Acceptances: 210}}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("AIUsage() = %+v, want %+v", got, want)
	}
}

func TestAIUsageActiveInLastMonth(t *testing.T) {
	tests := []struct {
		name   string
		months []string
		want   bool
	}{
		{"no activity", nil, false},
		{"only earlier months", []string{"2026-09", "2026-10"}, false},
		{"activity in the month the window ends", []string{"2026-10", "2026-11"}, true},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			repos := &fakeRepos{}
			for _, m := range tc.months {
				repos.monthly = append(repos.monthly, repositories.AIMonthlyRow{Provider: "claude", Month: m, ActiveDays: 1})
			}
			got, err := newService(t, repos).AIUsage("abc@arbisoft.com", utils.Period{TimeFrame: "1y"})
			if err != nil {
				t.Fatalf("AIUsage() error = %v", err)
			}
			if got.ActiveInLastMonth != tc.want {
				t.Errorf("ActiveInLastMonth = %v, want %v", got.ActiveInLastMonth, tc.want)
			}
		})
	}
}

func TestAIDaysAreHalfOpen(t *testing.T) {
	tests := []struct {
		name, from, to, wantFrom, wantTo, wantLastMonth string
	}{
		{"a month from midnight to midnight", "2026-11-01T00:00:00Z", "2026-12-01T00:00:00Z", "2026-11-01", "2026-12-01", "2026-11"},
		{"a part day at either end", "2026-11-03T12:00:00Z", "2026-11-10T12:00:00Z", "2026-11-04", "2026-11-11", "2026-11"},
		{"an offset is read in UTC", "2026-11-01T02:00:00+05:00", "2026-11-02T02:00:00+05:00", "2026-11-01", "2026-11-02", "2026-11"},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			repos := &fakeRepos{monthly: []repositories.AIMonthlyRow{{Provider: "claude", Month: tc.wantLastMonth, ActiveDays: 1}}}
			got, err := newService(t, repos).AIUsage("abc@arbisoft.com", utils.Period{From: tc.from, To: tc.to})
			if err != nil {
				t.Fatalf("AIUsage() error = %v", err)
			}
			for _, c := range repos.calls {
				if want := []interface{}{tc.wantFrom, tc.wantTo}; !reflect.DeepEqual(c.args[:2], want) {
					t.Errorf("%s got days %v, want %v", c.method, c.args[:2], want)
				}
			}
			if !got.ActiveInLastMonth {
				t.Errorf("ActiveInLastMonth = false, want true for activity in %s", tc.wantLastMonth)
			}
		})
	}
}

func TestAICostMapsTheRepositoryRows(t *testing.T) {
	tests := []struct {
		name, total, wantTotal string
	}{
		{"total from the repository", "1.25000000", "1.25000000"},
		{"no total row is zero", "", zeroCost},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			repos := &fakeRepos{
				modelCosts:   []repositories.AIModelCostRow{{Provider: "claude", Model: "claude-sonnet-5", InputTokens: 10, EstimatedCostUSD: "1.25000000"}},
				totalCost:    tc.total,
				monthlyCosts: []repositories.AIMonthlyCostRow{{Month: "2026-11", InputTokens: 10, CostUSD: "1.25000000"}},
			}
			got, err := newService(t, repos).AICost("abc@arbisoft.com", utils.Period{TimeFrame: "24h"})
			if err != nil {
				t.Fatalf("AICost() error = %v", err)
			}
			want := &AICostStats{StatsWindow: StatsWindow{Email: "abc@arbisoft.com", TimeFrame: "24h", From: fixedNow.Add(-24 * time.Hour), To: fixedNow},
				Models:                []AIModelCost{{Provider: "claude", Model: "claude-sonnet-5", InputTokens: 10, EstimatedCostUSD: "1.25000000"}},
				TotalEstimatedCostUSD: tc.wantTotal,
				Monthly:               []AIMonthlyCost{{Month: "2026-11", InputTokens: 10, CostUSD: "1.25000000"}}}
			if !reflect.DeepEqual(got, want) {
				t.Errorf("AICost() = %+v, want %+v", got, want)
			}
		})
	}
}

func TestStatsWithNoDataAreZeroAndNeverNull(t *testing.T) {
	s := newService(t, &fakeRepos{})

	jira, err := s.Jira("abc@arbisoft.com", utils.Period{TimeFrame: "24h"})
	if err != nil || jira.ByType == nil || jira.ByTeam == nil || jira.IssuesResolved != 0 {
		t.Errorf("Jira() = %+v, %v; want zeros and empty lists", jira, err)
	}
	github, err := s.GitHub("abc@arbisoft.com", utils.Period{TimeFrame: "24h"})
	if err != nil || github.ByRepo == nil || github.ByTeam == nil || github.Reviews != 0 {
		t.Errorf("GitHub() = %+v, %v; want zeros and empty lists", github, err)
	}
	usage, err := s.AIUsage("abc@arbisoft.com", utils.Period{TimeFrame: "24h"})
	if err != nil || usage.Providers == nil || usage.Monthly == nil || len(usage.Providers)+len(usage.Monthly) != 0 {
		t.Errorf("AIUsage() = %+v, %v; want empty, non-nil lists", usage, err)
	}
	cost, err := s.AICost("abc@arbisoft.com", utils.Period{TimeFrame: "24h"})
	if err != nil || cost.Models == nil || cost.Monthly == nil || cost.TotalEstimatedCostUSD != zeroCost {
		t.Errorf("AICost() = %+v, %v; want empty lists and a zero total", cost, err)
	}
}
