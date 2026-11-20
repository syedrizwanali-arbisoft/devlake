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

package repositories

import (
	"regexp"
	"strings"

	"github.com/apache/incubator-devlake/core/dal"
	"github.com/apache/incubator-devlake/core/errors"
)

// rowsReader is the one DAL method the repositories need. dal.Dal satisfies it.
type rowsReader interface {
	All(dst interface{}, clauses ...dal.Clause) errors.Error
}

var _ rowsReader = (dal.Dal)(nil)

// The sources the Integration Service reads today. A source is the prefix DevLake gives the domain ids of one tool.
const (
	SourceJira   = "jira"
	SourceGitHub = "github"
)

// sourcePattern is what a source must look like: a lowercase tool name such as jira, github, gitlab or plane.
var sourcePattern = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)

// idLike validates source and returns the LIKE pattern for its domain ids, for example "github:%". A _ in source is
// escaped, because LIKE reads a bare _ as any one character. The pattern is always bound as a query parameter, never
// written into the SQL.
func idLike(source string) (string, errors.Error) {
	if !sourcePattern.MatchString(source) {
		return "", errors.Default.New("repository source must be a lowercase tool name such as jira or github")
	}
	return strings.ReplaceAll(source, "_", `\_`) + ":%", nil
}

// The repositories match emails case-insensitively and expect them lowercased, because the stored emails are not.
// Every window is half-open, [from, to). The AI ones are ranges of whole days.

// singleRow returns the single row an aggregate query reads, or the zero value when it read none.
func singleRow[T any](rows []T) T {
	var zero T
	if len(rows) == 0 {
		return zero
	}
	return rows[0]
}
