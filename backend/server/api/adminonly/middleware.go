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
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/server/api/access"
	"github.com/apache/incubator-devlake/server/api/shared"
)

type accessChecker interface {
	Enabled() bool
	RequireAdmin(c *gin.Context) (*access.Principal, errors.Error)
}

// Require admits only a DevLake customer admin's session; REST API-key callers are always denied.
func Require() gin.HandlerFunc {
	return requireWith(access.Default())
}

// RequireAdminOrAPIKey admits a DevLake customer admin's session, like Require, and also any REST API-key caller.
// RestAuthentication has already checked the key and that its allowed path covers the route, so the key's allowed
// path is the only limit on what the key can do here. Use it only on routes an automated client must reach.
func RequireAdminOrAPIKey() gin.HandlerFunc {
	return requireAdminOrAPIKeyWith(access.Default())
}

func requireAdminOrAPIKeyWith(accessService accessChecker) gin.HandlerFunc {
	adminOnly := requireWith(accessService)
	return func(c *gin.Context) {
		if _, viaApiKey := shared.GetRestAuthUser(c.Request); viaApiKey {
			c.Next()
			return
		}
		adminOnly(c)
	}
}

func requireWith(accessService accessChecker) gin.HandlerFunc {
	return func(c *gin.Context) {
		if _, viaApiKey := shared.GetRestAuthUser(c.Request); viaApiKey {
			deny(c)
			return
		}
		if !accessService.Enabled() {
			c.Next()
			return
		}
		if _, err := accessService.RequireAdmin(c); err != nil {
			deny(c)
			return
		}
		c.Next()
	}
}

// deny answers every failure with one message so callers learn nothing about why.
func deny(c *gin.Context) {
	c.AbortWithStatusJSON(http.StatusForbidden, gin.H{
		"success": false,
		"message": "administrator role required",
	})
}
