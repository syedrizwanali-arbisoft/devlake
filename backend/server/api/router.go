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

package api

import (
	"fmt"
	"net/http"
	"strings"

	"github.com/apache/incubator-devlake/core/context"
	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/impls/logruslog"
	"github.com/apache/incubator-devlake/server/api/access"
	"github.com/apache/incubator-devlake/server/api/adminonly"
	"github.com/apache/incubator-devlake/server/api/apikeys"
	"github.com/apache/incubator-devlake/server/api/auth"
	"github.com/apache/incubator-devlake/server/api/store"

	"github.com/apache/incubator-devlake/core/plugin"
	"github.com/apache/incubator-devlake/server/api/blueprints"
	compliancescorecard "github.com/apache/incubator-devlake/server/api/compliance_scorecard"
	"github.com/apache/incubator-devlake/server/api/domainlayer"
	"github.com/apache/incubator-devlake/server/api/grafanausers"
	"github.com/apache/incubator-devlake/server/api/integration"
	"github.com/apache/incubator-devlake/server/api/pipelines"
	"github.com/apache/incubator-devlake/server/api/plugininfo"
	"github.com/apache/incubator-devlake/server/api/project"
	"github.com/apache/incubator-devlake/server/api/push"
	"github.com/apache/incubator-devlake/server/api/shared"
	"github.com/apache/incubator-devlake/server/api/task"
	userprojectmapping "github.com/apache/incubator-devlake/server/api/user_project_mapping"
	"github.com/apache/incubator-devlake/server/services"

	"github.com/gin-gonic/gin"
)

func RegisterRouter(r *gin.Engine, basicRes context.BasicRes) {
	r.GET("/pipelines", pipelines.Index)
	r.POST("/pipelines", pipelines.Post)
	r.GET("/pipelines/:pipelineId", pipelines.Get)
	r.DELETE("/pipelines/:pipelineId", pipelines.Delete)
	r.GET("/pipelines/:pipelineId/tasks", task.GetTaskByPipeline)
	r.GET("/pipelines/:pipelineId/subtasks", task.GetSubtaskByPipeline)
	r.POST("/pipelines/:pipelineId/rerun", pipelines.PostRerun)
	r.GET("/pipelines/:pipelineId/logging.tar.gz", pipelines.DownloadLogs)

	r.GET("/blueprints", blueprints.Index)
	r.POST("/blueprints", blueprints.Post)
	r.PATCH("/blueprints/:blueprintId", blueprints.Patch)
	r.DELETE("/blueprints/:blueprintId", blueprints.Delete)
	r.GET("/blueprints/:blueprintId", blueprints.Get)
	r.POST("/blueprints/:blueprintId/trigger", blueprints.Trigger)
	r.GET("/blueprints/:blueprintId/pipelines", blueprints.GetBlueprintPipelines)

	r.POST("/tasks/:taskId/rerun", task.PostRerun)

	r.POST("/push/:tableName", push.Post)
	r.GET("/domainlayer/repos", domainlayer.ReposIndex)

	// compliance scorecard api
	r.GET("/compliance-scorecard", compliancescorecard.Get)

	// plugin api
	r.GET("/plugininfo", plugininfo.Get)
	r.GET("/plugins", plugininfo.GetPluginMetas)

	// project api
	r.GET("/projects/:projectName", project.GetProject)
	r.GET("/projects/:projectName/check", project.GetProjectCheck)
	r.PATCH("/projects/:projectName", project.PatchProject)
	r.DELETE("/projects/:projectName", project.DeleteProject)
	r.POST("/projects", project.PostProject)
	r.GET("/projects", project.GetProjects)
	// on board api
	r.GET("/store/:storeKey", store.GetStore)
	r.PUT("/store/:storeKey", store.PutStore)

	// api keys api (admin-only)
	admin := adminonly.Require()
	r.GET("/api-keys", admin, apikeys.GetApiKeys)
	r.POST("/api-keys", admin, apikeys.PostApiKey)
	r.PUT("/api-keys/:apiKeyId", admin, apikeys.PutApiKey)
	r.DELETE("/api-keys/:apiKeyId", admin, apikeys.DeleteApiKey)

	// auth (OIDC and local user login)
	auth.RegisterRoutes(r)

	// fork-owned native OIDC access directory
	access.RegisterRoutes(r)
	grafanausers.RegisterRoutes(r)

	// integration service api (skeletons, behavior to be defined)
	r.GET("/is_valid", integration.IsValid)
	r.GET("/stats/:email/jira", integration.GetJiraStats)
	r.GET("/stats/:email/github", integration.GetGithubStats)
	r.GET("/stats/:email/ai_usage", integration.GetAiUsageStats)
	r.GET("/stats/:email/ai_cost", integration.GetAiCostStats)

	// user project mapping api
	// admin-only: customer-admin sessions; API keys can't manage keys or project access
	r.GET("/user-project-mappings", admin, userprojectmapping.GetAllMappings)
	r.GET("/user-project-mappings/:userLogin", admin, userprojectmapping.GetMappingsByUser)
	r.POST("/user-project-mappings/:userLogin", admin, userprojectmapping.PostMapping)
	r.DELETE("/user-project-mappings/:userLogin/:projectName", admin, userprojectmapping.DeleteMapping)

	// mount all api resources for all plugins
	resources, err := services.GetPluginsApiResources()
	if err != nil {
		panic(err)
	}
	// mount all api resources for all plugins
	for pluginName, apiResources := range resources {
		registerPluginEndpoints(r, basicRes, pluginName, apiResources)
	}
}

func registerPluginEndpoints(r *gin.Engine, basicRes context.BasicRes, pluginName string, apiResources map[string]map[string]plugin.ApiResourceHandler) {
	for resourcePath, resourceHandlers := range apiResources {
		for method, h := range resourceHandlers {
			r.Handle(
				method,
				fmt.Sprintf("/plugins/%s/%s", pluginName, resourcePath),
				handlePluginCall(basicRes, pluginName, h),
			)
		}
	}
}

func handlePluginCall(basicRes context.BasicRes, pluginName string, handler plugin.ApiResourceHandler) func(c *gin.Context) {
	return func(c *gin.Context) {
		var err errors.Error
		input := &plugin.ApiResourceInput{}
		input.Params = make(map[string]string)
		if len(c.Params) > 0 {
			for _, param := range c.Params {
				input.Params[param.Key] = param.Value
			}
		}
		input.Params["plugin"] = pluginName
		input.Query = c.Request.URL.Query()
		user, exist := shared.GetUser(c)
		if !exist {
			basicRes.GetLogger().Debug("user doesn't exist")
		} else {
			input.User = user
		}
		// When access management is disabled, IsCustomerAdmin stays false for every
		// request, so every plugin endpoint gated on it is permanently forbidden rather
		// than left unrestricted. This is deliberate: there is no role source of truth
		// to consult otherwise, and failing closed on a data-access boundary is correct
		// even though it makes such an endpoint unreachable.
		if accessService := access.Default(); accessService != nil && accessService.Enabled() {
			principal, principalErr := accessService.CurrentPrincipal(c)
			input.IsCustomerAdmin = principalErr == nil && principal.Role == access.RoleCustomerAdmin
		}
		if c.Request.Body != nil {
			contentType := c.Request.Header.Get("Content-Type")
			if strings.HasPrefix(contentType, "multipart/form-data;") || strings.HasPrefix(contentType, "application/x-protobuf") {
				input.Request = c.Request
			} else {
				shouldBindJSONErr := c.ShouldBindJSON(&input.Body)
				if shouldBindJSONErr != nil && shouldBindJSONErr.Error() != "EOF" {
					shared.ApiOutputError(c, errors.BadInput.Wrap(shouldBindJSONErr, shared.BadRequestBody))
					return
				}
			}
		}
		output, err := handler(input)
		if err != nil {
			err = shared.NormalizeError(err)
			if output != nil && output.Body != nil {
				logruslog.Global.Error(err, "")
				// helpers build these bodies from err.Error(), which carries the
				// full wrap chain; keep only the client-safe message
				if body, ok := output.Body.(*shared.ApiBody); ok {
					body.Message = shared.SafeErrorMessage(err)
					body.Causes = nil
				}
				shared.ApiOutputSuccess(c, output.Body, err.GetType().GetHttpCode())
			} else {
				shared.ApiOutputError(c, err)
			}
		} else if output != nil {
			status := output.Status
			if status < http.StatusContinue {
				status = http.StatusOK
			}
			if output.Header != nil {
				for k, vs := range output.Header {
					for _, v := range vs {
						c.Header(k, v)
					}
				}
			}
			if output.File != nil {
				c.Data(status, output.File.ContentType, output.File.Data)
				return
			}
			if blob, ok := output.Body.([]byte); ok && output.ContentType != "" {
				c.Data(status, output.ContentType, blob)
			} else {
				shared.ApiOutputSuccess(c, output.Body, status)
			}
		} else {
			shared.ApiOutputSuccess(c, nil, http.StatusOK)
		}
	}
}
