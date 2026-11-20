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

import "time"

// StatsWindow identifies whose stats a response holds and the window they cover.
type StatsWindow struct {
	// Email is the requested email, lowercased.
	Email string `json:"email" example:"abc@arbisoft.com"`
	// TimeFrame is the tf query parameter as given. It is left out when the request used from and to.
	TimeFrame string `json:"timeFrame,omitempty" example:"1w"`
	// From is the inclusive start of the window, RFC 3339 in UTC.
	From time.Time `json:"from" example:"2026-11-03T09:30:00Z"`
	// To is the exclusive end of the window, RFC 3339 in UTC.
	To time.Time `json:"to" example:"2026-11-10T09:30:00Z"`
}

// JiraStats summarizes Jira issues for the Jira accounts whose email matches. Jira hides most account emails, so
// an engineer whose account has none gets zero counts.
type JiraStats struct {
	StatsWindow
	// IssuesResolved counts issues assigned to the engineer, resolved in the window and still DONE.
	IssuesResolved int64 `json:"issuesResolved"`
	// StoryPointsResolved sums the story points of those resolved issues.
	StoryPointsResolved float64 `json:"storyPointsResolved"`
	// IssuesCreated counts issues the engineer created in the window.
	IssuesCreated int64 `json:"issuesCreated"`
	// IssuesInProgress counts issues assigned to the engineer that are in progress now, whatever the window.
	IssuesInProgress int64 `json:"issuesInProgress"`
	// IssuesTodo counts issues assigned to the engineer that are still to do now, whatever the window.
	IssuesTodo int64 `json:"issuesTodo"`
	// AvgResolutionHours averages created-to-resolved time of the issues resolved in the window. Zero without any.
	AvgResolutionHours float64 `json:"avgResolutionHours"`
	// ByType splits the resolved issues by ticket type. It is empty, never null, without data.
	ByType []JiraTypeStats `json:"byType"`
	// ByTeam splits the resolved issues by DevLake project, so an engineer on several teams appears in each.
	ByTeam []JiraTeamStats `json:"byTeam"`
}

// JiraTypeStats is the resolved work of one ticket type.
type JiraTypeStats struct {
	IssueType      string  `json:"type" example:"BUG"`
	IssuesResolved int64   `json:"issuesResolved"`
	StoryPoints    float64 `json:"storyPoints"`
}

// JiraTeamStats is the resolved work in one DevLake project.
type JiraTeamStats struct {
	Team           string  `json:"team" example:"Akina"`
	IssuesResolved int64   `json:"issuesResolved"`
	StoryPoints    float64 `json:"storyPoints"`
}

// GitHubStats summarizes GitHub work. Commit counts use the commit author email, which is reliable. Pull request
// authorship and reviews need the GitHub account's email, which many accounts hide, so PullRequestsWithCommits
// counts pull requests that contain the engineer's commits as well.
type GitHubStats struct {
	StatsWindow
	// Commits counts commits authored in the window in GitHub repositories.
	Commits int64 `json:"commits"`
	// Additions sums the lines added by those commits.
	Additions int64 `json:"additions"`
	// Deletions sums the lines deleted by those commits.
	Deletions int64 `json:"deletions"`
	// PullRequestsOpened counts pull requests the engineer's account opened in the window.
	PullRequestsOpened int64 `json:"pullRequestsOpened"`
	// PullRequestsMerged counts pull requests the engineer's account authored that were merged in the window.
	PullRequestsMerged int64 `json:"pullRequestsMerged"`
	// PullRequestsWithCommits counts pull requests with a commit the engineer authored in the window.
	PullRequestsWithCommits int64 `json:"pullRequestsWithCommits"`
	// Reviews counts pull request reviews the engineer's account submitted in the window on pull requests someone
	// else authored.
	Reviews int64 `json:"reviews"`
	// ReviewComments counts the normal and inline comments the engineer's account wrote in the window on pull
	// requests someone else authored. Review submissions are left out, because Reviews counts them.
	ReviewComments int64 `json:"reviewComments"`
	// PullRequestsReviewed counts distinct pull requests someone else authored that the engineer's account reviewed
	// or commented on in the window. A pull request with both counts once.
	PullRequestsReviewed int64 `json:"pullRequestsReviewed"`
	// AvgPrMergeHours averages created-to-merged time of the engineer's pull requests merged in the window.
	AvgPrMergeHours float64 `json:"avgPrMergeHours"`
	// ByRepo splits commits and pull requests by repository. It is empty, never null, without data.
	ByRepo []GitHubBreakdown `json:"byRepo"`
	// ByTeam splits them by DevLake project.
	ByTeam []GitHubBreakdown `json:"byTeam"`
}

// GitHubBreakdown is the engineer's GitHub work in one repository or project, next to what everyone did there. It
// lists only the groups where the engineer committed, opened or merged something in the window. The shares are
// percentages with one decimal, 0 when the group has no such work.
type GitHubBreakdown struct {
	Name               string `json:"name" example:"arbisoft/devlake"`
	Commits            int64  `json:"commits"`
	PullRequestsOpened int64  `json:"pullRequestsOpened"`
	PullRequestsMerged int64  `json:"pullRequestsMerged"`
	// LinesChanged sums the engineer's additions and deletions.
	LinesChanged int64 `json:"linesChanged"`
	// RepoCommits, RepoLinesChanged and RepoPullRequestsMerged are everyone's totals in the window, including in a
	// group where the engineer did only one kind of work.
	RepoCommits            int64 `json:"repoCommits"`
	RepoLinesChanged       int64 `json:"repoLinesChanged"`
	RepoPullRequestsMerged int64 `json:"repoPullRequestsMerged"`
	// Contributors counts the distinct commit author emails in the group in the window.
	Contributors               int64   `json:"contributors"`
	CommitSharePct             float64 `json:"commitSharePct"`
	LinesSharePct              float64 `json:"linesSharePct"`
	PullRequestsMergedSharePct float64 `json:"pullRequestsMergedSharePct"`
}

// AIProviderUsage is one AI provider's activity for the engineer. AI data is daily, so the window covers the whole
// UTC days that start at or after From and before To. A range from 2026-11-01T00:00:00Z to 2026-12-01T00:00:00Z
// covers November only.
type AIProviderUsage struct {
	Provider     string `json:"provider" example:"cursor"`
	ActiveDays   int64  `json:"activeDays"`
	Sessions     int64  `json:"sessions"`
	Suggestions  int64  `json:"suggestions"`
	Acceptances  int64  `json:"acceptances"`
	LinesAdded   int64  `json:"linesAdded"`
	LinesRemoved int64  `json:"linesRemoved"`
	CommitsByAI  int64  `json:"commitsByAi"`
	PullRequests int64  `json:"pullRequests"`
}

// AIUsageStats lists AI activity per provider, plus tool decisions where the provider reports them (Claude).
type AIUsageStats struct {
	StatsWindow
	// Providers has one entry per provider with activity, sorted by name. It is empty, never null, without data.
	Providers []AIProviderUsage `json:"providers"`
	// ToolEditsAccepted counts accepted file edits and writes.
	ToolEditsAccepted int64 `json:"toolEditsAccepted"`
	// ToolEditsRejected counts rejected file edits and writes.
	ToolEditsRejected int64 `json:"toolEditsRejected"`
	// Monthly is the activity per provider and UTC month, oldest first. It is empty, never null, without data.
	Monthly []AIMonthlyUsage `json:"monthly"`
	// ActiveInLastMonth is true when the engineer has activity in the UTC month of the last day the window covers.
	ActiveInLastMonth bool `json:"activeInLastMonth"`
}

// AIMonthlyUsage is one provider's activity in one UTC month.
type AIMonthlyUsage struct {
	Provider   string `json:"provider" example:"claude"`
	Month      string `json:"month" example:"2026-11"`
	ActiveDays int64  `json:"activeDays"`
	Sessions   int64  `json:"sessions"`
	// Suggestions sums the suggestions the provider made in the month.
	Suggestions int64 `json:"suggestions"`
	// Acceptances sums the suggestions the engineer accepted in the month.
	Acceptances int64 `json:"acceptances"`
}

// AIModelCost is the token use and estimated cost of one model. Costs are decimal strings in US dollars.
type AIModelCost struct {
	Provider            string `json:"provider" example:"claude"`
	Model               string `json:"model" example:"claude-sonnet-5"`
	InputTokens         int64  `json:"inputTokens"`
	OutputTokens        int64  `json:"outputTokens"`
	CacheReadTokens     int64  `json:"cacheReadTokens"`
	CacheCreationTokens int64  `json:"cacheCreationTokens"`
	EstimatedCostUSD    string `json:"estimatedCostUsd" example:"12.12764220"`
}

// AICostStats lists AI cost per model. Claude comes from model usage records; other providers from daily activity.
type AICostStats struct {
	StatsWindow
	// Models has one entry per provider and model, sorted. It is empty, never null, without data.
	Models []AIModelCost `json:"models"`
	// TotalEstimatedCostUSD sums every model's estimated cost, as a decimal string in US dollars.
	TotalEstimatedCostUSD string `json:"totalEstimatedCostUsd" example:"12.12764220"`
	// Monthly is the token use and cost per UTC month, oldest first. It is empty, never null, without data.
	Monthly []AIMonthlyCost `json:"monthly"`
}

// AIMonthlyCost is the token use and estimated cost of one UTC month. The cost is a decimal string in US dollars.
type AIMonthlyCost struct {
	Month        string `json:"month" example:"2026-11"`
	InputTokens  int64  `json:"inputTokens"`
	OutputTokens int64  `json:"outputTokens"`
	CostUSD      string `json:"estimatedCostUsd" example:"12.12764220"`
}
