package main

import (
	"bytes"
	"compress/gzip"
	"encoding/json"
	"io"
	"io/fs"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"
	"testing/fstest"
)

func handlerForTest(t *testing.T, base string) *webHandler {
	t.Helper()
	files, err := fs.Sub(embeddedFiles, "dist")
	if err != nil {
		t.Fatal(err)
	}
	handler, err := newWebHandler(files, base)
	if err != nil {
		t.Fatal(err)
	}
	return handler
}

func requestTo(h http.Handler, method, target string, headers map[string]string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, target, nil)
	for key, value := range headers {
		req.Header.Set(key, value)
	}
	res := httptest.NewRecorder()
	h.ServeHTTP(res, req)
	return res
}

func TestEmbeddedFilesAndMIMETypes(t *testing.T) {
	h := handlerForTest(t, "/")
	for _, test := range []struct{ name, contentType, contains string }{
		{"/", "text/html", "题库选择器"},
		{"/index.html", "text/html", "选择器"},
		{"/style.css", "text/css", ".card"},
		{"/app.js", "text/javascript", "ChooserCore"},
		{"/default.xml", "application/xml", "<quiz>"},
		{"/manifest.webmanifest", "application/manifest+json", "选择器"},
	} {
		t.Run(test.name, func(t *testing.T) {
			res := requestTo(h, http.MethodGet, test.name, nil)
			if res.Code != http.StatusOK || !strings.HasPrefix(res.Header().Get("Content-Type"), test.contentType) || !strings.Contains(res.Body.String(), test.contains) {
				t.Fatalf("status=%d type=%s body-prefix=%q", res.Code, res.Header().Get("Content-Type"), res.Body.String()[:min(60, res.Body.Len())])
			}
			if res.Header().Get("X-Content-Type-Options") != "nosniff" {
				t.Fatal("missing MIME protection")
			}
		})
	}
}

func TestMethodsAndNoSourceLeak(t *testing.T) {
	h := handlerForTest(t, "/")
	for _, method := range []string{http.MethodPost, http.MethodPut, http.MethodDelete, http.MethodOptions} {
		res := requestTo(h, method, "/", nil)
		if res.Code != http.StatusMethodNotAllowed || res.Header().Get("Allow") != "GET, HEAD" {
			t.Errorf("%s: %d Allow=%q", method, res.Code, res.Header().Get("Allow"))
		}
	}
	for _, target := range []string{"/main.go", "/go.mod", "/_headers", "/.openai/hosting.json", "/.git/config", "/../main.go", "/%2e%2e/main.go", "/dist/", "/api/upload", "/api/answers", "/foo//bar", "/foo%5cbar"} {
		if res := requestTo(h, http.MethodGet, target, nil); res.Code != http.StatusNotFound {
			t.Errorf("%s leaked with status %d", target, res.Code)
		}
	}
}

func TestEntityTagsAndCacheHeaders(t *testing.T) {
	h := handlerForTest(t, "/")
	first := requestTo(h, http.MethodGet, "/app.js?v=1.4.0", nil)
	tag := first.Header().Get("ETag")
	if tag == "" || strings.HasPrefix(tag, "W/") || first.Header().Get("Cache-Control") != "public, max-age=31536000, immutable" {
		t.Fatalf("invalid cache headers: %v", first.Header())
	}
	for _, value := range []string{tag, "W/" + tag, `"other", ` + tag, "*"} {
		res := requestTo(h, http.MethodGet, "/app.js?v=1.4.0", map[string]string{"If-None-Match": value})
		if res.Code != http.StatusNotModified || res.Body.Len() != 0 || res.Header().Get("ETag") != tag {
			t.Errorf("conditional %q: code=%d body=%d", value, res.Code, res.Body.Len())
		}
	}
	for _, target := range []string{"/", "/index.html?v=1.4.0", "/sw.js?v=1.4.0"} {
		if res := requestTo(h, http.MethodGet, target, nil); res.Header().Get("Cache-Control") != "no-cache" {
			t.Errorf("%s must revalidate", target)
		}
	}
	if res := requestTo(h, http.MethodGet, "/sw.js", nil); res.Header().Get("Service-Worker-Allowed") != "/" {
		t.Fatal("invalid service worker scope")
	}
}

func TestGzipRepresentationsAndHead(t *testing.T) {
	h := handlerForTest(t, "/")
	identity := requestTo(h, http.MethodGet, "/default.xml", nil)
	compressed := requestTo(h, http.MethodGet, "/default.xml", map[string]string{"Accept-Encoding": "gzip, deflate, br"})
	if compressed.Code != http.StatusOK || compressed.Header().Get("Content-Encoding") != "gzip" || compressed.Header().Get("Vary") != "Accept-Encoding" || compressed.Body.Len() >= identity.Body.Len() {
		t.Fatalf("invalid gzip response: %d %v", compressed.Code, compressed.Header())
	}
	reader, err := gzip.NewReader(bytes.NewReader(compressed.Body.Bytes()))
	if err != nil {
		t.Fatal(err)
	}
	decoded, err := io.ReadAll(reader)
	_ = reader.Close()
	if err != nil || !bytes.Equal(decoded, identity.Body.Bytes()) {
		t.Fatal("gzip did not reproduce original file")
	}
	if identity.Header().Get("ETag") == compressed.Header().Get("ETag") {
		t.Fatal("strong ETag reused for different representation")
	}
	wrongTag := requestTo(h, http.MethodGet, "/default.xml", map[string]string{"Accept-Encoding": "gzip", "If-None-Match": identity.Header().Get("ETag")})
	if wrongTag.Code != http.StatusOK {
		t.Fatal("identity ETag incorrectly validated gzip")
	}
	head := requestTo(h, http.MethodHead, "/default.xml", map[string]string{"Accept-Encoding": "gzip"})
	if head.Code != http.StatusOK || head.Body.Len() != 0 || head.Header().Get("Content-Length") != strconv.Itoa(compressed.Body.Len()) || head.Header().Get("ETag") != compressed.Header().Get("ETag") {
		t.Fatalf("bad HEAD response: %d %v", head.Code, head.Header())
	}
	for _, header := range []string{"gzip;q=0", "gzip;q=0,*;q=1", "GZIP; q=0", "gzip;q=bogus"} {
		res := requestTo(h, http.MethodGet, "/default.xml", map[string]string{"Accept-Encoding": header})
		if res.Header().Get("Content-Encoding") != "" || !bytes.Equal(res.Body.Bytes(), identity.Body.Bytes()) {
			t.Errorf("gzip incorrectly selected for %q", header)
		}
	}
	if res := requestTo(h, http.MethodGet, "/default.xml", map[string]string{"Accept-Encoding": "gzip;q=0,identity;q=0"}); res.Code != http.StatusNotAcceptable {
		t.Fatal("unacceptable encodings not rejected")
	}
}

func TestBasePathAndHealthEndpoints(t *testing.T) {
	h := handlerForTest(t, "/chooser")
	if res := requestTo(h, http.MethodGet, "/chooser?x=1", nil); res.Code != http.StatusPermanentRedirect || res.Header().Get("Location") != "/chooser/?x=1" {
		t.Fatalf("bad base redirect: %d %v", res.Code, res.Header())
	}
	for _, target := range []string{"/chooser/", "/chooser/index.html", "/chooser/default.xml", "/chooser/sw.js"} {
		if res := requestTo(h, http.MethodGet, target, nil); res.Code != http.StatusOK {
			t.Errorf("base route %s: %d", target, res.Code)
		}
	}
	for _, target := range []string{"/", "/default.xml", "/chooser2/", "/healthz"} {
		if res := requestTo(h, http.MethodGet, target, nil); res.Code != http.StatusNotFound {
			t.Errorf("route escaped base prefix: %s", target)
		}
	}
	for _, target := range []string{"/chooser/healthz", "/chooser/api/version"} {
		res := requestTo(h, http.MethodGet, target, nil)
		var data map[string]string
		if res.Code != http.StatusOK || json.Unmarshal(res.Body.Bytes(), &data) != nil || data["version"] != version || res.Header().Get("Cache-Control") != "no-store" {
			t.Errorf("bad JSON endpoint %s: %d %s", target, res.Code, res.Body.String())
		}
		head := requestTo(h, http.MethodHead, target, nil)
		if head.Code != http.StatusOK || head.Body.Len() != 0 || head.Header().Get("Content-Length") != res.Header().Get("Content-Length") {
			t.Errorf("bad JSON HEAD: %s", target)
		}
	}
}

func TestHelpAndHiddenAssets(t *testing.T) {
	files := fstest.MapFS{
		"index.html":           {Data: []byte("<!doctype html><title>选择器</title>")},
		"help.html":            {Data: []byte("<!doctype html><h1>题库 XML 写作指南</h1>")},
		"examples/quiz.xml":    {Data: []byte("<quiz></quiz>")},
		".openai/hosting.json": {Data: []byte(`{"secret":"hidden"}`)},
		"_headers":             {Data: []byte("hidden config")},
		"main.go":              {Data: []byte("source")},
	}
	h, err := newWebHandler(files, "/")
	if err != nil {
		t.Fatal(err)
	}
	if res := requestTo(h, http.MethodGet, "/help.html", nil); res.Code != http.StatusOK || res.Header().Get("Cache-Control") != "no-cache" || !strings.Contains(res.Body.String(), "XML") {
		t.Fatal("help page not served")
	}
	if res := requestTo(h, http.MethodGet, "/examples/quiz.xml", nil); res.Code != http.StatusOK {
		t.Fatal("nested public example not served")
	}
	for _, target := range []string{"/.openai/hosting.json", "/_headers", "/main.go", "/examples/"} {
		if res := requestTo(h, http.MethodGet, target, nil); res.Code != http.StatusNotFound {
			t.Fatalf("hidden source/config or directory exposed: %s", target)
		}
	}
	for _, base := range []string{"chooser", "/../chooser", "/chooser//", "/chooser?x=1", "/chooser\\"} {
		if _, err := newWebHandler(files, base); err == nil {
			t.Errorf("invalid base accepted: %s", base)
		}
	}
}
