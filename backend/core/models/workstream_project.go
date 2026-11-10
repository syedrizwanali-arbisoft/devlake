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

package models

import (
	"time"

	"github.com/apache/incubator-devlake/core/models/common"
)

// WorkstreamProject is a project loaded from Workstream through the Integration Service, with its team lead.
// It is keyed by Workstream's ID, so loading again updates the row instead of adding one. A project links to the
// DevLake project with the same name, if there is one, when it is read.
type WorkstreamProject struct {
	WorkstreamId  string     `json:"workstreamId" gorm:"primaryKey;type:varchar(255)"`
	Name          string     `json:"name" gorm:"type:varchar(255);index"`
	TeamLeadName  string     `json:"teamLeadName" gorm:"type:varchar(255)"`
	TeamLeadEmail string     `json:"teamLeadEmail" gorm:"type:varchar(255)"`
	LastLoadedAt  *time.Time `json:"lastLoadedAt"`
	common.NoPKModel
}

// TableName returns the workstream_projects table.
func (WorkstreamProject) TableName() string {
	return "workstream_projects"
}
