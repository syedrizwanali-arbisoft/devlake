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

// maxRange is the longest explicit range, the same limit the Integration Service applies.
const maxRange = 366 * 24 * time.Hour

// Period is what the caller asked for: a time frame counted back from now, or an explicit range. Exactly one of the two
// must be given.
type Period struct {
	TimeFrame string
	From      string
	To        string
}

// resolve returns the half-open window [from, to) in UTC and the time frame text (empty for a range). Errors are BadInput.
func (p Period) resolve(now time.Time) (from, to time.Time, timeFrame string, err errors.Error) {
	hasRange := p.From != "" || p.To != ""
	switch {
	case p.TimeFrame != "" && hasRange:
		return time.Time{}, time.Time{}, "", errors.BadInput.New("give either tf or from and to, not both")
	case p.TimeFrame != "":
		tf, err := ParseTimeFrame(p.TimeFrame)
		if err != nil {
			return time.Time{}, time.Time{}, "", err
		}
		from, to = tf.Window(now)
		return from, to, tf.String(), nil
	case hasRange:
		from, to, err = parseRange(p.From, p.To)
		return from, to, "", err
	default:
		return time.Time{}, time.Time{}, "", errors.BadInput.New("tf is required, or give both from and to")
	}
}

func parseRange(rawFrom, rawTo string) (from, to time.Time, err errors.Error) {
	if rawFrom == "" || rawTo == "" {
		return time.Time{}, time.Time{}, errors.BadInput.New("from and to must be given together")
	}
	// RFC 3339 requires the offset, so a time with no zone is rejected instead of read in server time.
	parsedFrom, parseErr := time.Parse(time.RFC3339, rawFrom)
	if parseErr != nil {
		return time.Time{}, time.Time{}, errors.BadInput.New("from must be an RFC 3339 time with an offset, for example 2026-11-01T00:00:00Z")
	}
	parsedTo, parseErr := time.Parse(time.RFC3339, rawTo)
	if parseErr != nil {
		return time.Time{}, time.Time{}, errors.BadInput.New("to must be an RFC 3339 time with an offset, for example 2026-11-08T00:00:00Z")
	}
	from, to = parsedFrom.UTC().Truncate(time.Second), parsedTo.UTC().Truncate(time.Second)
	if !from.Before(to) {
		return time.Time{}, time.Time{}, errors.BadInput.New("from must be earlier than to")
	}
	if to.Sub(from) > maxRange {
		return time.Time{}, time.Time{}, errors.BadInput.New("the range can be at most one year")
	}
	return from, to, nil
}
