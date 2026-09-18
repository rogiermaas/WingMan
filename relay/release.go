package main

import (
	"archive/zip"
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"encoding/pem"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
)

var releaseName = regexp.MustCompile(`^WingMan-[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?-win-x64\.zip$`)

type release struct {
	Version     string `json:"version"`
	Protocol    int    `json:"protocol"`
	File        string `json:"file"`
	SHA256      string `json:"sha256"`
	Size        int64  `json:"size"`
	PublishedAt string `json:"publishedAt"`
}
type envelope struct {
	Payload   string `json:"payload"`
	Signature string `json:"signature"`
}

func parseVersion(s string) ([4]int, error) {
	var v [4]int
	parts := strings.Split(s, ".")
	if len(parts) < 3 || len(parts) > 4 {
		return v, errors.New("Version must have 3 or 4 numeric parts")
	}
	for i, p := range parts {
		n, e := strconv.Atoi(p)
		if e != nil || n < 0 || n > 65535 {
			return v, errors.New("Invalid version")
		}
		v[i] = n
	}
	return v, nil
}
func compareVersions(a, b [4]int) int {
	for i := range a {
		if a[i] < b[i] {
			return -1
		}
		if a[i] > b[i] {
			return 1
		}
	}
	return 0
}
func decodeRelease(data []byte, key *rsa.PublicKey) (release, error) {
	var env envelope
	var out release
	if len(data) > 16384 {
		return out, errors.New("Manifest too large")
	}
	if err := json.Unmarshal(data, &env); err != nil {
		return out, err
	}
	payload, e := base64.StdEncoding.DecodeString(env.Payload)
	if e != nil {
		return out, e
	}
	if key != nil {
		sig, e := base64.StdEncoding.DecodeString(env.Signature)
		if e != nil {
			return out, e
		}
		hash := sha256.Sum256(payload)
		if e = rsa.VerifyPKCS1v15(key, crypto.SHA256, hash[:], sig); e != nil {
			return out, e
		}
	}
	if e = json.Unmarshal(payload, &out); e != nil {
		return out, e
	}
	if _, e = parseVersion(out.Version); e != nil {
		return out, e
	}
	if !releaseName.MatchString(out.File) || out.File != "WingMan-"+out.Version+"-win-x64.zip" || len(out.SHA256) != 64 || out.Size <= 0 || out.Size > 300*1024*1024 || out.Protocol != protocol {
		return out, errors.New("Invalid release")
	}
	if _, err := hex.DecodeString(out.SHA256); err != nil {
		return out, err
	}
	return out, nil
}
func writeNew(path string, data []byte, mode os.FileMode) error {
	f, e := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, mode)
	if e != nil {
		return e
	}
	defer f.Close()
	_, e = f.Write(data)
	return e
}
func keygen(args []string) error {
	f := flag.NewFlagSet("keygen", flag.ContinueOnError)
	private := f.String("private", "", "Private key output (keep offline)")
	public := f.String("public", "", "Public key output (embed in Windows app)")
	if e := f.Parse(args); e != nil {
		return e
	}
	if *private == "" || *public == "" {
		return errors.New("Specify -private and -public paths")
	}
	k, e := rsa.GenerateKey(rand.Reader, 3072)
	if e != nil {
		return e
	}
	priv, e := x509.MarshalPKCS8PrivateKey(k)
	if e != nil {
		return e
	}
	pub, e := x509.MarshalPKIXPublicKey(&k.PublicKey)
	if e != nil {
		return e
	}
	if e = writeNew(*private, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: priv}), 0600); e != nil {
		return e
	}
	return writeNew(*public, pem.EncodeToMemory(&pem.Block{Type: "PUBLIC KEY", Bytes: pub}), 0644)
}
func publish(args []string) error {
	f := flag.NewFlagSet("publish", flag.ContinueOnError)
	version := f.String("version", "", "Release version")
	source := f.String("source", "", "Windows publish folder")
	dest := f.String("out", "releases", "Output directory")
	keyPath := f.String("key", "", "Offline private key")
	if e := f.Parse(args); e != nil {
		return e
	}
	if _, e := parseVersion(*version); e != nil {
		return e
	}
	keyBytes, e := os.ReadFile(*keyPath)
	if e != nil {
		return e
	}
	block, _ := pem.Decode(keyBytes)
	if block == nil {
		return errors.New("Invalid PEM key")
	}
	parsed, e := x509.ParsePKCS8PrivateKey(block.Bytes)
	if e != nil {
		return e
	}
	key, ok := parsed.(*rsa.PrivateKey)
	if !ok {
		return errors.New("RSA key required")
	}
	exe := filepath.Join(*source, "WingMan.exe")
	if _, e = os.Stat(exe); e != nil {
		return e
	}
	if e = os.MkdirAll(*dest, 0755); e != nil {
		return e
	}
	name := "WingMan-" + *version + "-win-x64.zip"
	if !releaseName.MatchString(name) {
		return errors.New("Invalid version filename")
	}
	zipPath := filepath.Join(*dest, name)
	output, e := os.OpenFile(zipPath, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0644)
	if e != nil {
		return e
	}
	z := zip.NewWriter(output)
	entry, e := z.Create("WingMan.exe")
	if e != nil {
		output.Close()
		return e
	}
	input, e := os.Open(exe)
	if e != nil {
		output.Close()
		return e
	}
	_, e = io.Copy(entry, input)
	input.Close()
	if e != nil {
		output.Close()
		return e
	}
	if e = z.Close(); e != nil {
		output.Close()
		return e
	}
	if e = output.Close(); e != nil {
		return e
	}
	// Only the self-contained executable is replaced. Settings, logs and the locally supplied SimConnect DLL remain intact.
	archive, e := os.ReadFile(zipPath)
	if e != nil {
		return e
	}
	hash := sha256.Sum256(archive)
	payload, e := json.Marshal(release{*version, protocol, name, hex.EncodeToString(hash[:]), int64(len(archive)), time.Now().UTC().Format(time.RFC3339)})
	if e != nil {
		return e
	}
	digest := sha256.Sum256(payload)
	signature, e := rsa.SignPKCS1v15(rand.Reader, key, crypto.SHA256, digest[:])
	if e != nil {
		return e
	}
	data, e := json.MarshalIndent(envelope{base64.StdEncoding.EncodeToString(payload), base64.StdEncoding.EncodeToString(signature)}, "", "  ")
	if e != nil {
		return e
	}
	temp := filepath.Join(*dest, "latest.json.tmp")
	if e = os.WriteFile(temp, data, 0644); e != nil {
		return e
	}
	if e = os.Rename(temp, filepath.Join(*dest, "latest.json")); e != nil {
		return e
	}
	fmt.Println("Signed release created:", zipPath)
	return nil
}
