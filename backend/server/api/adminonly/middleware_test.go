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

package adminonly

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/spf13/viper"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models/common"
	contextimpl "github.com/apache/incubator-devlake/impls/context"
	"github.com/apache/incubator-devlake/impls/logruslog"
	"github.com/apache/incubator-devlake/server/api/access"
	"github.com/apache/incubator-devlake/server/api/shared"
)

// TestMain initializes access.Default() once; access.Init is guarded by a sync.Once per binary.
func TestMain(m *testing.M) {
	cfg := viper.New()
	cfg.Set("AUTH_ACCESS_ENABLED", true)
	cfg.Set("AUTH_PUBLIC_URL", "https://devlake.example.com")
	cfg.Set("GRAFANA_PUBLIC_URL", "https://grafana.example.com")
	cfg.Set("GRAFANA_INTERNAL_URL", "http://grafana:3000")
	cfg.Set("GRAFANA_MANAGEMENT_USER", "admin")
	cfg.Set("GRAFANA_MANAGEMENT_PASSWORD", "test-password")
	access.Init(contextimpl.NewDefaultBasicRes(cfg, logruslog.Global, nil))
	os.Exit(m.Run())
}

type fakeChecker struct {
	enabled bool
	err     errors.Error
}

func (f fakeChecker) Enabled() bool { return f.enabled }

func (f fakeChecker) RequireAdmin(*gin.Context) (*access.Principal, errors.Error) {
	return nil, f.err
}

// newRouter marks the request as API-key authenticated when restAuth is set, else installs the principal.
func newRouter(mw gin.HandlerFunc, restAuth bool, principal *access.Principal) *gin.Engine {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	if restAuth {
		r.Use(func(c *gin.Context) {
			c.Request = shared.SetRestAuthUser(c.Request, &common.User{Name: "api-key-creator"})
			c.Next()
		})
	} else if principal != nil {
		r.Use(func(c *gin.Context) {
			access.SetPrincipal(c, principal)
			c.Next()
		})
	}
	r.Use(mw)
	r.POST("/user-project-mappings/:userLogin", func(c *gin.Context) { c.String(http.StatusOK, "ok") })
	return r
}

func call(r *gin.Engine, grafanaUser string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodPost, "/user-project-mappings/alice", nil)
	if grafanaUser != "" {
		req.Header.Set("X-Grafana-User", grafanaUser)
	}
	resp := httptest.NewRecorder()
	r.ServeHTTP(resp, req)
	return resp
}

func TestRequire(t *testing.T) {
	if !access.Default().Enabled() {
		t.Fatal("access.Default().Enabled() = false; want true (TestMain sets AUTH_ACCESS_ENABLED=true)")
	}

	t.Run("api key is denied", func(t *testing.T) {
		if code := call(newRouter(Require(), true, nil), "").Code; code != http.StatusForbidden {
			t.Fatalf("expected 403 for an API-key caller, got %d", code)
		}
	})

	t.Run("api key is denied even with an admin grafana header", func(t *testing.T) {
		if code := call(newRouter(Require(), true, nil), "alice").Code; code != http.StatusForbidden {
			t.Fatalf("expected 403 for an API-key caller sending X-Grafana-User, got %d", code)
		}
	})

	t.Run("session customer admin passes", func(t *testing.T) {
		principal := &access.Principal{UserID: 1, Role: access.RoleCustomerAdmin}
		if code := call(newRouter(Require(), false, principal), "").Code; code != http.StatusOK {
			t.Fatalf("expected a customer admin session to pass, got %d", code)
		}
	})

	t.Run("session member is denied", func(t *testing.T) {
		principal := &access.Principal{UserID: 2, Role: access.RoleMember}
		if code := call(newRouter(Require(), false, principal), "").Code; code != http.StatusForbidden {
			t.Fatalf("expected a member session to be denied, got %d", code)
		}
	})

	t.Run("session without principal is denied", func(t *testing.T) {
		if code := call(newRouter(Require(), false, nil), "").Code; code != http.StatusForbidden {
			t.Fatalf("expected a session without a principal to be denied, got %d", code)
		}
	})

	t.Run("denial body carries the fixed message", func(t *testing.T) {
		resp := call(newRouter(Require(), true, nil), "")
		var body struct {
			Success bool   `json:"success"`
			Message string `json:"message"`
		}
		if err := json.Unmarshal(resp.Body.Bytes(), &body); err != nil {
			t.Fatalf("decode body: %v", err)
		}
		if body.Success || body.Message != "administrator role required" {
			t.Fatalf("unexpected body: %+v", body)
		}
	})
}

func TestRequireWith(t *testing.T) {
	t.Run("access disabled lets a session through", func(t *testing.T) {
		if code := call(newRouter(requireWith(fakeChecker{enabled: false}), false, nil), "").Code; code != http.StatusOK {
			t.Fatalf("expected 200 when access is disabled, got %d", code)
		}
	})

	t.Run("access disabled still denies an api key", func(t *testing.T) {
		if code := call(newRouter(requireWith(fakeChecker{enabled: false}), true, nil), "alice").Code; code != http.StatusForbidden {
			t.Fatalf("expected 403 for an API key when access is disabled, got %d", code)
		}
	})

	t.Run("enabled with an admin passes", func(t *testing.T) {
		if code := call(newRouter(requireWith(fakeChecker{enabled: true}), false, nil), "").Code; code != http.StatusOK {
			t.Fatalf("expected 200 for an admin, got %d", code)
		}
	})

	t.Run("enabled with a rejection is denied", func(t *testing.T) {
		mw := requireWith(fakeChecker{enabled: true, err: errors.Forbidden.New("nope")})
		if code := call(newRouter(mw, false, nil), "").Code; code != http.StatusForbidden {
			t.Fatalf("expected 403 on a RequireAdmin error, got %d", code)
		}
	})
}

func TestRequireAdminOrAPIKey(t *testing.T) {
	tests := []struct {
		name      string
		checker   fakeChecker
		restAuth  bool
		principal *access.Principal
		want      int
	}{
		{name: "api key passes", checker: fakeChecker{enabled: true, err: errors.Forbidden.New("no session")}, restAuth: true, want: http.StatusOK},
		{name: "api key passes when access is disabled", checker: fakeChecker{enabled: false}, restAuth: true, want: http.StatusOK},
		{name: "session admin passes", checker: fakeChecker{enabled: true}, want: http.StatusOK},
		{name: "session non-admin is denied", checker: fakeChecker{enabled: true, err: errors.Forbidden.New("member")}, want: http.StatusForbidden},
		{name: "session passes when access is disabled", checker: fakeChecker{enabled: false}, want: http.StatusOK},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			resp := call(newRouter(requireAdminOrAPIKeyWith(tc.checker), tc.restAuth, tc.principal), "")
			if resp.Code != tc.want {
				t.Errorf("status = %d, want %d", resp.Code, tc.want)
			}
		})
	}

	t.Run("the exported middleware denies a member session", func(t *testing.T) {
		principal := &access.Principal{UserID: 2, Role: access.RoleMember}
		if code := call(newRouter(RequireAdminOrAPIKey(), false, principal), "").Code; code != http.StatusForbidden {
			t.Fatalf("expected a member session to be denied, got %d", code)
		}
	})

	t.Run("the exported middleware admits an api key", func(t *testing.T) {
		if code := call(newRouter(RequireAdminOrAPIKey(), true, nil), "").Code; code != http.StatusOK {
			t.Fatalf("expected an API-key caller to pass, got %d", code)
		}
	})
}
