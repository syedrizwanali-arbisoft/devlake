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

package migrationscripts

import (
	"time"

	"github.com/apache/incubator-devlake/core/context"
	"github.com/apache/incubator-devlake/core/errors"
	"github.com/apache/incubator-devlake/core/models/migrationscripts/archived"
	"github.com/apache/incubator-devlake/core/plugin"
	"github.com/apache/incubator-devlake/helpers/migrationhelper"
)

var _ plugin.MigrationScript = (*addWorkstreamProjects)(nil)

// addWorkstreamProjects adds the catalog of projects and team leads loaded from Workstream.
type addWorkstreamProjects struct{}

type workstreamProject20261110 struct {
	WorkstreamId  string `gorm:"primaryKey;type:varchar(255)"`
	Name          string `gorm:"type:varchar(255);index"`
	TeamLeadName  string `gorm:"type:varchar(255)"`
	TeamLeadEmail string `gorm:"type:varchar(255)"`
	LastLoadedAt  *time.Time
	archived.NoPKModel
}

func (workstreamProject20261110) TableName() string { return "workstream_projects" }

func (*addWorkstreamProjects) Up(basicRes context.BasicRes) errors.Error {
	return migrationhelper.AutoMigrateTables(basicRes, &workstreamProject20261110{})
}

func (*addWorkstreamProjects) Version() uint64 { return 20261110120000 }

func (*addWorkstreamProjects) Name() string { return "add workstream_projects table" }
