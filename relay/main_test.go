package main

import (
	"bytes"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"github.com/gorilla/websocket"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func connectTest(t *testing.T, url string, id int, room string) (*websocket.Conn, string) {
	t.Helper()
	c, _, err := websocket.DefaultDialer.Dial("ws"+strings.TrimPrefix(url, "http")+"/v1/session", nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	err = c.WriteJSON(message{Type: "join", Protocol: 1, Create: room == "", Room: room, ID: fmt.Sprintf("%032x", id), Token: strings.Repeat("k", 32), Name: fmt.Sprintf("Pilot %d", id), Share: true, Echo: time.Now().UnixMilli()})
	if err != nil {
		t.Fatal(err)
	}
	m := readType(t, c, "welcome")
	return c, m["room"].(string)
}
func readType(t *testing.T, c *websocket.Conn, kind string) map[string]any {
	t.Helper()
	c.SetReadDeadline(time.Now().Add(5 * time.Second))
	for i := 0; i < 1000; i++ {
		var m map[string]any
		if e := c.ReadJSON(&m); e != nil {
			t.Fatal(e)
		}
		if m["type"] == kind {
			return m
		}
	}
	t.Fatal("Message not received: " + kind)
	return nil
}
func testSample(seq int64) *telemetry {
	return &telemetry{Sequence: seq, SampleTimeMs: time.Now().UnixMilli(), Latitude: 52, Longitude: 5, AltitudeFeet: 10000, GroundSpeedKnots: 250, GroundTrackDegrees: 90, HeadingDegrees: 95, Model: "787", SimRate: 1}
}

func TestFormationAndIsolation(t *testing.T) {
	h := newHub(t.TempDir())
	s := httptest.NewServer(h.routes())
	defer s.Close()
	a, room := connectTest(t, s.URL, 1, "")
	b, _ := connectTest(t, s.URL, 2, room)
	c, _ := connectTest(t, s.URL, 3, room)
	other, _ := connectTest(t, s.URL, 4, "")
	b.WriteJSON(message{Type: "configure", Share: true, Lead: fmt.Sprintf("%032x", 1)})
	readType(t, b, "configured")
	c.WriteJSON(message{Type: "configure", Share: true, Lead: fmt.Sprintf("%032x", 2)})
	readType(t, c, "configured")
	a.WriteJSON(message{Type: "configure", Share: true, Lead: fmt.Sprintf("%032x", 3)})
	if !strings.Contains(readType(t, a, "error")["message"].(string), "loop") {
		t.Fatal("Follow cycle accepted")
	}
	sample := testSample(1)
	a.WriteJSON(message{Type: "telemetry", Telemetry: sample})
	if readType(t, c, "telemetry")["id"] != fmt.Sprintf("%032x", 1) {
		t.Fatal("Wrong pilot identity")
	}
	other.WriteJSON(message{Type: "ping", Echo: 1})
	for {
		var m map[string]any
		other.SetReadDeadline(time.Now().Add(time.Second))
		if e := other.ReadJSON(&m); e != nil {
			t.Fatal(e)
		}
		if m["type"] == "telemetry" {
			t.Fatal("Room leaked")
		}
		if m["type"] == "pong" {
			break
		}
	}
	a.WriteJSON(message{Type: "telemetry", Telemetry: sample})
	if !strings.Contains(readType(t, a, "error")["message"].(string), "sequence") {
		t.Fatal("Duplicate accepted")
	}
	sample = testSample(2)
	sample.SampleTimeMs -= 3000
	a.WriteJSON(message{Type: "telemetry", Telemetry: sample})
	readType(t, a, "error")
	a.WriteJSON(message{Type: "unavailable"})
	if readType(t, b, "unavailable")["id"] != fmt.Sprintf("%032x", 1) {
		t.Fatal("No unavailable signal")
	}
	a.Close()
	for {
		m := readType(t, b, "roster")
		offline := false
		for _, v := range m["members"].([]any) {
			p := v.(map[string]any)
			if p["id"] == fmt.Sprintf("%032x", 1) && p["online"] == false {
				offline = true
			}
		}
		if offline {
			break
		}
	}
	b.WriteJSON(message{Type: "configure", Share: true, Lead: fmt.Sprintf("%032x", 1)})
	readType(t, b, "error")
	a2, _ := connectTest(t, s.URL, 1, room)
	a2.WriteJSON(message{Type: "telemetry", Telemetry: testSample(1)})
	readType(t, b, "telemetry")
}
func TestIdentityAuthentication(t *testing.T) {
	h := newHub(t.TempDir())
	s := httptest.NewServer(h.routes())
	defer s.Close()
	a, room := connectTest(t, s.URL, 1, "")
	c, _, e := websocket.DefaultDialer.Dial("ws"+strings.TrimPrefix(s.URL, "http")+"/v1/session", nil)
	if e != nil {
		t.Fatal(e)
	}
	defer c.Close()
	c.WriteJSON(message{Type: "join", Protocol: 1, Room: room, ID: fmt.Sprintf("%032x", 1), Token: strings.Repeat("x", 32), Name: "Impostor", Echo: time.Now().UnixMilli()})
	c.SetReadDeadline(time.Now().Add(time.Second))
	var v any
	if c.ReadJSON(&v) == nil {
		t.Fatal("Wrong secret accepted")
	}
	a.WriteJSON(message{Type: "ping", Echo: 77})
	if readType(t, a, "pong")["echo"].(float64) != 77 {
		t.Fatal("Real pilot disconnected")
	}
}
func connectPublic(t *testing.T, url string, id int, share bool) *websocket.Conn {
	t.Helper()
	c, _, err := websocket.DefaultDialer.Dial("ws"+strings.TrimPrefix(url, "http")+"/v1/session", nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	c.WriteJSON(message{Type: "join", Protocol: 1, PublicRoom: true, ID: fmt.Sprintf("%032x", id), Token: strings.Repeat("k", 32), Name: fmt.Sprintf("Pilot %d", id), Share: share, Echo: time.Now().UnixMilli()})
	if readType(t, c, "welcome")["room"] != "PUBLIC" {
		t.Fatal("Public connection required a code")
	}
	return c
}
func TestPublicNearbyDiscovery(t *testing.T) {
	h := newHub(t.TempDir())
	s := httptest.NewServer(h.routes())
	defer s.Close()
	a := connectPublic(t, s.URL, 1, true)
	b := connectPublic(t, s.URL, 2, true)
	far := connectPublic(t, s.URL, 3, true)
	observer := connectPublic(t, s.URL, 4, false)
	b.WriteJSON(message{Type: "telemetry", Telemetry: testSample(1)})
	distant := testSample(1)
	distant.Latitude = -30
	far.WriteJSON(message{Type: "telemetry", Telemetry: distant})
	observer.WriteJSON(message{Type: "observer", Telemetry: testSample(1)})
	observer.WriteJSON(message{Type: "ping", Echo: 1})
	readType(t, observer, "pong")
	a.WriteJSON(message{Type: "telemetry", Telemetry: testSample(1)})
	readType(t, b, "telemetry")
	readType(t, observer, "telemetry")
	far.WriteJSON(message{Type: "ping", Echo: 9})
	for {
		var m map[string]any
		far.SetReadDeadline(time.Now().Add(time.Second))
		if err := far.ReadJSON(&m); err != nil {
			t.Fatal(err)
		}
		if m["type"] == "telemetry" {
			t.Fatal("Distant pilot received nearby telemetry")
		}
		if m["type"] == "pong" {
			break
		}
	}
	b.WriteJSON(message{Type: "configure", Share: true, Lead: fmt.Sprintf("%032x", 1)})
	readType(t, b, "configured")
	start := time.Now()
	for i := int64(2); i <= 11; i++ {
		time.Sleep(100 * time.Millisecond)
		a.WriteJSON(message{Type: "telemetry", Telemetry: testSample(i)})
		readType(t, b, "telemetry")
	}
	if time.Since(start) > 2*time.Second {
		t.Fatal("Following was throttled to discovery frequency")
	}
	// Observer updates locate the receiver, but never make it a followable aircraft.
	b.WriteJSON(message{Type: "ping", Echo: 10})
	for {
		var m map[string]any
		b.SetReadDeadline(time.Now().Add(time.Second))
		if err := b.ReadJSON(&m); err != nil {
			t.Fatal(err)
		}
		if m["type"] == "telemetry" && m["id"] == fmt.Sprintf("%032x", 4) {
			t.Fatal("Non-followable observer exposed")
		}
		if m["type"] == "pong" {
			break
		}
	}
}
func TestClockSkewAndStaleSamples(t *testing.T) {
	h := newHub("")
	now := time.Now()
	r := &room{members: map[string]*member{}, touched: now}
	h.rooms["00000000000000000000"] = r
	p := &peer{id: "id", room: "00000000000000000000", send: make(chan any, 64), done: make(chan struct{}), clockOffset: -3600000}
	r.members["id"] = &member{ID: "id", Share: true, Online: true, peer: p}
	sample := testSample(1)
	sample.SampleTimeMs += 3600000
	if e := h.sample(p, sample); e != nil {
		t.Fatal(e)
	}
	// A new sequence does not make a delayed sample fresh.
	p.sequence = 0
	p.sample = 0
	sample = testSample(2)
	sample.SampleTimeMs += 3600000 - 4000
	if h.sample(p, sample) == nil {
		t.Fatal("Stale sample accepted")
	}
	sample = testSample(3)
	sample.GroundSpeedKnots = -1
	if h.sample(p, sample) == nil {
		t.Fatal("Invalid units accepted")
	}
}
func TestSignedPublishAndDownloadDuringTelemetry(t *testing.T) {
	dir := t.TempDir()
	source := filepath.Join(dir, "source")
	os.Mkdir(source, 0755)
	data := make([]byte, 2*1024*1024)
	rand.Read(data)
	os.WriteFile(filepath.Join(source, "WingMan.exe"), data, 0644)
	key, _ := rsa.GenerateKey(rand.Reader, 2048)
	der, _ := x509.MarshalPKCS8PrivateKey(key)
	keyFile := filepath.Join(dir, "key.pem")
	os.WriteFile(keyFile, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: der}), 0600)
	releases := filepath.Join(dir, "releases")
	if e := publish([]string{"-version", "1.2.0", "-source", source, "-out", releases, "-key", keyFile}); e != nil {
		t.Fatal(e)
	}
	manifestBytes, _ := os.ReadFile(filepath.Join(releases, "latest.json"))
	manifest, e := decodeRelease(manifestBytes, &key.PublicKey)
	if e != nil {
		t.Fatal(e)
	}
	var env envelope
	json.Unmarshal(manifestBytes, &env)
	env.Payload = env.Payload[:len(env.Payload)-4] + "AAAA"
	tampered, _ := json.Marshal(env)
	if _, e := decodeRelease(tampered, &key.PublicKey); e == nil {
		t.Fatal("Tampering accepted")
	}
	h := newHub(releases)
	s := httptest.NewServer(h.routes())
	defer s.Close()
	a, room := connectTest(t, s.URL, 1, "")
	b, _ := connectTest(t, s.URL, 2, room)
	response, e := http.Get(s.URL + "/v1/update?version=1.1.0")
	if e != nil || response.StatusCode != 200 {
		t.Fatal("Update missing", e)
	}
	response.Body.Close()
	response, _ = http.Get(s.URL + "/v1/update?version=1.2.0")
	if response.StatusCode != 204 {
		t.Fatal("Already-current update offered")
	}
	response.Body.Close()
	download := make(chan error, 1)
	go func() {
		r, e := http.Get(s.URL + "/releases/" + manifest.File)
		if e != nil {
			download <- e
			return
		}
		defer r.Body.Close()
		_, e = io.Copy(io.Discard, r.Body)
		download <- e
	}()
	var worst time.Duration
	for i := int64(1); i <= 20; i++ {
		start := time.Now()
		a.WriteJSON(message{Type: "telemetry", Telemetry: testSample(i)})
		readType(t, b, "telemetry")
		if d := time.Since(start); d > worst {
			worst = d
		}
		time.Sleep(100 * time.Millisecond)
	}
	if e := <-download; e != nil {
		t.Fatal(e)
	}
	t.Logf("20 telemetry samples at 10 Hz while downloading: worst loopback delivery %s", worst)
	a.WriteJSON(message{Type: "ping", Echo: 1})
	readType(t, a, "pong")
	// Verify the downloaded archive is really the published archive.
	response, _ = http.Get(s.URL + "/releases/" + manifest.File)
	downloaded, _ := io.ReadAll(response.Body)
	response.Body.Close()
	disk, _ := os.ReadFile(filepath.Join(releases, manifest.File))
	if !bytes.Equal(downloaded, disk) {
		t.Fatal("Archive corrupted")
	}
}

func TestPublicDiscoveryAfterIdle(t *testing.T) {
    h := newHub("")
    h.rooms["PUBLIC"] = &room{members: map[string]*member{}, touched: time.Now().Add(-2*time.Hour)}
    s := httptest.NewServer(h.routes())
    defer s.Close()
    c := connectPublic(t, s.URL, 1, true)
    c.WriteJSON(message{Type: "ping", Echo: 42})
    readType(t, c, "pong")
}
