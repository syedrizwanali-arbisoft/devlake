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
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/core/errors"
)

type fakeCatalog struct {
	result *LoadResult
	list   *ProjectList
	err    errors.Error
}

func (f fakeCatalog) Load(context.Context) (*LoadResult, errors.Error) { return f.result, f.err }
func (f fakeCatalog) List() (*ProjectList, errors.Error)               { return f.list, f.err }

func serve(t *testing.T, c catalog, method, path string) *httptest.ResponseRecorder {
	t.Helper()
	gin.SetMode(gin.TestMode)
	h, err := NewHandlers(c)
	if err != nil {
		t.Fatalf("NewHandlers() error = %v", err)
	}
	r := gin.New()
	r.POST("/projects/load", h.LoadProjects)
	r.GET("/workstream-projects", h.ListProjects)
	resp := httptest.NewRecorder()
	r.ServeHTTP(resp, httptest.NewRequest(method, path, nil))
	return resp
}

func TestLoadProjectsHandler(t *testing.T) {
	tests := []struct {
		name string
		c    fakeCatalog
		want int
	}{
		{"success", fakeCatalog{result: &LoadResult{Loaded: 3, Created: 1, Updated: 2}}, http.StatusOK},
		{"not configured", fakeCatalog{err: errors.Unavailable.New("loading from Workstream needs INTEGRATION_SERVICE_URL")}, http.StatusServiceUnavailable},
		{"timeout", fakeCatalog{err: errors.Timeout.New("slow")}, http.StatusGatewayTimeout},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			resp := serve(t, tc.c, http.MethodPost, "/projects/load")
			if resp.Code != tc.want {
				t.Fatalf("status = %d %s, want %d", resp.Code, resp.Body.String(), tc.want)
			}
			if tc.want == http.StatusOK {
				var body LoadResult
				if err := json.Unmarshal(resp.Body.Bytes(), &body); err != nil || body.Loaded != 3 || body.Updated != 2 {
					t.Errorf("body = %s", resp.Body.String())
				}
			}
		})
	}
}

func TestListProjectsHandler(t *testing.T) {
	resp := serve(t, fakeCatalog{list: &ProjectList{Count: 1, Projects: []ProjectView{{WorkstreamId: "7", Name: "DevLake"}}}}, http.MethodGet, "/workstream-projects")

	if resp.Code != http.StatusOK {
		t.Fatalf("status = %d", resp.Code)
	}
	var body ProjectList
	if err := json.Unmarshal(resp.Body.Bytes(), &body); err != nil || body.Count != 1 || body.Projects[0].WorkstreamId != "7" {
		t.Errorf("body = %s", resp.Body.String())
	}
}

func TestNewHandlersRejectsNil(t *testing.T) {
	if _, err := NewHandlers(nil); err == nil {
		t.Error("NewHandlers(nil) = nil error")
	}
}
