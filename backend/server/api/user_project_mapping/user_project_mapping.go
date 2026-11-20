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

package userprojectmapping

import (
	"net/http"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models"
	"github.com/apache/incubator-devlake/server/api/shared"
	"github.com/apache/incubator-devlake/server/services"

	"github.com/gin-gonic/gin"
)

// @Summary Get all user-project mappings
// @Description Returns all user-project mappings (admin view)
// @Tags framework/user-project-mappings
// @Success 200 {array} models.UserProjectMapping
// @Failure 500 {string} errcode.Error "Internal Error"
// @Router /user-project-mappings [get]
func GetAllMappings(c *gin.Context) {
	mappings, err := services.GetAllUserProjectMappings()
	if err != nil {
		shared.ApiOutputError(c, errors.Default.Wrap(err, "error getting user project mappings"))
		return
	}
	shared.ApiOutputSuccess(c, mappings, http.StatusOK)
}

// @Summary Get projects for a user
// @Description Returns all projects mapped to a Grafana user login
// @Tags framework/user-project-mappings
// @Param userLogin path string true "Grafana user login"
// @Success 200 {array} models.UserProjectMapping
// @Failure 500 {string} errcode.Error "Internal Error"
// @Router /user-project-mappings/{userLogin} [get]
func GetMappingsByUser(c *gin.Context) {
	userLogin := c.Param("userLogin")
	mappings, err := services.GetUserProjectMappings(userLogin)
	if err != nil {
		shared.ApiOutputError(c, errors.Default.Wrap(err, "error getting user project mappings"))
		return
	}
	shared.ApiOutputSuccess(c, mappings, http.StatusOK)
}

// @Summary Create a user-project mapping
// @Description Assigns a project to a Grafana user login. Creating a mapping that exists already succeeds. A customer admin's session can call it, and so can a REST API key whose allowed path covers it (call it as /rest/user-project-mappings/{userLogin}).
// @Tags framework/user-project-mappings
// @Accept application/json
// @Param userLogin path string true "Grafana user login"
// @Param body body object true "json"
// @Success 201 {object} models.UserProjectMapping
// @Failure 400 {string} errcode.Error "Bad Request, or the project doesn't exist"
// @Failure 403 {string} errcode.Error "Administrator role or an API key required"
// @Failure 500 {string} errcode.Error "Internal Error"
// @Router /user-project-mappings/{userLogin} [post]
func PostMapping(c *gin.Context) {
	userLogin := c.Param("userLogin")
	var body struct {
		ProjectName string `json:"projectName" binding:"required"`
	}
	if err := c.ShouldBindJSON(&body); err != nil {
		shared.ApiOutputError(c, errors.BadInput.Wrap(err, shared.BadRequestBody))
		return
	}
	mapping := &models.UserProjectMapping{
		UserLogin:   userLogin,
		ProjectName: body.ProjectName,
	}
	if err := services.CreateUserProjectMapping(mapping); err != nil {
		shared.ApiOutputError(c, errors.Default.Wrap(err, "error creating user project mapping"))
		return
	}
	shared.ApiOutputSuccess(c, mapping, http.StatusCreated)
}

// @Summary Delete a user-project mapping
// @Description Removes a project from a Grafana user login
// @Tags framework/user-project-mappings
// @Param userLogin path string true "Grafana user login"
// @Param projectName path string true "project name"
// @Success 200
// @Failure 500 {string} errcode.Error "Internal Error"
// @Router /user-project-mappings/{userLogin}/{projectName} [delete]
func DeleteMapping(c *gin.Context) {
	userLogin := c.Param("userLogin")
	projectName := c.Param("projectName")
	if err := services.DeleteUserProjectMapping(userLogin, projectName); err != nil {
		shared.ApiOutputError(c, errors.Default.Wrap(err, "error deleting user project mapping"))
		return
	}
	shared.ApiOutputSuccess(c, nil, http.StatusOK)
}
