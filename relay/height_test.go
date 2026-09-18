package main

import (
	"encoding/json"
	"math"
	"testing"
)

func TestOptionalLeadHeight(t *testing.T) {
	old := testSample(1)
	if !old.valid() || old.AboveGroundFeet != nil {
		t.Fatal("legacy telemetry must remain usable")
	}
	height := 800.0
	old.AboveGroundFeet = &height
	raw, err := json.Marshal(old)
	if err != nil {
		t.Fatal(err)
	}
	var decoded telemetry
	if err := json.Unmarshal(raw, &decoded); err != nil {
		t.Fatal(err)
	}
	if !decoded.valid() || decoded.AboveGroundFeet == nil || *decoded.AboveGroundFeet != height {
		t.Fatal("relay must preserve optional AGL")
	}
	for _, invalid := range []float64{math.NaN(), math.Inf(1), -2000, 100001} {
		decoded.AboveGroundFeet = &invalid
		if decoded.valid() {
			t.Fatal("invalid AGL accepted")
		}
	}
}
