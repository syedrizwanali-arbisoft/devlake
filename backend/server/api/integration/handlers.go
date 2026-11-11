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
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/server/api/shared"
)

// statsProvider is what the handlers need from StatsService. Each method validates its input and returns a
// BadInput error naming the field when it is wrong.
type statsProvider interface {
	Jira(email string, p Period) (*JiraStats, errors.Error)
	GitHub(email string, p Period) (*GitHubStats, errors.Error)
	AIUsage(email string, p Period) (*AIUsageStats, errors.Error)
	AICost(email string, p Period) (*AICostStats, errors.Error)
}

var _ statsProvider = (*StatsService)(nil)

const (
	emailParam     = "email"
	timeFrameParam = "tf"
	fromParam      = "from"
	toParam        = "to"
)

// Handlers serves the endpoints the Integration Service calls.
type Handlers struct {
	stats statsProvider
}

// NewHandlers returns handlers that read stats through stats, which must not be nil.
func NewHandlers(stats statsProvider) (*Handlers, errors.Error) {
	if stats == nil {
		return nil, errors.Default.New("integration handlers: nil stats provider")
	}
	return &Handlers{stats: stats}, nil
}

// IsValid answers true. RestAuthentication has already rejected a missing, unknown, expired or out-of-scope API
// key with 401 or 403, so reaching this handler proves the key is valid.
// @Summary Check the DevLake API key
// @Description Returns true when the API key in the Authorization header is valid for this path. Call it as /rest/is_valid.
// @Tags framework/integration
// @Produce json
// @Security ApiKeyAuth
// @Success 200 {boolean} boolean "true"
// @Failure 401 {object} shared.ApiBody "API key missing or malformed"
// @Failure 403 {object} shared.ApiBody "API key invalid, expired or out of scope"
// @Router /is_valid [get]
func (h *Handlers) IsValid(c *gin.Context) {
	shared.ApiOutputSuccess(c, true, http.StatusOK)
}

// GetJiraStats returns an engineer's Jira stats.
// @Summary Get an engineer's Jira stats
// @Description Jira issues resolved, created and open for the Jira account with this email, over the time frame. An email with no data returns zero counts. Call it as /rest/stats/{email}/jira.
// @Tags framework/integration
// @Produce json
// @Security ApiKeyAuth
// @Param email path string true "Engineer email" example(abc@arbisoft.com)
// @Param tf query string false "Time frame: 1-999 followed by h, d, w, M or y (case-sensitive). Give tf, or from and to." example(1w)
// @Param from query string false "Start of an explicit range, RFC 3339 with an offset, for example 2026-11-01T00:00:00Z"
// @Param to query string false "End of an explicit range, RFC 3339 with an offset"
// @Success 200 {object} JiraStats
// @Failure 400 {object} shared.ApiBody "Invalid email, tf or range"
// @Failure 401 {object} shared.ApiBody "API key missing"
// @Failure 403 {object} shared.ApiBody "API key invalid or out of scope"
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /stats/{email}/jira [get]
func (h *Handlers) GetJiraStats(c *gin.Context) {
	stats, err := h.stats.Jira(c.Param(emailParam), periodOf(c))
	respond(c, stats, err)
}

// GetGitHubStats returns an engineer's GitHub stats.
// @Summary Get an engineer's GitHub stats
// @Description Commits, pull requests and reviews in GitHub repositories for this email, over the time frame. An email with no data returns zero counts. Call it as /rest/stats/{email}/github.
// @Tags framework/integration
// @Produce json
// @Security ApiKeyAuth
// @Param email path string true "Engineer email" example(abc@arbisoft.com)
// @Param tf query string false "Time frame: 1-999 followed by h, d, w, M or y (case-sensitive). Give tf, or from and to." example(1w)
// @Param from query string false "Start of an explicit range, RFC 3339 with an offset, for example 2026-11-01T00:00:00Z"
// @Param to query string false "End of an explicit range, RFC 3339 with an offset"
// @Success 200 {object} GitHubStats
// @Failure 400 {object} shared.ApiBody "Invalid email, tf or range"
// @Failure 401 {object} shared.ApiBody "API key missing"
// @Failure 403 {object} shared.ApiBody "API key invalid or out of scope"
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /stats/{email}/github [get]
func (h *Handlers) GetGitHubStats(c *gin.Context) {
	stats, err := h.stats.GitHub(c.Param(emailParam), periodOf(c))
	respond(c, stats, err)
}

// GetAIUsageStats returns an engineer's AI usage stats.
// @Summary Get an engineer's AI usage stats
// @Description AI coding activity per provider and tool decisions for this email, over the time frame in whole UTC days. An email with no data returns an empty providers list. Call it as /rest/stats/{email}/ai_usage.
// @Tags framework/integration
// @Produce json
// @Security ApiKeyAuth
// @Param email path string true "Engineer email" example(abc@arbisoft.com)
// @Param tf query string false "Time frame: 1-999 followed by h, d, w, M or y (case-sensitive). Give tf, or from and to." example(30d)
// @Param from query string false "Start of an explicit range, RFC 3339 with an offset, for example 2026-11-01T00:00:00Z"
// @Param to query string false "End of an explicit range, RFC 3339 with an offset"
// @Success 200 {object} AIUsageStats
// @Failure 400 {object} shared.ApiBody "Invalid email, tf or range"
// @Failure 401 {object} shared.ApiBody "API key missing"
// @Failure 403 {object} shared.ApiBody "API key invalid or out of scope"
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /stats/{email}/ai_usage [get]
func (h *Handlers) GetAIUsageStats(c *gin.Context) {
	stats, err := h.stats.AIUsage(c.Param(emailParam), periodOf(c))
	respond(c, stats, err)
}

// GetAICostStats returns an engineer's AI cost stats.
// @Summary Get an engineer's AI cost stats
// @Description Token use and estimated cost per AI model for this email, over the time frame in whole UTC days. Costs are decimal strings in US dollars. Call it as /rest/stats/{email}/ai_cost.
// @Tags framework/integration
// @Produce json
// @Security ApiKeyAuth
// @Param email path string true "Engineer email" example(abc@arbisoft.com)
// @Param tf query string false "Time frame: 1-999 followed by h, d, w, M or y (case-sensitive). Give tf, or from and to." example(30d)
// @Param from query string false "Start of an explicit range, RFC 3339 with an offset, for example 2026-11-01T00:00:00Z"
// @Param to query string false "End of an explicit range, RFC 3339 with an offset"
// @Success 200 {object} AICostStats
// @Failure 400 {object} shared.ApiBody "Invalid email, tf or range"
// @Failure 401 {object} shared.ApiBody "API key missing"
// @Failure 403 {object} shared.ApiBody "API key invalid or out of scope"
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /stats/{email}/ai_cost [get]
func (h *Handlers) GetAICostStats(c *gin.Context) {
	stats, err := h.stats.AICost(c.Param(emailParam), periodOf(c))
	respond(c, stats, err)
}

// periodOf reads the period from the query: tf, or from and to.
func periodOf(c *gin.Context) Period {
	return Period{TimeFrame: c.Query(timeFrameParam), From: c.Query(fromParam), To: c.Query(toParam)}
}

// respond writes body, or the error with the status its kind carries.
func respond(c *gin.Context, body interface{}, err errors.Error) {
	if err != nil {
		shared.ApiOutputError(c, err)
		return
	}
	shared.ApiOutputSuccess(c, body, http.StatusOK)
}
