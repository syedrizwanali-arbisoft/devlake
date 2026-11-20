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
	"regexp"
	"strings"

	"github.com/apache/incubator-devlake/core/dal"
	"github.com/apache/incubator-devlake/core/errors"
)

// protectedTablePrefixes and protectedTables are the tables that control access: DevLake's own tables (API keys,
// pipelines, blueprints), sign-in and access tables, and the user to project mapping that scopes the dashboards. The
// push API never writes them, even when PUSH_API_ALLOWED_TABLES lists them, because any API key whose allowed path
// covers /push could otherwise give itself more access.
var (
	protectedTablePrefixes = []string{"_devlake_", "auth_"}
	protectedTables        = map[string]bool{"user_project_mapping": true}
)

// isProtectedTable reports whether table is one the push API must never write. Table names are compared in lowercase,
// because MySQL may treat them case-insensitively.
func isProtectedTable(table string) bool {
	table = strings.ToLower(table)
	if protectedTables[table] {
		return true
	}
	for _, prefix := range protectedTablePrefixes {
		if strings.HasPrefix(table, prefix) {
			return true
		}
	}
	return false
}

// InsertRow inserts rows into table for the push API. table must be a plain table name, not a protected table, and in
// PUSH_API_ALLOWED_TABLES when that is set.
func InsertRow(table string, rows []map[string]interface{}) (int64, errors.Error) {
	if !regexp.MustCompile(`^[a-zA-Z0-9_]+$`).MatchString(table) {
		return 0, errors.BadInput.New("table name invalid")
	}
	if isProtectedTable(table) {
		return 0, errors.Forbidden.New("the push API can't write this table")
	}

	if allowedTables := cfg.GetString("PUSH_API_ALLOWED_TABLES"); allowedTables != "" {
		allow := false
		for _, t := range strings.Split(allowedTables, ",") {
			if strings.TrimSpace(t) == table {
				allow = true
				break
			}
		}
		if !allow {
			return 0, errors.Forbidden.New("table name is not in the allowed list")
		}
	}

	err := db.Create(rows, dal.From(table))
	if err != nil {
		return 0, err
	}
	return 1, nil
}
