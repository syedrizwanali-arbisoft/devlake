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
	"testing"
	"time"
)

func TestParseTimeFrame(t *testing.T) {
	now := time.Date(2026, 11, 10, 12, 0, 0, 0, time.UTC)
	tests := []struct {
		name string
		in   string
		from time.Time
	}{
		{"hours", "24h", time.Date(2026, 11, 9, 12, 0, 0, 0, time.UTC)},
		{"days", "30d", time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)},
		{"weeks", "1w", time.Date(2026, 11, 3, 12, 0, 0, 0, time.UTC)},
		{"months", "11M", time.Date(2025, 12, 10, 12, 0, 0, 0, time.UTC)},
		{"years", "1y", time.Date(2025, 11, 10, 12, 0, 0, 0, time.UTC)},
		{"largest", "999d", now.AddDate(0, 0, -999)},
	}
	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			tf, err := ParseTimeFrame(tc.in)
			if err != nil {
				t.Fatalf("ParseTimeFrame(%q) error = %v", tc.in, err)
			}
			from, to := tf.Window(now)
			if !from.Equal(tc.from) || !to.Equal(now) {
				t.Errorf("Window = [%v, %v), want [%v, %v)", from, to, tc.from, now)
			}
			if tf.String() != tc.in {
				t.Errorf("String() = %q, want %q", tf.String(), tc.in)
			}
		})
	}
}

func TestWindowIsUTC(t *testing.T) {
	tf, _ := ParseTimeFrame("1d")
	local := time.Date(2026, 11, 10, 17, 0, 0, 0, time.FixedZone("PKT", 5*3600))
	from, to := tf.Window(local)
	if from.Location() != time.UTC || to.Location() != time.UTC || to.Hour() != 12 {
		t.Errorf("Window(%v) = [%v, %v), want UTC ending 12:00", local, from, to)
	}
}

func TestParseTimeFrameRejects(t *testing.T) {
	for _, in := range []string{"", "24", "h", "24H", "1m", "1D", "0d", "01d", "1000d", "-1d", " 1d", "1d ", "1.5d", "1dd"} {
		t.Run(in, func(t *testing.T) {
			_, err := ParseTimeFrame(in)
			if err == nil {
				t.Fatalf("ParseTimeFrame(%q) = nil error, want BadInput", in)
			}
			if got := err.GetType().GetHttpCode(); got != http.StatusBadRequest {
				t.Errorf("status = %d, want 400", got)
			}
		})
	}
}
