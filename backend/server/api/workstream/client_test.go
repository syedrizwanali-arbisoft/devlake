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
	"encoding/base64"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"
)

func newTestClient(t *testing.T, handler http.HandlerFunc, timeout time.Duration) *Client {
	t.Helper()
	server := httptest.NewServer(handler)
	t.Cleanup(server.Close)
	base, _ := url.Parse(server.URL + "/svc/")
	c, err := NewClient(&http.Client{Timeout: timeout}, Config{BaseURL: base, ClientID: "devlake", ClientSecret: "s3cret"})
	if err != nil {
		t.Fatalf("NewClient() error = %v", err)
	}
	return c
}

func TestFetchProjectsGetsATokenThenTheProjects(t *testing.T) {
	var calls []string
	client := newTestClient(t, func(w http.ResponseWriter, r *http.Request) {
		calls = append(calls, r.Method+" "+r.URL.Path)
		switch r.URL.Path {
		case "/svc/auth/token/":
			wantAuth := "Basic " + base64.StdEncoding.EncodeToString([]byte("devlake:s3cret"))
			if r.Header.Get("Authorization") != wantAuth || r.FormValue("grant_type") != "client_credentials" {
				http.Error(w, "bad credentials", http.StatusUnauthorized)
				return
			}
			_, _ = w.Write([]byte(`{"access_token":"tok","token_type":"Bearer","expires_in":3600}`))
		case "/svc/fetch/projects":
			if r.Header.Get("Authorization") != "Bearer tok" {
				http.Error(w, "no", http.StatusUnauthorized)
				return
			}
			_, _ = w.Write([]byte(`[{"id":"7","name":"DevLake","team_lead":{"name":"Lead","email":"lead@arbisoft.com"}},{"id":"8","name":"Sophia","team_lead":null}]`))
		default:
			http.NotFound(w, r)
		}
	}, 5*time.Second)

	projects, err := client.FetchProjects(context.Background())

	if err != nil {
		t.Fatalf("FetchProjects() error = %v", err)
	}
	if len(projects) != 2 || projects[0].TeamLead.Email != "lead@arbisoft.com" || projects[1].TeamLead != nil {
		t.Errorf("FetchProjects() = %+v", projects)
	}
	if strings.Join(calls, ",") != "POST /svc/auth/token/,GET /svc/fetch/projects" {
		t.Errorf("calls = %v", calls)
	}
}

func TestFetchProjectsErrors(t *testing.T) {
	tests := []struct {
		name       string
		handler    http.HandlerFunc
		timeout    time.Duration
		wantStatus int
		wantText   string
	}{
		{"credentials rejected", func(w http.ResponseWriter, r *http.Request) {
			http.Error(w, `{"error":"invalid_client"}`, http.StatusUnauthorized)
		}, time.Second, http.StatusServiceUnavailable, "rejected DevLake's credentials"},
		{"workstream failure", func(w http.ResponseWriter, r *http.Request) {
			if r.URL.Path == "/svc/auth/token/" {
				_, _ = w.Write([]byte(`{"access_token":"tok"}`))
				return
			}
			http.Error(w, `{"error":"Workstream: /projects returned 503."}`, http.StatusBadGateway)
		}, time.Second, http.StatusServiceUnavailable, "returned 502"},
		{"no access token", func(w http.ResponseWriter, r *http.Request) {
			_, _ = w.Write([]byte(`{}`))
		}, time.Second, http.StatusServiceUnavailable, "no access_token"},
		{"invalid json", func(w http.ResponseWriter, r *http.Request) {
			_, _ = w.Write([]byte(`<html>`))
		}, time.Second, http.StatusServiceUnavailable, "invalid JSON"},
		{"timeout", func(w http.ResponseWriter, r *http.Request) {
			time.Sleep(300 * time.Millisecond)
		}, 50 * time.Millisecond, http.StatusGatewayTimeout, "didn't answer in time"},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			_, err := newTestClient(t, tc.handler, tc.timeout).FetchProjects(context.Background())
			if err == nil {
				t.Fatal("FetchProjects() = nil error")
			}
			if err.GetType().GetHttpCode() != tc.wantStatus || !strings.Contains(err.Error(), tc.wantText) {
				t.Errorf("error = %d %v, want %d containing %q", err.GetType().GetHttpCode(), err, tc.wantStatus, tc.wantText)
			}
			if strings.Contains(err.Error(), "invalid_client") || strings.Contains(err.Error(), "s3cret") {
				t.Errorf("error leaks the response or secret: %v", err)
			}
		})
	}
}

func TestFetchProjectsUnreachable(t *testing.T) {
	base, _ := url.Parse("http://127.0.0.1:1/")
	c, _ := NewClient(&http.Client{Timeout: time.Second}, Config{BaseURL: base, ClientID: "a", ClientSecret: "b"})

	_, err := c.FetchProjects(context.Background())

	if err == nil || err.GetType().GetHttpCode() != http.StatusServiceUnavailable {
		t.Errorf("error = %v, want 503 unreachable", err)
	}
}

func TestNewClientRejectsNil(t *testing.T) {
	if _, err := NewClient(nil, Config{}); err == nil {
		t.Error("NewClient(nil) = nil error")
	}
}
