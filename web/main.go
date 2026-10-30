// Chooser's small HTTP server uses only the Go standard library. All quiz state
// stays in the browser; this process serves embedded application files only.
package main

import (
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"crypto/tls"
	"embed"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io/fs"
	"log"
	"mime"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"time"
)

const version = "1.4.0"

//go:embed dist/*
var embeddedFiles embed.FS

type asset struct {
	body, compressed []byte
	etag, gzipETag   string
	contentType      string
}

type webHandler struct {
	base   string
	assets map[string]asset
}

func main() {
	addr := flag.String("addr", "0.0.0.0:8088", "HTTP listen address")
	base := flag.String("base-path", "/", "URL prefix, for example /chooser/")
	cert := flag.String("cert", "", "HTTPS certificate PEM file; requires -key")
	key := flag.String("key", "", "HTTPS private key PEM file; requires -cert")
	showVersion := flag.Bool("version", false, "print version and exit")
	flag.Parse()
	if *showVersion {
		fmt.Printf("Chooser server %s (%s, %s/%s)\n", version, runtime.Version(), runtime.GOOS, runtime.GOARCH)
		return
	}
	if (*cert == "") != (*key == "") {
		log.Fatal("HTTPS requires both -cert and -key")
	}
	files, err := fs.Sub(embeddedFiles, "dist")
	if err != nil {
		log.Fatal(err)
	}
	handler, err := newWebHandler(files, *base)
	if err != nil {
		log.Fatal(err)
	}
	listener, err := net.Listen("tcp", *addr)
	if err != nil {
		log.Fatal(err)
	}
	server := &http.Server{
		Handler:           handler,
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       15 * time.Second,
		WriteTimeout:      30 * time.Second,
		IdleTimeout:       60 * time.Second,
		MaxHeaderBytes:    16 << 10,
		TLSConfig:         &tls.Config{MinVersion: tls.VersionTLS12},
	}
	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	defer signal.Stop(stop)
	go func() {
		<-stop
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = server.Shutdown(ctx)
	}()
	protocol := "http"
	if *cert != "" {
		protocol = "https"
	}
	fmt.Printf("Chooser server v%s\nListen: %s://%s%s\n", version, protocol, listener.Addr(), handler.base)
	fmt.Println("Quiz banks, progress and answers are saved in each browser; no answer-upload endpoint is provided.")
	if *cert != "" {
		err = server.ServeTLS(listener, *cert, *key)
	} else {
		err = server.Serve(listener)
	}
	if err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatal(err)
	}
}

func normalizeBase(value string) (string, error) {
	value = strings.TrimSpace(value)
	if value == "" {
		return "/", nil
	}
	if !strings.HasPrefix(value, "/") || strings.ContainsAny(value, "\\?#\x00") {
		return "", errors.New("base-path must be an absolute URL path, for example /chooser/")
	}
	for _, component := range strings.Split(value, "/") {
		if component == "." || component == ".." {
			return "", errors.New("base-path must not contain dot path components")
		}
	}
	if strings.Contains(value, "//") {
		return "", errors.New("base-path must not contain repeated slashes")
	}
	return strings.TrimSuffix(value, "/") + "/", nil
}

func newWebHandler(files fs.FS, base string) (*webHandler, error) {
	prefix, err := normalizeBase(base)
	if err != nil {
		return nil, err
	}
	handler := &webHandler{base: prefix, assets: make(map[string]asset)}
	err = fs.WalkDir(files, ".", func(name string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if name != "." && (strings.HasPrefix(entry.Name(), ".") || strings.HasPrefix(entry.Name(), "_")) {
			if entry.IsDir() {
				return fs.SkipDir
			}
			return nil
		}
		if entry.IsDir() || !publicExtension(path.Ext(name)) {
			return nil
		}
		body, readErr := fs.ReadFile(files, name)
		if readErr != nil {
			return readErr
		}
		item := asset{body: body, etag: entityTag(body), contentType: contentType(name)}
		if compressible(name) && len(body) >= 256 {
			var buffer bytes.Buffer
			writer, writeErr := gzip.NewWriterLevel(&buffer, gzip.BestCompression)
			if writeErr != nil {
				return writeErr
			}
			if _, writeErr = writer.Write(body); writeErr != nil {
				return writeErr
			}
			if writeErr = writer.Close(); writeErr != nil {
				return writeErr
			}
			if buffer.Len()+32 < len(body) {
				item.compressed = buffer.Bytes()
				item.gzipETag = entityTag(item.compressed)
			}
		}
		handler.assets[name] = item
		return nil
	})
	if err != nil {
		return nil, err
	}
	if _, exists := handler.assets["index.html"]; !exists {
		return nil, errors.New("embedded application is missing dist/index.html")
	}
	return handler, nil
}

func (h *webHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("X-Content-Type-Options", "nosniff")
	w.Header().Set("Referrer-Policy", "same-origin")
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		w.Header().Set("Allow", "GET, HEAD")
		w.Header().Set("Cache-Control", "no-store")
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}
	requestPath := r.URL.Path
	if strings.ContainsAny(requestPath, "\\\x00") || strings.Contains(requestPath, "//") {
		http.NotFound(w, r)
		return
	}
	for _, component := range strings.Split(requestPath, "/") {
		if component == "." || component == ".." || strings.HasPrefix(component, ".") {
			http.NotFound(w, r)
			return
		}
	}
	if h.base != "/" && requestPath == strings.TrimSuffix(h.base, "/") {
		target := h.base
		if r.URL.RawQuery != "" {
			target += "?" + r.URL.RawQuery
		}
		http.Redirect(w, r, target, http.StatusPermanentRedirect)
		return
	}
	if !strings.HasPrefix(requestPath, h.base) {
		http.NotFound(w, r)
		return
	}
	name := strings.TrimPrefix(requestPath, h.base)
	if name == "healthz" {
		writeJSON(w, r, map[string]string{"status": "ok", "version": version})
		return
	}
	if name == "api/version" {
		writeJSON(w, r, map[string]string{"name": "选择器", "version": version, "backend": "go", "storage": "browser-local"})
		return
	}
	if name == "" {
		name = "index.html"
	}
	item, exists := h.assets[name]
	if !exists {
		http.NotFound(w, r)
		return
	}
	body, tag := item.body, item.etag
	w.Header().Set("Vary", "Accept-Encoding")
	if len(item.compressed) > 0 && acceptsGzip(r.Header.Get("Accept-Encoding")) {
		body, tag = item.compressed, item.gzipETag
		w.Header().Set("Content-Encoding", "gzip")
	} else if rejectsIdentity(r.Header.Get("Accept-Encoding")) {
		http.Error(w, "No acceptable content encoding", http.StatusNotAcceptable)
		return
	}
	w.Header().Set("Content-Type", item.contentType)
	w.Header().Set("ETag", tag)
	switch name {
	case "index.html", "help.html", "sw.js":
		w.Header().Set("Cache-Control", "no-cache")
	default:
		if r.URL.Query().Get("v") != "" {
			w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
		} else {
			w.Header().Set("Cache-Control", "public, max-age=86400")
		}
	}
	if name == "sw.js" {
		w.Header().Set("Service-Worker-Allowed", h.base)
	}
	if matchesEntityTag(r.Header.Get("If-None-Match"), tag) {
		w.WriteHeader(http.StatusNotModified)
		return
	}
	w.Header().Set("Content-Length", strconv.Itoa(len(body)))
	if r.Method == http.MethodHead {
		w.WriteHeader(http.StatusOK)
		return
	}
	_, _ = w.Write(body)
}

func writeJSON(w http.ResponseWriter, r *http.Request, value any) {
	body, _ := json.Marshal(value)
	body = append(body, '\n')
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("Content-Length", strconv.Itoa(len(body)))
	if r.Method != http.MethodHead {
		_, _ = w.Write(body)
	}
}

func entityTag(body []byte) string {
	digest := sha256.Sum256(body)
	return `"` + hex.EncodeToString(digest[:]) + `"`
}

func matchesEntityTag(header, current string) bool {
	for _, part := range strings.Split(header, ",") {
		part = strings.TrimSpace(part)
		if part == "*" || strings.TrimPrefix(part, "W/") == current {
			return true
		}
	}
	return false
}

func encodingQuality(header, name string) (float64, bool) {
	for _, part := range strings.Split(header, ",") {
		fields := strings.Split(part, ";")
		if !strings.EqualFold(strings.TrimSpace(fields[0]), name) {
			continue
		}
		quality := 1.0
		for _, parameter := range fields[1:] {
			pair := strings.SplitN(strings.TrimSpace(parameter), "=", 2)
			if len(pair) == 2 && strings.EqualFold(pair[0], "q") {
				value, err := strconv.ParseFloat(strings.TrimSpace(pair[1]), 64)
				if err != nil || value < 0 || value > 1 {
					value = 0
				}
				quality = value
			}
		}
		return quality, true
	}
	return 0, false
}

func acceptsGzip(header string) bool {
	if quality, exists := encodingQuality(header, "gzip"); exists {
		return quality > 0
	}
	quality, exists := encodingQuality(header, "*")
	return exists && quality > 0
}

func rejectsIdentity(header string) bool {
	if quality, exists := encodingQuality(header, "identity"); exists {
		return quality == 0
	}
	quality, exists := encodingQuality(header, "*")
	return exists && quality == 0
}

func publicExtension(extension string) bool {
	switch strings.ToLower(extension) {
	case ".html", ".css", ".js", ".xml", ".json", ".webmanifest", ".ico", ".png", ".svg", ".jpg", ".jpeg", ".webp", ".woff2", ".txt":
		return true
	}
	return false
}

func compressible(name string) bool {
	switch strings.ToLower(path.Ext(name)) {
	case ".html", ".css", ".js", ".xml", ".json", ".webmanifest", ".svg", ".txt":
		return true
	}
	return false
}

func contentType(name string) string {
	switch strings.ToLower(path.Ext(name)) {
	case ".html":
		return "text/html; charset=utf-8"
	case ".css":
		return "text/css; charset=utf-8"
	case ".js":
		return "text/javascript; charset=utf-8"
	case ".xml":
		return "application/xml; charset=utf-8"
	case ".json":
		return "application/json; charset=utf-8"
	case ".webmanifest":
		return "application/manifest+json; charset=utf-8"
	}
	if value := mime.TypeByExtension(path.Ext(name)); value != "" {
		return value
	}
	return "application/octet-stream"
}
