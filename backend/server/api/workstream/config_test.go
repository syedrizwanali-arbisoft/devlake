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
	"strings"
	"testing"
	"time"

	"github.com/spf13/viper"
)

func reader(values map[string]interface{}) *viper.Viper {
	v := viper.New()
	for k, val := range values {
		v.Set(k, val)
	}
	return v
}

func TestLoadConfig(t *testing.T) {
	full := map[string]interface{}{EnvURL: "http://integration:5099", EnvClientID: "devlake", EnvClientSecret: "s3cret"}
	tests := []struct {
		name        string
		values      map[string]interface{}
		wantNil     bool
		wantErr     string
		wantBase    string
		wantTimeout time.Duration
	}{
		{name: "nothing set turns the feature off", values: map[string]interface{}{}, wantNil: true},
		{name: "all set", values: full, wantBase: "http://integration:5099/", wantTimeout: defaultTimeout},
		{name: "custom timeout", values: map[string]interface{}{EnvURL: "https://x.example/svc", EnvClientID: "a", EnvClientSecret: "b", EnvTimeoutSeconds: 5}, wantBase: "https://x.example/svc/", wantTimeout: 5 * time.Second},
		{name: "partly set", values: map[string]interface{}{EnvURL: "http://integration:5099"}, wantErr: "must all be set"},
		{name: "relative url", values: map[string]interface{}{EnvURL: "integration:5099", EnvClientID: "a", EnvClientSecret: "b"}, wantErr: "absolute http or https"},
		{name: "ftp url", values: map[string]interface{}{EnvURL: "ftp://integration", EnvClientID: "a", EnvClientSecret: "b"}, wantErr: "absolute http or https"},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			cfg, err := LoadConfig(reader(tc.values))
			if tc.wantErr != "" {
				if err == nil || !strings.Contains(err.Error(), tc.wantErr) {
					t.Fatalf("LoadConfig() error = %v, want %q", err, tc.wantErr)
				}
				return
			}
			if err != nil {
				t.Fatalf("LoadConfig() error = %v", err)
			}
			if tc.wantNil {
				if cfg != nil {
					t.Fatalf("LoadConfig() = %v, want nil", cfg)
				}
				return
			}
			if cfg.BaseURL.String() != tc.wantBase || cfg.Timeout != tc.wantTimeout {
				t.Errorf("LoadConfig() = %s %v, want %s %v", cfg.BaseURL, cfg.Timeout, tc.wantBase, tc.wantTimeout)
			}
		})
	}
}

func TestConfigStringHidesTheSecret(t *testing.T) {
	cfg, _ := LoadConfig(reader(map[string]interface{}{EnvURL: "http://i", EnvClientID: "devlake", EnvClientSecret: "s3cret"}))
	if strings.Contains(cfg.String(), "s3cret") {
		t.Errorf("String() = %q, leaks the secret", cfg.String())
	}
}
