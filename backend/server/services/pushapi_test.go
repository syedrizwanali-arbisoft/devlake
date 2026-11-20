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

package services

import (
	"net/http"
	"testing"
)

func TestIsProtectedTable(t *testing.T) {
	tests := map[string]bool{
		"_devlake_api_keys":      true,
		"_DEVLAKE_API_KEYS":      true,
		"_devlake_pipelines":     true,
		"auth_sessions":          true,
		"auth_local_credentials": true,
		"user_project_mapping":   true,
		"User_Project_Mapping":   true,
		"commits":                false,
		"issues":                 false,
		"_tool_github_repos":     false,
		"authors":                false,
		"user_accounts":          false,
	}
	for table, want := range tests {
		if got := isProtectedTable(table); got != want {
			t.Errorf("isProtectedTable(%q) = %v, want %v", table, got, want)
		}
	}
}

func TestInsertRowRefusesAProtectedTable(t *testing.T) {
	// The check runs before the config or the database is read, so neither is needed here.
	for _, table := range []string{"_devlake_api_keys", "user_project_mapping", "auth_access_users"} {
		_, err := InsertRow(table, []map[string]interface{}{{"id": "x"}})
		if err == nil || err.GetType().GetHttpCode() != http.StatusForbidden {
			t.Errorf("InsertRow(%q) error = %v, want 403", table, err)
		}
	}
}
