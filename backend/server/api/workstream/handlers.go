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

package workstream

import (
	"context"
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/server/api/shared"
)

// catalog is what the handlers need from Service.
type catalog interface {
	Load(ctx context.Context) (*LoadResult, errors.Error)
	List() (*ProjectList, errors.Error)
}

var _ catalog = (*Service)(nil)

// Handlers serves the project ingestion screen.
type Handlers struct {
	catalog catalog
}

// NewHandlers returns handlers backed by c, which must not be nil.
func NewHandlers(c catalog) (*Handlers, errors.Error) {
	if c == nil {
		return nil, errors.Default.New("workstream handlers: nil catalog")
	}
	return &Handlers{catalog: c}, nil
}

// LoadProjects loads every Workstream project through the Integration Service.
// @Summary Load projects and team leads from Workstream
// @Description Fetches every Workstream project through the Integration Service and saves it, with its team lead, by Workstream ID. Loading again updates existing rows. No DevLake projects are created.
// @Tags framework/workstream
// @Produce json
// @Success 200 {object} LoadResult
// @Failure 403 {object} shared.ApiBody "Administrator role required"
// @Failure 503 {object} shared.ApiBody "Integration Service not configured, unreachable, or rejected DevLake's credentials"
// @Failure 504 {object} shared.ApiBody "Integration Service timed out"
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /projects/load [post]
func (h *Handlers) LoadProjects(c *gin.Context) {
	result, err := h.catalog.Load(requestContext(c))
	if err != nil {
		shared.ApiOutputError(c, err)
		return
	}
	shared.ApiOutputSuccess(c, result, http.StatusOK)
}

// ListProjects lists the Workstream catalog.
// @Summary List projects loaded from Workstream
// @Description Every loaded Workstream project with its team lead and the DevLake project of the same name, if any.
// @Tags framework/workstream
// @Produce json
// @Success 200 {object} ProjectList
// @Failure 500 {object} shared.ApiBody "Internal Error"
// @Router /workstream-projects [get]
func (h *Handlers) ListProjects(c *gin.Context) {
	list, err := h.catalog.List()
	if err != nil {
		shared.ApiOutputError(c, err)
		return
	}
	shared.ApiOutputSuccess(c, list, http.StatusOK)
}

func requestContext(c *gin.Context) context.Context {
	if c != nil && c.Request != nil {
		return c.Request.Context()
	}
	return context.TODO()
}
