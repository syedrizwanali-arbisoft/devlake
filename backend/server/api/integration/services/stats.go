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
	"math"
	"net/mail"
	"sort"
	"strings"
	"time"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/server/api/integration/repositories"
	"github.com/apache/incubator-devlake/server/api/integration/utils"
)

const (
	// maxEmailLength is the longest address SMTP allows.
	maxEmailLength = 320
	// dayLayout formats the dates the daily AI tables store.
	dayLayout = "2006-01-02"
	// monthLayout formats the months the monthly AI rows carry.
	monthLayout = "2006-01"
	// zeroCost is the total for an engineer with no AI cost records.
	zeroCost = "0.00000000"
)

// ticketStatsReader reads the issues of an engineer's ticket tracker, such as Jira. repositories.Issues satisfies it.
type ticketStatsReader interface {
	Assigned(email string, from, to time.Time) (repositories.AssignedCounts, errors.Error)
	Created(email string, from, to time.Time) (int64, errors.Error)
	ResolvedByType(email string, from, to time.Time) ([]repositories.IssueTypeRow, errors.Error)
	ResolvedByTeam(email string, from, to time.Time) ([]repositories.IssueTeamRow, errors.Error)
}

// versionControlStatsReader reads the work in an engineer's code host, such as GitHub. repositories.Code satisfies it.
type versionControlStatsReader interface {
	Commits(email string, from, to time.Time) (repositories.CommitCounts, errors.Error)
	Authored(email string, from, to time.Time) (repositories.AuthoredCounts, errors.Error)
	PullRequestsWithCommits(email string, from, to time.Time) (int64, errors.Error)
	Reviews(email string, from, to time.Time) (repositories.ReviewCounts, errors.Error)
	CommitsByGroup(email string, from, to time.Time, group repositories.Group) ([]repositories.CommitGroupRow, errors.Error)
	PullRequestsByGroup(email string, from, to time.Time, group repositories.Group) ([]repositories.PullRequestGroupRow, errors.Error)
}

// aiReader reads AI activity and cost over a half-open range of days, [fromDay, toDay). repositories.AI satisfies it.
type aiReader interface {
	Providers(email, fromDay, toDay string) ([]repositories.AIProviderRow, errors.Error)
	ToolDecisions(email, fromDay, toDay string) (repositories.AIToolDecisionCounts, errors.Error)
	Monthly(email, fromDay, toDay string) ([]repositories.AIMonthlyRow, errors.Error)
	ModelCosts(email, fromDay, toDay string) ([]repositories.AIModelCostRow, errors.Error)
	TotalCost(email, fromDay, toDay string) (string, errors.Error)
	MonthlyCosts(email, fromDay, toDay string) ([]repositories.AIMonthlyCostRow, errors.Error)
}

var (
	_ ticketStatsReader         = (*repositories.Issues)(nil)
	_ versionControlStatsReader = (*repositories.Code)(nil)
	_ aiReader                  = (*repositories.AI)(nil)
)

// StatsService turns an engineer's email and period into stats: it validates the input, resolves the window, reads
// through the repositories and shapes the response. It is safe for concurrent use.
type StatsService struct {
	ticketStats         ticketStatsReader
	versionControlStats versionControlStatsReader
	ai                  aiReader
	now                 func() time.Time
}

// Option configures a StatsService.
type Option func(*StatsService)

// WithClock sets the clock that ends every window. The default is time.Now.
func WithClock(now func() time.Time) Option {
	return func(s *StatsService) { s.now = now }
}

// NewStatsService returns a service that reads through the given repositories, none of which may be nil.
func NewStatsService(ticketStats ticketStatsReader, versionControlStats versionControlStatsReader, ai aiReader, opts ...Option) (*StatsService, errors.Error) {
	if ticketStats == nil || versionControlStats == nil || ai == nil {
		return nil, errors.Default.New("stats service: nil repository")
	}
	s := &StatsService{ticketStats: ticketStats, versionControlStats: versionControlStats, ai: ai, now: time.Now}
	for _, opt := range opts {
		opt(s)
	}
	if s.now == nil {
		return nil, errors.Default.New("stats service: nil clock")
	}
	return s, nil
}

// window validates the email and period and returns the response header. Errors are BadInput and name the field.
func (s *StatsService) window(email string, p utils.Period) (StatsWindow, errors.Error) {
	normalized, err := normalizeEmail(email)
	if err != nil {
		return StatsWindow{}, err
	}
	from, to, timeFrame, err := p.Resolve(s.now())
	if err != nil {
		return StatsWindow{}, err
	}
	return StatsWindow{Email: normalized, TimeFrame: timeFrame, From: from, To: to}, nil
}

// normalizeEmail accepts a bare address only and lowercases it, because the stored emails are case-sensitive.
func normalizeEmail(email string) (string, errors.Error) {
	trimmed := strings.TrimSpace(email)
	address, err := mail.ParseAddress(trimmed)
	if err != nil || address.Address != trimmed || address.Name != "" || len(trimmed) > maxEmailLength {
		return "", errors.BadInput.New("email must be a valid email address")
	}
	return strings.ToLower(trimmed), nil
}

// Jira returns the Jira stats for email over the period.
func (s *StatsService) Jira(email string, p utils.Period) (*JiraStats, errors.Error) {
	w, err := s.window(email, p)
	if err != nil {
		return nil, err
	}
	assigned, err := s.ticketStats.Assigned(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	created, err := s.ticketStats.Created(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	byType, err := s.ticketStats.ResolvedByType(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	byTeam, err := s.ticketStats.ResolvedByTeam(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	return &JiraStats{
		StatsWindow:         w,
		IssuesResolved:      assigned.IssuesResolved,
		StoryPointsResolved: assigned.StoryPointsResolved,
		IssuesCreated:       created,
		IssuesInProgress:    assigned.IssuesInProgress,
		IssuesTodo:          assigned.IssuesTodo,
		AvgResolutionHours:  assigned.AvgResolutionHours,
		ByType:              convert(byType, func(r repositories.IssueTypeRow) JiraTypeStats { return JiraTypeStats(r) }),
		ByTeam:              convert(byTeam, func(r repositories.IssueTeamRow) JiraTeamStats { return JiraTeamStats(r) }),
	}, nil
}

// GitHub returns the GitHub stats for email over the period.
func (s *StatsService) GitHub(email string, p utils.Period) (*GitHubStats, errors.Error) {
	w, err := s.window(email, p)
	if err != nil {
		return nil, err
	}
	commits, err := s.versionControlStats.Commits(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	authored, err := s.versionControlStats.Authored(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	withCommits, err := s.versionControlStats.PullRequestsWithCommits(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	reviews, err := s.versionControlStats.Reviews(w.Email, w.From, w.To)
	if err != nil {
		return nil, err
	}
	byRepo, err := s.githubBreakdown(w, repositories.GroupByRepo)
	if err != nil {
		return nil, err
	}
	byTeam, err := s.githubBreakdown(w, repositories.GroupByTeam)
	if err != nil {
		return nil, err
	}
	return &GitHubStats{
		StatsWindow:             w,
		Commits:                 commits.Commits,
		Additions:               commits.Additions,
		Deletions:               commits.Deletions,
		PullRequestsOpened:      authored.PullRequestsOpened,
		PullRequestsMerged:      authored.PullRequestsMerged,
		PullRequestsWithCommits: withCommits,
		Reviews:                 reviews.Reviews,
		ReviewComments:          reviews.ReviewComments,
		PullRequestsReviewed:    reviews.PullRequestsReviewed,
		AvgPrMergeHours:         authored.AvgPrMergeHours,
		ByRepo:                  byRepo,
		ByTeam:                  byTeam,
	}, nil
}

// githubBreakdown merges the commit and pull request rows of each group into one entry, sorted by name, and works
// out the engineer's share of each group.
func (s *StatsService) githubBreakdown(w StatsWindow, group repositories.Group) ([]GitHubBreakdown, errors.Error) {
	commits, err := s.versionControlStats.CommitsByGroup(w.Email, w.From, w.To, group)
	if err != nil {
		return nil, err
	}
	prs, err := s.versionControlStats.PullRequestsByGroup(w.Email, w.From, w.To, group)
	if err != nil {
		return nil, err
	}
	return mergeBreakdown(commits, prs), nil
}

// mergeBreakdown joins commit and pull request rows on the group name and keeps the groups where the engineer
// committed, opened or merged something. A group's totals come from every author's work in it, whichever kind of work
// the engineer did there.
func mergeBreakdown(commits []repositories.CommitGroupRow, prs []repositories.PullRequestGroupRow) []GitHubBreakdown {
	byName := make(map[string]*GitHubBreakdown, len(commits)+len(prs))
	row := func(name string) *GitHubBreakdown {
		if r, ok := byName[name]; ok {
			return r
		}
		r := &GitHubBreakdown{Name: name}
		byName[name] = r
		return r
	}
	for _, c := range commits {
		r := row(c.Name)
		r.Commits, r.LinesChanged = c.EngineerCommits, c.EngineerLines
		r.RepoCommits, r.RepoLinesChanged, r.Contributors = c.RepoCommits, c.RepoLines, c.Contributors
	}
	for _, p := range prs {
		r := row(p.Name)
		r.PullRequestsOpened, r.PullRequestsMerged = p.EngineerOpened, p.EngineerMerged
		r.RepoPullRequestsMerged = p.RepoMerged
	}
	out := make([]GitHubBreakdown, 0, len(byName))
	for _, r := range byName {
		if r.Commits == 0 && r.PullRequestsOpened == 0 && r.PullRequestsMerged == 0 {
			continue
		}
		r.CommitSharePct = sharePct(r.Commits, r.RepoCommits)
		r.LinesSharePct = sharePct(r.LinesChanged, r.RepoLinesChanged)
		r.PullRequestsMergedSharePct = sharePct(r.PullRequestsMerged, r.RepoPullRequestsMerged)
		out = append(out, *r)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// sharePct returns part as a percentage of total with one decimal, or 0 when total is 0.
func sharePct(part, total int64) float64 {
	if total <= 0 {
		return 0
	}
	return math.Round(float64(part)*1000/float64(total)) / 10
}

// convert maps rows with to, returning an empty slice for none so the JSON is [] and never null.
func convert[R, D any](rows []R, to func(R) D) []D {
	out := make([]D, 0, len(rows))
	for _, r := range rows {
		out = append(out, to(r))
	}
	return out
}

// days returns the half-open range of UTC days, [fromDay, toDay), that the daily AI tables use for the window. A day
// is in the range when it starts inside the window. So a window from one midnight to another covers exactly those
// days, and back-to-back windows never share a day.
func days(w StatsWindow) (fromDay, toDay time.Time) {
	return ceilDay(w.From), ceilDay(w.To)
}

// ceilDay returns the first UTC midnight at or after t.
func ceilDay(t time.Time) time.Time {
	day := t.UTC().Truncate(24 * time.Hour)
	if day.Before(t) {
		day = day.AddDate(0, 0, 1)
	}
	return day
}

// AIUsage returns the AI usage stats for email over the period, at day granularity.
func (s *StatsService) AIUsage(email string, p utils.Period) (*AIUsageStats, errors.Error) {
	w, err := s.window(email, p)
	if err != nil {
		return nil, err
	}
	start, end := days(w)
	fromDay, toDay := start.Format(dayLayout), end.Format(dayLayout)
	providers, err := s.ai.Providers(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	decisions, err := s.ai.ToolDecisions(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	monthly, err := s.ai.Monthly(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	out := &AIUsageStats{
		StatsWindow:       w,
		Providers:         convert(providers, func(r repositories.AIProviderRow) AIProviderUsage { return AIProviderUsage(r) }),
		ToolEditsAccepted: decisions.ToolEditsAccepted,
		ToolEditsRejected: decisions.ToolEditsRejected,
		Monthly:           convert(monthly, func(r repositories.AIMonthlyRow) AIMonthlyUsage { return AIMonthlyUsage(r) }),
	}
	// The last month is the month of the last day in the range, so a November range is judged on November.
	lastMonth := end.AddDate(0, 0, -1).Format(monthLayout)
	for _, m := range out.Monthly {
		if m.Month == lastMonth {
			out.ActiveInLastMonth = true
			break
		}
	}
	return out, nil
}

// AICost returns the AI cost stats for email over the period, at day granularity.
func (s *StatsService) AICost(email string, p utils.Period) (*AICostStats, errors.Error) {
	w, err := s.window(email, p)
	if err != nil {
		return nil, err
	}
	start, end := days(w)
	fromDay, toDay := start.Format(dayLayout), end.Format(dayLayout)
	models, err := s.ai.ModelCosts(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	total, err := s.ai.TotalCost(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	monthly, err := s.ai.MonthlyCosts(w.Email, fromDay, toDay)
	if err != nil {
		return nil, err
	}
	if total == "" {
		total = zeroCost
	}
	return &AICostStats{
		StatsWindow:           w,
		Models:                convert(models, func(r repositories.AIModelCostRow) AIModelCost { return AIModelCost(r) }),
		TotalEstimatedCostUSD: total,
		Monthly:               convert(monthly, func(r repositories.AIMonthlyCostRow) AIMonthlyCost { return AIMonthlyCost(r) }),
	}, nil
}
