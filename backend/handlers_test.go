package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
)

const validBody = `{"title":"  墙面破损  ","description":"裂缝","priority":"high","position":{"x":0,"y":0.03,"z":1.26}}`

func call(h http.Handler, method, path, body string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	r.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	return w
}

func TestValidationAndErrors(t *testing.T) {
	s, err := openStore(filepath.Join(t.TempDir(), "issues.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer s.db.Close()
	h := newHandler(s, "http://localhost:5173")
	for name, body := range map[string]string{
		"blank title":      strings.Replace(validBody, "  墙面破损  ", "  ", 1),
		"bad priority":     strings.Replace(validBody, "high", "urgent", 1),
		"missing x":        strings.Replace(validBody, `"x":0,`, "", 1),
		"null x":           strings.Replace(validBody, `"x":0`, `"x":null`, 1),
		"missing position": `{"title":"a","priority":"low"}`,
		"bad JSON":         `{`, "null body": `null`, "multiple JSON": validBody + `{}`,
	} {
		t.Run(name, func(t *testing.T) {
			w := call(h, "POST", "/api/issues", body)
			if w.Code != 400 {
				t.Fatalf("status=%d body=%s", w.Code, w.Body)
			}
			var data struct {
				Error struct {
					Message string `json:"message"`
				} `json:"error"`
			}
			if err := json.Unmarshal(w.Body.Bytes(), &data); err != nil || data.Error.Message == "" {
				t.Fatalf("missing clear error: %s", w.Body)
			}
		})
	}
	if w := call(h, "PATCH", "/api/issues/missing/status", `{"status":"done"}`); w.Code != 400 {
		t.Fatalf("bad status: %s", w.Body)
	}
	if w := call(h, "PATCH", "/api/issues/missing/status", `{"status":"resolved"}`); w.Code != 404 {
		t.Fatalf("missing ID: %s", w.Body)
	}
	if w := call(h, "GET", "/api/issues", ""); !strings.Contains(w.Body.String(), `"items":[]`) {
		t.Fatalf("empty array: %s", w.Body)
	}
}

func TestLifecycleAndPersistence(t *testing.T) {
	path := filepath.Join(t.TempDir(), "issues.db")
	s, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	h := newHandler(s, "http://localhost:5173")
	w := call(h, "POST", "/api/issues", validBody)
	if w.Code != 201 {
		t.Fatalf("create: %d %s", w.Code, w.Body)
	}
	var created Issue
	if err := json.Unmarshal(w.Body.Bytes(), &created); err != nil {
		t.Fatal(err)
	}
	if created.ID == "" || created.Title != "墙面破损" || created.Status != "open" || created.CreatedAt == "" || created.UpdatedAt == "" || created.Position.X != 0 {
		t.Fatalf("bad issue: %+v", created)
	}
	w2 := call(h, "POST", "/api/issues", validBody)
	var second Issue
	if err := json.Unmarshal(w2.Body.Bytes(), &second); err != nil {
		t.Fatal(err)
	}
	if second.ID == created.ID {
		t.Fatal("IDs must be unique")
	}
	for _, status := range []string{"in_progress", "resolved", "open", "resolved"} {
		w = call(h, "PATCH", "/api/issues/"+created.ID+"/status", `{"status":"`+status+`"}`)
		if w.Code != 200 {
			t.Fatalf("update: %d %s", w.Code, w.Body)
		}
		var updated Issue
		if err := json.Unmarshal(w.Body.Bytes(), &updated); err != nil {
			t.Fatal(err)
		}
		if updated.Status != status || updated.CreatedAt != created.CreatedAt || updated.UpdatedAt < created.UpdatedAt {
			t.Fatalf("bad update: %+v", updated)
		}
	}
	if err := s.db.Close(); err != nil {
		t.Fatal(err)
	}
	s, err = openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	defer s.db.Close()
	w = call(newHandler(s, ""), "GET", "/api/issues", "")
	var data struct {
		Items []Issue `json:"items"`
	}
	if err := json.Unmarshal(w.Body.Bytes(), &data); err != nil {
		t.Fatal(err)
	}
	if len(data.Items) != 2 {
		t.Fatalf("lost data: %s", w.Body)
	}
	found := false
	for _, item := range data.Items {
		if item.ID == created.ID && item.Status == "resolved" {
			found = true
		}
	}
	if !found {
		t.Fatalf("lost latest status: %s", w.Body)
	}
}

func TestCORS(t *testing.T) {
	h := newHandler(nil, "http://localhost:5173")
	for _, origin := range []string{"http://localhost:5173", "http://unexpected.example"} {
		r := httptest.NewRequest("OPTIONS", "/api/issues/a/status", nil)
		r.Header.Set("Origin", origin)
		r.Header.Set("Access-Control-Request-Method", "PATCH")
		w := httptest.NewRecorder()
		h.ServeHTTP(w, r)
		if origin == "http://localhost:5173" {
			if w.Code != 204 || w.Header().Get("Access-Control-Allow-Origin") != origin || !strings.Contains(w.Header().Get("Access-Control-Allow-Methods"), "PATCH") {
				t.Fatalf("preflight failed: %v", w)
			}
		} else if w.Code != 403 || w.Header().Get("Access-Control-Allow-Origin") != "" {
			t.Fatal("unexpected origin allowed")
		}
	}
}
