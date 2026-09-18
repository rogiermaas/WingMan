// Escort relay: ephemeral private rooms, no aircraft control, no telemetry stored on disk.
package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"errors"
	"flag"
	"fmt"
	"github.com/gorilla/websocket"
	"log"
	"math"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"sync"
	"syscall"
	"time"
)

const protocol = 1

var identifier = regexp.MustCompile(`^[a-f0-9]{32}$`)
var codePattern = regexp.MustCompile(`^[A-F0-9]{20}$`)

type telemetry struct {
	Sequence           int64   `json:"sequence"`
	SampleTimeMs       int64   `json:"sampleTimeMs"`
	SampleAgeMs        float64 `json:"sampleAgeMs"`
	Latitude           float64 `json:"latitude"`
	Longitude          float64 `json:"longitude"`
	AltitudeFeet       float64 `json:"altitudeFeet"`
	GroundSpeedKnots   float64 `json:"groundSpeedKnots"`
	GroundTrackDegrees float64 `json:"groundTrackDegrees"`
	HeadingDegrees     float64 `json:"headingDegrees"`
	VerticalSpeedFpm   float64 `json:"verticalSpeedFpm"`
	Model              string  `json:"model"`
	Paused             bool    `json:"paused"`
	OnGround           bool    `json:"onGround"`
	SimRate            float64 `json:"simRate"`
	AircraftType       string  `json:"aircraftType,omitempty"`
	AircraftModel      string  `json:"aircraftModel,omitempty"`
	Registration       string  `json:"registration,omitempty"`
	Livery             string  `json:"livery,omitempty"`
}

func (t telemetry) valid() bool {
	for _, s := range []string{t.Model, t.AircraftType, t.AircraftModel, t.Registration, t.Livery} {
		if len(s) > 160 {
			return false
		}
	}
	for _, v := range []float64{t.SampleAgeMs, t.Latitude, t.Longitude, t.AltitudeFeet, t.GroundSpeedKnots, t.GroundTrackDegrees, t.HeadingDegrees, t.VerticalSpeedFpm, t.SimRate} {
		if math.IsNaN(v) || math.IsInf(v, 0) {
			return false
		}
	}
	return t.Sequence > 0 && t.SampleTimeMs > 0 && t.SampleAgeMs >= 0 && t.SampleAgeMs <= 2000 && math.Abs(t.Latitude) <= 90 && math.Abs(t.Longitude) <= 180 && t.AltitudeFeet >= -2000 && t.AltitudeFeet <= 100000 && t.GroundSpeedKnots >= 0 && t.GroundSpeedKnots <= 1500 && t.GroundTrackDegrees >= 0 && t.GroundTrackDegrees < 360 && t.HeadingDegrees >= 0 && t.HeadingDegrees < 360 && math.Abs(t.VerticalSpeedFpm) <= 20000 && len(t.Model) <= 160 && t.SimRate > 0 && t.SimRate <= 128
}

type message struct {
	Type       string     `json:"type"`
	Protocol   int        `json:"protocol"`
	Create     bool       `json:"create"`
	PublicRoom bool       `json:"publicRoom"`
	Room       string     `json:"room"`
	ID         string     `json:"id"`
	Token      string     `json:"token"`
	Name       string     `json:"name"`
	Share      bool       `json:"share"`
	Lead       string     `json:"lead"`
	Telemetry  *telemetry `json:"telemetry"`
	Echo       int64      `json:"echo"`
}
type member struct {
	ID     string `json:"id"`
	Name   string `json:"name"`
	Share  bool   `json:"share"`
	Lead   string `json:"lead"`
	Online bool   `json:"online"`
	secret [32]byte
	peer   *peer
	leftAt time.Time
}
type room struct {
	members map[string]*member
	touched time.Time
}
type peer struct {
	ws          *websocket.Conn
	send        chan any
	room        string
	id          string
	done        chan struct{}
	once        sync.Once
	sequence    int64
	sample      int64
	last        time.Time
	clockOffset int64
	position    *telemetry
	discovery   map[string]time.Time
}

func (p *peer) close() { p.once.Do(func() { close(p.done); p.ws.Close() }) }
func (p *peer) enqueue(v any) {
	select {
	case <-p.done:
		return
	default:
	}
	select {
	case p.send <- v:
	default:
		p.close()
	}
}

type hub struct {
	mu          sync.Mutex
	rooms       map[string]*room
	releases    string
	maxClients  int
	connections int
}

func newHub(releases string) *hub {
	return &hub{rooms: map[string]*room{}, releases: releases, maxClients: 512}
}
func randomCode() string {
	b := make([]byte, 10)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return fmt.Sprintf("%X", b)
}
func (h *hub) roster(r *room) []member {
	out := []member{}
	for _, m := range r.members {
		if r == h.rooms["PUBLIC"] && !m.Online {
			continue
		}
		out = append(out, *m)
	}
	return out
}
func (h *hub) broadcast(r *room, v any) {
	for _, m := range r.members {
		if m.peer != nil {
			m.peer.enqueue(v)
		}
	}
}
func (h *hub) rosterChanged(r *room) {
	h.broadcast(r, map[string]any{"type": "roster", "members": h.roster(r)})
}
func (h *hub) join(p *peer, m message) error {
	h.mu.Lock()
	defer h.mu.Unlock()
	if m.Protocol != protocol {
		return errors.New("Incompatible protocol; update the app")
	}
	if !identifier.MatchString(m.ID) || len(m.Token) < 32 || len(m.Token) > 128 || len(m.Name) == 0 || len(m.Name) > 64 {
		return errors.New("Invalid pilot identity")
	}
	now := time.Now()
	if m.Echo <= 0 {
		return errors.New("Client clock required")
	}
	if m.PublicRoom {
		m.Room = "PUBLIC"
		m.Create = false
		if h.rooms[m.Room] == nil {
			h.rooms[m.Room] = &room{members: map[string]*member{}, touched: now}
		}
		// Public discovery retains active identities, not an ever-growing history of visitors.
		for id, old := range h.rooms[m.Room].members {
			if !old.Online && now.Sub(old.leftAt) > time.Hour {
				delete(h.rooms[m.Room].members, id)
			}
		}
	}
	for code, r := range h.rooms {
		online := false
		for _, v := range r.members {
			online = online || v.Online
		}
		if !online && now.Sub(r.touched) > time.Hour && !(m.PublicRoom && code == "PUBLIC") {
			delete(h.rooms, code)
		}
	}
	if m.Create {
		if len(h.rooms) >= 256 {
			return errors.New("Server room limit reached")
		}
		m.Room = randomCode()
		h.rooms[m.Room] = &room{members: map[string]*member{}, touched: now}
	}
	r := h.rooms[m.Room]
	if (!codePattern.MatchString(m.Room) && !(m.PublicRoom && m.Room == "PUBLIC")) || r == nil {
		return errors.New("Room not found; check the invitation code")
	}
	digest := sha256.Sum256([]byte(m.Token))
	v := r.members[m.ID]
	if v != nil {
		if subtle.ConstantTimeCompare(digest[:], v.secret[:]) != 1 {
			return errors.New("Pilot identity authentication failed")
		}
		if v.peer != nil {
			v.peer.close()
		}
	} else {
		limit := 32
		count := len(r.members)
		if m.PublicRoom {
			limit = 128
			count = 0
			for _, pilot := range r.members {
				if pilot.Online {
					count++
				}
			}
		}
		if count >= limit || len(r.members) >= 8192 {
			return errors.New("Server pilot limit reached")
		}
		v = &member{ID: m.ID, secret: digest}
		r.members[m.ID] = v
	}
	p.clockOffset = now.UnixMilli() - m.Echo
	v.Name = m.Name
	v.Share = m.Share
	v.Online = true
	v.peer = p
	v.Lead = ""
	p.room = m.Room
	p.id = m.ID
	r.touched = now
	p.enqueue(map[string]any{"type": "welcome", "protocol": protocol, "room": m.Room, "id": m.ID, "serverTimeMs": now.UnixMilli()})
	h.rosterChanged(r)
	return nil
}
func (h *hub) configure(p *peer, m message) error {
	h.mu.Lock()
	defer h.mu.Unlock()
	r := h.rooms[p.room]
	if r == nil {
		return errors.New("Room expired")
	}
	self := r.members[p.id]
	if self.peer != p {
		return errors.New("Connection replaced")
	}
	if m.Lead != "" {
		lead := r.members[m.Lead]
		if lead == nil || !lead.Share || !lead.Online {
			return errors.New("Selected lead is not sharing")
		}
		seen := map[string]bool{p.id: true}
		id := m.Lead
		for id != "" {
			if seen[id] {
				return errors.New("Cannot form a follow loop; choose an aircraft ahead of you")
			}
			seen[id] = true
			v := r.members[id]
			if v == nil {
				break
			}
			id = v.Lead
		}
	}
	self.Share = m.Share
	self.Lead = m.Lead
	if !self.Share {
		h.clearFollowers(r, p.id)
	}
	p.enqueue(map[string]any{"type": "configured", "share": self.Share, "lead": self.Lead})
	h.rosterChanged(r)
	return nil
}
func (h *hub) sample(p *peer, t *telemetry) error {
	return h.positionSample(p, t, false)
}
func (h *hub) positionSample(p *peer, t *telemetry, observer bool) error {
	if t == nil || !t.valid() {
		return errors.New("Invalid telemetry")
	}
	now := time.Now()
	if t.Sequence <= p.sequence || t.SampleTimeMs <= p.sample {
		return errors.New("Telemetry sequence must increase")
	}
	sampleTime := t.SampleTimeMs + p.clockOffset
	age := now.UnixMilli() - sampleTime
	if age < -1000 || age > 2000 {
		return errors.New("Telemetry expired or clock changed; reconnect")
	}
	p.sequence = t.Sequence
	p.sample = t.SampleTimeMs
	h.mu.Lock()
	defer h.mu.Unlock()
	r := h.rooms[p.room]
	if r == nil {
		return errors.New("Room expired")
	}
	self := r.members[p.id]
	if self.peer != p || (!self.Share && !observer) {
		return errors.New("Enable Share my aircraft before publishing")
	}
	p.position = t
	p.last = now
	if observer {
		return nil
	}
	// Do not retain samples or replay old positions to joining clients.
	r.touched = now
	packet := map[string]any{"type": "telemetry", "id": p.id, "name": self.Name, "serverTimeMs": sampleTime, "telemetry": t}
	if p.room != "PUBLIC" {
		h.broadcast(r, packet)
		return nil
	}
	if p.discovery == nil {
		p.discovery = map[string]time.Time{}
	}
	for id, m := range r.members {
		if m.peer == nil || id == p.id {
			continue
		}
		if m.Lead == p.id {
			m.peer.enqueue(packet)
			continue
		}
		other := m.peer.position
		if other != nil && now.Sub(m.peer.last) <= 3*time.Second && distanceNm(t.Latitude, t.Longitude, other.Latitude, other.Longitude) <= 100 && now.Sub(p.discovery[id]) >= time.Second {
			m.peer.enqueue(packet)
			p.discovery[id] = now
		}
	}
	for id := range p.discovery {
		if r.members[id] == nil {
			delete(p.discovery, id)
		}
	}
	return nil
}
func distanceNm(lat1, lon1, lat2, lon2 float64) float64 {
	dlat := (lat2 - lat1) * math.Pi / 180
	dlon := (lon2 - lon1) * math.Pi / 180
	a := math.Sin(dlat/2)*math.Sin(dlat/2) + math.Cos(lat1*math.Pi/180)*math.Cos(lat2*math.Pi/180)*math.Sin(dlon/2)*math.Sin(dlon/2)
	return 3440.065 * 2 * math.Asin(math.Sqrt(math.Min(1, a)))
}
func (h *hub) clearFollowers(r *room, id string) {
	for _, m := range r.members {
		if m.Lead == id {
			m.Lead = ""
			if m.peer != nil {
				m.peer.enqueue(map[string]any{"type": "configured", "share": m.Share, "lead": ""})
			}
		}
	}
}
func (h *hub) leave(p *peer) {
	h.mu.Lock()
	defer h.mu.Unlock()
	r := h.rooms[p.room]
	if r == nil {
		return
	}
	m := r.members[p.id]
	if m != nil && m.peer == p {
		m.peer = nil
		m.Online = false
		m.leftAt = time.Now()
		m.Lead = ""
		h.clearFollowers(r, p.id)
		r.touched = time.Now()
		h.rosterChanged(r)
	}
}

var upgrader = websocket.Upgrader{HandshakeTimeout: 5 * time.Second, CheckOrigin: func(r *http.Request) bool { return r.Header.Get("Origin") == "" }}

func (h *hub) socket(w http.ResponseWriter, r *http.Request) {
	h.mu.Lock()
	if h.connections >= h.maxClients {
		h.mu.Unlock()
		http.Error(w, "Server full", 503)
		return
	}
	h.connections++
	h.mu.Unlock()
	defer func() { h.mu.Lock(); h.connections--; h.mu.Unlock() }()
	ws, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}
	p := &peer{ws: ws, send: make(chan any, 64), done: make(chan struct{})}
	defer p.close()
	defer h.leave(p)
	ws.SetReadLimit(8192)
	ws.SetReadDeadline(time.Now().Add(10 * time.Second))
	var hello message
	if err = ws.ReadJSON(&hello); err != nil || hello.Type != "join" {
		return
	}
	go func() {
		for {
			select {
			case <-p.done:
				return
			case v := <-p.send:
				ws.SetWriteDeadline(time.Now().Add(3 * time.Second))
				if ws.WriteJSON(v) != nil {
					p.close()
					return
				}
			}
		}
	}()
	if err = h.join(p, hello); err != nil {
		ws.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(1008, err.Error()), time.Now().Add(time.Second))
		return
	}
	var window = time.Now()
	count := 0
	for {
		ws.SetReadDeadline(time.Now().Add(15 * time.Second))
		var m message
		if ws.ReadJSON(&m) != nil {
			return
		}
		count++
		if time.Since(window) >= time.Second {
			window = time.Now()
			count = 1
		}
		if count > 25 {
			return
		}
		switch m.Type {
		case "configure":
			err = h.configure(p, m)
		case "telemetry":
			err = h.sample(p, m.Telemetry)
		case "observer":
			err = h.positionSample(p, m.Telemetry, true)
		case "unavailable":
			h.mu.Lock()
			if room := h.rooms[p.room]; room != nil && room.members[p.id].peer == p {
				p.position = nil
				h.broadcast(room, map[string]any{"type": "unavailable", "id": p.id})
			}
			h.mu.Unlock()
			err = nil
		case "ping":
			p.enqueue(map[string]any{"type": "pong", "echo": m.Echo, "serverTimeMs": time.Now().UnixMilli()})
			err = nil
		default:
			err = errors.New("Unknown message")
		}
		if err != nil {
			p.enqueue(map[string]any{"type": "error", "message": err.Error()})
		}
	}
}
func (h *hub) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		fmt.Fprintf(w, `{"ok":true,"protocol":%d}`, protocol)
	})
	mux.HandleFunc("GET /v1/session", h.socket)
	mux.HandleFunc("GET /v1/update", func(w http.ResponseWriter, r *http.Request) {
		data, err := os.ReadFile(filepath.Join(h.releases, "latest.json"))
		if err != nil {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		manifest, err := decodeRelease(data, nil)
		if err != nil {
			http.Error(w, "Release metadata unavailable", 503)
			return
		}
		current, err := parseVersion(r.URL.Query().Get("version"))
		if err != nil {
			http.Error(w, "Invalid client version", 400)
			return
		}
		next, _ := parseVersion(manifest.Version)
		w.Header().Set("Cache-Control", "no-store")
		if compareVersions(next, current) <= 0 {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		w.Write(data)
	})
	mux.HandleFunc("GET /releases/{file}", func(w http.ResponseWriter, r *http.Request) {
		name := r.PathValue("file")
		if !releaseName.MatchString(name) {
			http.NotFound(w, r)
			return
		}
		http.ServeFile(w, r, filepath.Join(h.releases, name))
	})
	return mux
}
func main() {
	if len(os.Args) > 1 && os.Args[1] == "keygen" {
		if err := keygen(os.Args[2:]); err != nil {
			log.Fatal(err)
		}
		return
	}
	if len(os.Args) > 1 && os.Args[1] == "publish" {
		if err := publish(os.Args[2:]); err != nil {
			log.Fatal(err)
		}
		return
	}
	listen := flag.String("listen", "127.0.0.1:8787", "HTTP bind address (reverse proxy supplies HTTPS)")
	releases := flag.String("releases", "./releases", "Signed release directory")
	cert := flag.String("tls-cert", "", "Optional TLS certificate PEM")
	key := flag.String("tls-key", "", "Optional TLS private key PEM")
	flag.Parse()
	h := newHub(*releases)
	server := &http.Server{Addr: *listen, Handler: h.routes(), ReadHeaderTimeout: 5 * time.Second, IdleTimeout: 30 * time.Second, MaxHeaderBytes: 16384}
	done := make(chan os.Signal, 1)
	signal.Notify(done, os.Interrupt, syscall.SIGTERM)
	go func() {
		<-done
		h.mu.Lock()
		for _, r := range h.rooms {
			for _, m := range r.members {
				if m.peer != nil {
					m.peer.close()
				}
			}
		}
		h.mu.Unlock()
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		server.Shutdown(ctx)
	}()
	log.Printf("WingMan relay protocol %d listening on %s", protocol, *listen)
	var err error
	if *cert != "" {
		err = server.ListenAndServeTLS(*cert, *key)
	} else {
		err = server.ListenAndServe()
	}
	if err != nil && err != http.ErrServerClosed {
		log.Fatal(err)
	}
}
