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

package integration

import (
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/apache/incubator-devlake/server/api/shared"
)

// RequireAPIKey admits only requests that RestAuthentication authenticated with a DevLake API key, that is, calls
// to /rest/... with "Authorization: Bearer <API key>". It keeps these routes closed to direct calls even when session
// authentication is turned off.
func RequireAPIKey() gin.HandlerFunc {
	return func(c *gin.Context) {
		if _, viaAPIKey := shared.GetRestAuthUser(c.Request); !viaAPIKey {
			c.AbortWithStatusJSON(http.StatusUnauthorized, &shared.ApiBody{
				Success: false,
				Message: "a DevLake API key is required: call /rest" + c.Request.URL.Path + " with Authorization: Bearer <API key>",
			})
			return
		}
		c.Next()
	}
}
