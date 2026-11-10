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
	"regexp"
	"strconv"
	"time"

	"github.com/apache/incubator-devlake/core/errors"
)

// timeFramePattern is the grammar agreed with the Integration Service: 1 to 999 followed by a case-sensitive unit.
var timeFramePattern = regexp.MustCompile(`^([1-9][0-9]{0,2})([hdwMy])$`)

// TimeFrame is a look-back window such as 24h, 1w, 30d, 11M or 1y. The units are h (hours), d (days), w (weeks),
// M (months) and y (years).
type TimeFrame struct {
	amount int
	unit   byte
	raw    string
}

// ParseTimeFrame parses value, returning a BadInput error that names the tf parameter when it is invalid.
func ParseTimeFrame(value string) (TimeFrame, errors.Error) {
	match := timeFramePattern.FindStringSubmatch(value)
	if match == nil {
		return TimeFrame{}, errors.BadInput.New("tf must be a number from 1 to 999 followed by h, d, w, M or y, for example 24h")
	}
	amount, err := strconv.Atoi(match[1])
	if err != nil {
		return TimeFrame{}, errors.BadInput.Wrap(err, "tf amount")
	}
	return TimeFrame{amount: amount, unit: match[2][0], raw: value}, nil
}

// String returns the time frame as it was given, for example "24h".
func (tf TimeFrame) String() string { return tf.raw }

// Window returns the half-open interval [from, to) that ends at now, in UTC.
func (tf TimeFrame) Window(now time.Time) (from, to time.Time) {
	to = now.UTC()
	switch tf.unit {
	case 'h':
		from = to.Add(-time.Duration(tf.amount) * time.Hour)
	case 'd':
		from = to.AddDate(0, 0, -tf.amount)
	case 'w':
		from = to.AddDate(0, 0, -7*tf.amount)
	case 'M':
		from = to.AddDate(0, -tf.amount, 0)
	default: // 'y': ParseTimeFrame admits no other unit
		from = to.AddDate(-tf.amount, 0, 0)
	}
	return from, to
}
