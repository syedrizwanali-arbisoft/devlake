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
	stderrors "errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"

	"github.com/apache/incubator-devlake/core/errors"
)

const (
	tokenPath    = "auth/token/"
	projectsPath = "fetch/projects"
	// maxResponseBytes caps what DevLake reads from the Integration Service.
	maxResponseBytes = 32 << 20
)

// FetchedProject is one project as the Integration Service returns it.
type FetchedProject struct {
	ID       string `json:"id"`
	Name     string `json:"name"`
	TeamLead *struct {
		Name  string `json:"name"`
		Email string `json:"email"`
	} `json:"team_lead"`
}

// Client fetches Workstream projects through the Integration Service, authenticating with OAuth2 client
// credentials. It is safe for concurrent use.
type Client struct {
	http *http.Client
	cfg  Config
}

// NewClient returns a client that sends requests through httpClient, which should have a timeout.
func NewClient(httpClient *http.Client, cfg Config) (*Client, errors.Error) {
	if httpClient == nil || cfg.BaseURL == nil {
		return nil, errors.Default.New("workstream client: nil http client or base url")
	}
	return &Client{http: httpClient, cfg: cfg}, nil
}

// FetchProjects returns every Workstream project. An unreachable service or one that rejects DevLake's
// credentials returns Unavailable; a timeout returns Timeout.
func (c *Client) FetchProjects(ctx context.Context) ([]FetchedProject, errors.Error) {
	token, err := c.token(ctx)
	if err != nil {
		return nil, err
	}
	req, reqErr := http.NewRequestWithContext(ctx, http.MethodGet, c.cfg.BaseURL.ResolveReference(&url.URL{Path: projectsPath}).String(), nil)
	if reqErr != nil {
		return nil, errors.Default.Wrap(reqErr, "build projects request")
	}
	req.Header.Set("Authorization", "Bearer "+token)
	// The Integration Service answers with the whole list as a JSON array.
	var projects []FetchedProject
	if err := c.do(req, "fetch projects", &projects); err != nil {
		return nil, err
	}
	return projects, nil
}

func (c *Client) token(ctx context.Context) (string, errors.Error) {
	form := url.Values{"grant_type": {"client_credentials"}}.Encode()
	req, reqErr := http.NewRequestWithContext(ctx, http.MethodPost, c.cfg.BaseURL.ResolveReference(&url.URL{Path: tokenPath}).String(), strings.NewReader(form))
	if reqErr != nil {
		return "", errors.Default.Wrap(reqErr, "build token request")
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	req.SetBasicAuth(c.cfg.ClientID, c.cfg.ClientSecret)
	var body struct {
		AccessToken string `json:"access_token"`
	}
	if err := c.do(req, "get an integration service token", &body); err != nil {
		return "", err
	}
	if body.AccessToken == "" {
		return "", errors.Unavailable.New("get an integration service token: the response has no access_token")
	}
	return body.AccessToken, nil
}

// do sends req and decodes a 2xx JSON body into out. The response body is never put in an error, because it is
// another service's content.
func (c *Client) do(req *http.Request, op string, out interface{}) errors.Error {
	resp, err := c.http.Do(req)
	if err != nil {
		if req.Context().Err() != nil || isTimeout(err) {
			return errors.Timeout.Wrap(err, op+": the integration service didn't answer in time")
		}
		return errors.Unavailable.Wrap(err, op+": the integration service is unreachable")
	}
	defer resp.Body.Close()
	limited := io.LimitReader(resp.Body, maxResponseBytes)
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		_, _ = io.Copy(io.Discard, limited) // drain so the connection can be reused; the body isn't needed
		switch resp.StatusCode {
		case http.StatusUnauthorized, http.StatusForbidden:
			return errors.Unavailable.New(fmt.Sprintf("%s: the integration service rejected DevLake's credentials (%d)", op, resp.StatusCode))
		default:
			return errors.Unavailable.New(fmt.Sprintf("%s: the integration service returned %d", op, resp.StatusCode))
		}
	}
	if err := json.NewDecoder(limited).Decode(out); err != nil {
		return errors.Unavailable.Wrap(err, op+": the integration service returned invalid JSON")
	}
	return nil
}

// isTimeout uses the standard library's errors.As: core/errors.As wraps its target in an interface and so
// matches every error without setting a typed target.
func isTimeout(err error) bool {
	var netErr net.Error
	return stderrors.As(err, &netErr) && netErr.Timeout()
}
