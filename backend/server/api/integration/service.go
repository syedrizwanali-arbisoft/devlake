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
	"net/mail"
	"strings"
	"time"

	"github.com/apache/incubator-devlake/core/dal"
	"github.com/apache/incubator-devlake/core/errors"
)

const (
	// maxEmailLength is the longest address SMTP allows.
	maxEmailLength = 320
	// dayLayout formats the dates the daily AI tables store.
	dayLayout = "2006-01-02"
	// zeroCost is the total for an engineer with no AI cost records.
	zeroCost = "0.00000000"
)

// rowsReader is the one DAL method the service needs. dal.Dal satisfies it.
type rowsReader interface {
	All(dst interface{}, clauses ...dal.Clause) errors.Error
}

var _ rowsReader = (dal.Dal)(nil)

// StatsService reads an engineer's stats from the domain tables. It is safe for concurrent use.
type StatsService struct {
	db  rowsReader
	now func() time.Time
}

// Option configures a StatsService.
type Option func(*StatsService)

// WithClock sets the clock that ends every window. The default is time.Now.
func WithClock(now func() time.Time) Option {
	return func(s *StatsService) { s.now = now }
}

// NewStatsService returns a service that reads through db, which must not be nil.
func NewStatsService(db rowsReader, opts ...Option) (*StatsService, errors.Error) {
	if db == nil {
		return nil, errors.Default.New("stats service: nil db")
	}
	s := &StatsService{db: db, now: time.Now}
	for _, opt := range opts {
		opt(s)
	}
	if s.now == nil {
		return nil, errors.Default.New("stats service: nil clock")
	}
	return s, nil
}

// window validates the email and time frame and returns the response header. Errors are BadInput and name the field.
func (s *StatsService) window(email, timeFrame string) (StatsWindow, errors.Error) {
	normalized, err := normalizeEmail(email)
	if err != nil {
		return StatsWindow{}, err
	}
	tf, err := ParseTimeFrame(timeFrame)
	if err != nil {
		return StatsWindow{}, err
	}
	from, to := tf.Window(s.now())
	return StatsWindow{Email: normalized, TimeFrame: tf.String(), From: from, To: to}, nil
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

// Jira returns the Jira stats for email over timeFrame.
func (s *StatsService) Jira(email, timeFrame string) (*JiraStats, errors.Error) {
	w, err := s.window(email, timeFrame)
	if err != nil {
		return nil, err
	}
	var assigned []struct {
		IssuesResolved      int64
		StoryPointsResolved float64
		IssuesInProgress    int64
		IssuesTodo          int64
	}
	if err := s.db.All(&assigned,
		dal.Select(`COALESCE(SUM(i.resolution_date >= ? AND i.resolution_date < ?), 0) AS issues_resolved,
			COALESCE(SUM(CASE WHEN i.resolution_date >= ? AND i.resolution_date < ? THEN i.story_point END), 0) AS story_points_resolved,
			COALESCE(SUM(i.status = 'IN_PROGRESS'), 0) AS issues_in_progress,
			COALESCE(SUM(i.status = 'TODO'), 0) AS issues_todo`, w.From, w.To, w.From, w.To),
		dal.From("issues i JOIN accounts a ON a.id = i.assignee_id"),
		dal.Where("i.id LIKE 'jira:%' AND LOWER(a.email) = ?", w.Email),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read jira issues assigned to the engineer")
	}
	var created []struct{ IssuesCreated int64 }
	if err := s.db.All(&created,
		dal.Select("COUNT(*) AS issues_created"),
		dal.From("issues i JOIN accounts a ON a.id = i.creator_id"),
		dal.Where("i.id LIKE 'jira:%' AND LOWER(a.email) = ? AND i.created_date >= ? AND i.created_date < ?", w.Email, w.From, w.To),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read jira issues created by the engineer")
	}
	// Aggregates return one row; an empty slice (for example from a fake) means zeros.
	out := &JiraStats{StatsWindow: w}
	if len(assigned) > 0 {
		out.IssuesResolved = assigned[0].IssuesResolved
		out.StoryPointsResolved = assigned[0].StoryPointsResolved
		out.IssuesInProgress = assigned[0].IssuesInProgress
		out.IssuesTodo = assigned[0].IssuesTodo
	}
	if len(created) > 0 {
		out.IssuesCreated = created[0].IssuesCreated
	}
	return out, nil
}

// GitHub returns the GitHub stats for email over timeFrame.
func (s *StatsService) GitHub(email, timeFrame string) (*GitHubStats, errors.Error) {
	w, err := s.window(email, timeFrame)
	if err != nil {
		return nil, err
	}
	var commits []struct{ Commits, Additions, Deletions int64 }
	if err := s.db.All(&commits,
		dal.Select("COUNT(*) AS commits, COALESCE(SUM(c.additions), 0) AS additions, COALESCE(SUM(c.deletions), 0) AS deletions"),
		dal.From("commits c"),
		dal.Where(`LOWER(c.author_email) = ? AND c.authored_date >= ? AND c.authored_date < ?
			AND EXISTS (SELECT 1 FROM repo_commits rc WHERE rc.commit_sha = c.sha AND rc.repo_id LIKE 'github:%')`,
			w.Email, w.From, w.To),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read github commits")
	}
	var authored []struct{ PullRequestsOpened, PullRequestsMerged int64 }
	if err := s.db.All(&authored,
		dal.Select(`COALESCE(SUM(p.created_date >= ? AND p.created_date < ?), 0) AS pull_requests_opened,
			COALESCE(SUM(p.merged_date >= ? AND p.merged_date < ?), 0) AS pull_requests_merged`, w.From, w.To, w.From, w.To),
		dal.From("pull_requests p JOIN accounts a ON a.id = p.author_id"),
		dal.Where("p.id LIKE 'github:%' AND LOWER(a.email) = ?", w.Email),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read github pull requests authored")
	}
	var withCommits []struct{ PullRequestsWithCommits int64 }
	if err := s.db.All(&withCommits,
		dal.Select("COUNT(DISTINCT prc.pull_request_id) AS pull_requests_with_commits"),
		dal.From("pull_request_commits prc"),
		dal.Where(`prc.pull_request_id LIKE 'github:%' AND LOWER(prc.commit_author_email) = ?
			AND prc.commit_authored_date >= ? AND prc.commit_authored_date < ?`, w.Email, w.From, w.To),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read github pull requests with the engineer's commits")
	}
	var reviews []struct{ Reviews int64 }
	if err := s.db.All(&reviews,
		dal.Select("COUNT(*) AS reviews"),
		dal.From("pull_request_comments pc JOIN accounts a ON a.id = pc.account_id"),
		dal.Where(`pc.id LIKE 'github:%' AND pc.type = 'REVIEW' AND LOWER(a.email) = ?
			AND pc.created_date >= ? AND pc.created_date < ?`, w.Email, w.From, w.To),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read github reviews")
	}
	out := &GitHubStats{StatsWindow: w}
	if len(commits) > 0 {
		out.Commits, out.Additions, out.Deletions = commits[0].Commits, commits[0].Additions, commits[0].Deletions
	}
	if len(authored) > 0 {
		out.PullRequestsOpened, out.PullRequestsMerged = authored[0].PullRequestsOpened, authored[0].PullRequestsMerged
	}
	if len(withCommits) > 0 {
		out.PullRequestsWithCommits = withCommits[0].PullRequestsWithCommits
	}
	if len(reviews) > 0 {
		out.Reviews = reviews[0].Reviews
	}
	return out, nil
}

// days returns the inclusive day range the daily AI tables use for the window.
func days(w StatsWindow) (first, last string) {
	return w.From.Format(dayLayout), w.To.Format(dayLayout)
}

// AIUsage returns the AI usage stats for email over timeFrame, at day granularity.
func (s *StatsService) AIUsage(email, timeFrame string) (*AIUsageStats, errors.Error) {
	w, err := s.window(email, timeFrame)
	if err != nil {
		return nil, err
	}
	first, last := days(w)
	var providers []AIProviderUsage
	if err := s.db.All(&providers,
		dal.Select(`provider, COUNT(DISTINCT date) AS active_days, COALESCE(SUM(num_sessions), 0) AS sessions,
			COALESCE(SUM(suggestions_count), 0) AS suggestions, COALESCE(SUM(acceptance_count), 0) AS acceptances,
			COALESCE(SUM(lines_added), 0) AS lines_added, COALESCE(SUM(lines_removed), 0) AS lines_removed,
			COALESCE(SUM(commits_created), 0) AS commits_by_ai, COALESCE(SUM(prs_created), 0) AS pull_requests`),
		dal.From("ai_activities"),
		dal.Where("LOWER(user_email) = ? AND date >= ? AND date <= ?", w.Email, first, last),
		dal.Groupby("provider"),
		dal.Orderby("provider"),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read ai activities")
	}
	var decisions []struct{ ToolEditsAccepted, ToolEditsRejected int64 }
	if err := s.db.All(&decisions,
		dal.Select("COALESCE(SUM(accepted_count), 0) AS tool_edits_accepted, COALESCE(SUM(rejected_count), 0) AS tool_edits_rejected"),
		dal.From("ai_tool_decisions"),
		dal.Where("LOWER(user_email) = ? AND date >= ? AND date <= ?", w.Email, first, last),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read ai tool decisions")
	}
	out := &AIUsageStats{StatsWindow: w, Providers: providers}
	if out.Providers == nil {
		out.Providers = []AIProviderUsage{}
	}
	if len(decisions) > 0 {
		out.ToolEditsAccepted, out.ToolEditsRejected = decisions[0].ToolEditsAccepted, decisions[0].ToolEditsRejected
	}
	return out, nil
}

// aiCostRows is the per-model union both cost queries read: Claude from model usage records, and every other
// provider from daily activity, which is where Cursor reports cost. Claude activity rows carry no cost, so
// skipping them avoids counting anything twice. Costs are summed as DECIMAL so no float rounding reaches the API.
const aiCostRows = `(
	SELECT provider, model, SUM(input_tokens) AS input_tokens, SUM(output_tokens) AS output_tokens,
		SUM(cache_read_tokens) AS cache_read_tokens, SUM(cache_creation_tokens) AS cache_creation_tokens,
		SUM(estimated_cost_usd) AS cost
	FROM ai_model_usages WHERE LOWER(user_email) = ? AND date >= ? AND date <= ? GROUP BY provider, model
	UNION ALL
	SELECT provider, model, SUM(input_tokens), SUM(output_tokens), 0, 0, SUM(CAST(estimated_cost_usd AS DECIMAL(20,8)))
	FROM ai_activities WHERE provider <> 'claude' AND LOWER(user_email) = ? AND date >= ? AND date <= ? GROUP BY provider, model
) costs`

// AICost returns the AI cost stats for email over timeFrame, at day granularity.
func (s *StatsService) AICost(email, timeFrame string) (*AICostStats, errors.Error) {
	w, err := s.window(email, timeFrame)
	if err != nil {
		return nil, err
	}
	first, last := days(w)
	params := []interface{}{w.Email, first, last, w.Email, first, last}
	var models []AIModelCost
	if err := s.db.All(&models,
		dal.Select(`provider, model, COALESCE(SUM(input_tokens), 0) AS input_tokens, COALESCE(SUM(output_tokens), 0) AS output_tokens,
			COALESCE(SUM(cache_read_tokens), 0) AS cache_read_tokens, COALESCE(SUM(cache_creation_tokens), 0) AS cache_creation_tokens,
			CAST(CAST(COALESCE(SUM(cost), 0) AS DECIMAL(20,8)) AS CHAR) AS estimated_cost_usd`),
		dal.From(aiCostRows, params...),
		dal.Groupby("provider, model"),
		dal.Orderby("provider, model"),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read ai model costs")
	}
	var total []struct{ TotalEstimatedCostUsd string }
	if err := s.db.All(&total,
		dal.Select("CAST(CAST(COALESCE(SUM(cost), 0) AS DECIMAL(20,8)) AS CHAR) AS total_estimated_cost_usd"),
		dal.From(aiCostRows, params...),
	); err != nil {
		return nil, errors.Default.Wrap(err, "read ai total cost")
	}
	out := &AICostStats{StatsWindow: w, Models: models, TotalEstimatedCostUSD: zeroCost}
	if out.Models == nil {
		out.Models = []AIModelCost{}
	}
	if len(total) > 0 && total[0].TotalEstimatedCostUsd != "" {
		out.TotalEstimatedCostUSD = total[0].TotalEstimatedCostUsd
	}
	return out, nil
}
