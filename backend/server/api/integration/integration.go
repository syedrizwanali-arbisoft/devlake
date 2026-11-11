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

	"github.com/apache/incubator-devlake/server/api/shared"
	"github.com/gin-gonic/gin"
)

// These endpoints are called by the Integration Service. They are skeletons:
// the behavior is defined later, so each one answers 501 Not Implemented.

func notImplemented(c *gin.Context) {
	shared.ApiOutputSuccess(c, shared.ApiBody{Success: false, Message: "not implemented"}, http.StatusNotImplemented)
}

// @Summary Check that an engineer email exists
// @Description Skeleton, not implemented yet
// @Tags integration
// @Param email query string true "engineer email"
// @Failure 501 {string} errcode.Error "Not Implemented"
// @Router /is_valid [get]
func IsValid(c *gin.Context) { notImplemented(c) }

// @Summary Jira stats for an engineer
// @Description Skeleton, not implemented yet
// @Tags integration
// @Param email path string true "engineer email"
// @Param tf query string false "time frame"
// @Param from query string false "start time"
// @Param to query string false "end time"
// @Failure 501 {string} errcode.Error "Not Implemented"
// @Router /stats/{email}/jira [get]
func GetJiraStats(c *gin.Context) { notImplemented(c) }

// @Summary GitHub stats for an engineer
// @Description Skeleton, not implemented yet
// @Tags integration
// @Param email path string true "engineer email"
// @Param tf query string false "time frame"
// @Param from query string false "start time"
// @Param to query string false "end time"
// @Failure 501 {string} errcode.Error "Not Implemented"
// @Router /stats/{email}/github [get]
func GetGithubStats(c *gin.Context) { notImplemented(c) }

// @Summary AI usage stats for an engineer
// @Description Skeleton, not implemented yet
// @Tags integration
// @Param email path string true "engineer email"
// @Param tf query string false "time frame"
// @Param from query string false "start time"
// @Param to query string false "end time"
// @Failure 501 {string} errcode.Error "Not Implemented"
// @Router /stats/{email}/ai_usage [get]
func GetAiUsageStats(c *gin.Context) { notImplemented(c) }

// @Summary AI cost stats for an engineer
// @Description Skeleton, not implemented yet
// @Tags integration
// @Param email path string true "engineer email"
// @Param tf query string false "time frame"
// @Param from query string false "start time"
// @Param to query string false "end time"
// @Failure 501 {string} errcode.Error "Not Implemented"
// @Router /stats/{email}/ai_cost [get]
func GetAiCostStats(c *gin.Context) { notImplemented(c) }
