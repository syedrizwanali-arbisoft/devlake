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
	"net/url"
	"strings"
	"time"

	"github.com/apache/incubator-devlake/core/config"
	"github.com/apache/incubator-devlake/core/errors"
)

// Environment variables that configure the Integration Service connection.
const (
	EnvURL            = "INTEGRATION_SERVICE_URL"
	EnvClientID       = "INTEGRATION_SERVICE_CLIENT_ID"
	EnvClientSecret   = "INTEGRATION_SERVICE_CLIENT_SECRET"
	EnvTimeoutSeconds = "INTEGRATION_SERVICE_TIMEOUT_SECONDS"
	// defaultTimeout bounds one load: a token request plus every Workstream page the Integration Service reads.
	defaultTimeout = 60 * time.Second
)

// Config is how DevLake reaches the Integration Service. The secret stays on the server: it is never sent to the
// UI or returned by an API.
type Config struct {
	BaseURL      *url.URL
	ClientID     string
	ClientSecret string
	Timeout      time.Duration
}

// String keeps the secret out of logs and errors.
func (c Config) String() string {
	return "Config{BaseURL: " + c.BaseURL.String() + ", ClientID: " + c.ClientID + "}"
}

// LoadConfig reads the settings. It returns nil, and no error, when none are set: the feature is off. Settings that
// are partly set or invalid return an error naming them.
func LoadConfig(reader config.ConfigReader) (*Config, errors.Error) {
	rawURL := strings.TrimSpace(reader.GetString(EnvURL))
	clientID := strings.TrimSpace(reader.GetString(EnvClientID))
	secret := reader.GetString(EnvClientSecret)
	if rawURL == "" && clientID == "" && secret == "" {
		return nil, nil
	}
	if rawURL == "" || clientID == "" || secret == "" {
		return nil, errors.BadInput.New(EnvURL + ", " + EnvClientID + " and " + EnvClientSecret + " must all be set")
	}
	base, err := url.Parse(rawURL)
	if err != nil || (base.Scheme != "http" && base.Scheme != "https") || base.Host == "" {
		return nil, errors.BadInput.New(EnvURL + " must be an absolute http or https URL")
	}
	if !strings.HasSuffix(base.Path, "/") {
		base.Path += "/"
	}
	timeout := defaultTimeout
	if seconds := reader.GetInt(EnvTimeoutSeconds); seconds > 0 {
		timeout = time.Duration(seconds) * time.Second
	}
	return &Config{BaseURL: base, ClientID: clientID, ClientSecret: secret, Timeout: timeout}, nil
}
