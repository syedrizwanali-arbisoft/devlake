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
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models/common"
	"github.com/apache/incubator-devlake/server/api/shared"
)

// fakeStats records its input and returns canned results.
type fakeStats struct {
	email, timeFrame string
	err              errors.Error
}

func (f *fakeStats) record(email string, p Period) { f.email, f.timeFrame = email, p.TimeFrame }

func (f *fakeStats) Jira(email string, p Period) (*JiraStats, errors.Error) {
	f.record(email, p)
	if f.err != nil {
		return nil, f.err
	}
	return &JiraStats{StatsWindow: StatsWindow{Email: email, TimeFrame: p.TimeFrame}, IssuesResolved: 3}, nil
}

func (f *fakeStats) GitHub(email string, p Period) (*GitHubStats, errors.Error) {
	f.record(email, p)
	if f.err != nil {
		return nil, f.err
	}
	return &GitHubStats{StatsWindow: StatsWindow{Email: email}, Commits: 7}, nil
}

func (f *fakeStats) AIUsage(email string, p Period) (*AIUsageStats, errors.Error) {
	f.record(email, p)
	if f.err != nil {
		return nil, f.err
	}
	return &AIUsageStats{StatsWindow: StatsWindow{Email: email}, Providers: []AIProviderUsage{}}, nil
}

func (f *fakeStats) AICost(email string, p Period) (*AICostStats, errors.Error) {
	f.record(email, p)
	if f.err != nil {
		return nil, f.err
	}
	return &AICostStats{StatsWindow: StatsWindow{Email: email}, Models: []AIModelCost{}, TotalEstimatedCostUSD: "0.00000000"}, nil
}

// newTestRouter registers the routes as router.go does. viaAPIKey marks requests as RestAuthentication would.
func newTestRouter(t *testing.T, stats statsProvider, viaAPIKey bool) *gin.Engine {
	t.Helper()
	gin.SetMode(gin.TestMode)
	h, err := NewHandlers(stats)
	if err != nil {
		t.Fatalf("NewHandlers() error = %v", err)
	}
	r := gin.New()
	if viaAPIKey {
		r.Use(func(c *gin.Context) {
			c.Request = shared.SetRestAuthUser(c.Request, &common.User{Name: "integration-service"})
			c.Next()
		})
	}
	only := RequireAPIKey()
	r.GET("/is_valid", only, h.IsValid)
	r.GET("/stats/:email/jira", only, h.GetJiraStats)
	r.GET("/stats/:email/github", only, h.GetGitHubStats)
	r.GET("/stats/:email/ai_usage", only, h.GetAIUsageStats)
	r.GET("/stats/:email/ai_cost", only, h.GetAICostStats)
	return r
}

func get(r *gin.Engine, path string) *httptest.ResponseRecorder {
	resp := httptest.NewRecorder()
	r.ServeHTTP(resp, httptest.NewRequest(http.MethodGet, path, nil))
	return resp
}

func TestNewHandlersRejectsNil(t *testing.T) {
	if _, err := NewHandlers(nil); err == nil {
		t.Error("NewHandlers(nil) = nil error, want error")
	}
}

func TestIsValidAnswersTrueForAnAPIKey(t *testing.T) {
	resp := get(newTestRouter(t, &fakeStats{}, true), "/is_valid")

	if resp.Code != http.StatusOK || strings.TrimSpace(resp.Body.String()) != "true" {
		t.Errorf("GET /is_valid = %d %q, want 200 true", resp.Code, resp.Body.String())
	}
}

func TestStatsRoutesPassTheEmailAndTimeFrame(t *testing.T) {
	tests := []struct {
		path, wantKey string
	}{
		{"/stats/abc@arbisoft.com/jira?tf=1w", "issuesResolved"},
		{"/stats/abc@arbisoft.com/github?tf=1w", "commits"},
		{"/stats/abc@arbisoft.com/ai_usage?tf=1w", "providers"},
		{"/stats/abc@arbisoft.com/ai_cost?tf=1w", "totalEstimatedCostUsd"},
	}
	for _, tc := range tests {
		t.Run(tc.path, func(t *testing.T) {
			stats := &fakeStats{}

			resp := get(newTestRouter(t, stats, true), tc.path)

			if resp.Code != http.StatusOK {
				t.Fatalf("GET %s = %d %s, want 200", tc.path, resp.Code, resp.Body.String())
			}
			var body map[string]interface{}
			if err := json.Unmarshal(resp.Body.Bytes(), &body); err != nil {
				t.Fatalf("body isn't JSON: %v", err)
			}
			if _, ok := body[tc.wantKey]; !ok {
				t.Errorf("body = %v, want key %q", body, tc.wantKey)
			}
			if stats.email != "abc@arbisoft.com" || stats.timeFrame != "1w" {
				t.Errorf("service got (%q, %q), want (abc@arbisoft.com, 1w)", stats.email, stats.timeFrame)
			}
		})
	}
}

func TestStatsRoutesReturnTheErrorStatus(t *testing.T) {
	tests := []struct {
		name string
		err  errors.Error
		want int
	}{
		{"invalid input", errors.BadInput.New("tf must be a number from 1 to 999 followed by h, d, w, M or y"), http.StatusBadRequest},
		{"database failure", errors.Default.New("read github commits"), http.StatusInternalServerError},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			resp := get(newTestRouter(t, &fakeStats{err: tc.err}, true), "/stats/abc@arbisoft.com/github?tf=1D")

			if resp.Code != tc.want {
				t.Errorf("status = %d, want %d", resp.Code, tc.want)
			}
			if tc.want == http.StatusBadRequest && !strings.Contains(resp.Body.String(), "tf must be") {
				t.Errorf("body = %s, want the message naming tf", resp.Body.String())
			}
		})
	}
}

func TestRoutesRejectCallsWithoutAnAPIKey(t *testing.T) {
	for _, path := range []string{"/is_valid", "/stats/abc@arbisoft.com/jira?tf=1d", "/stats/abc@arbisoft.com/ai_cost?tf=1d"} {
		t.Run(path, func(t *testing.T) {
			stats := &fakeStats{}

			resp := get(newTestRouter(t, stats, false), path)

			if resp.Code != http.StatusUnauthorized || !strings.Contains(resp.Body.String(), "API key is required") {
				t.Errorf("GET %s = %d %s, want 401 asking for an API key", path, resp.Code, resp.Body.String())
			}
			if stats.email != "" {
				t.Error("the service ran for a request without an API key")
			}
		})
	}
}
