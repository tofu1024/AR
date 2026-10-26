package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"strings"
)

type api struct{ store *Store }

func writeJSON(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(value); err != nil {
		log.Printf("response: %v", err)
	}
}

func fail(w http.ResponseWriter, status int, code, message string) {
	writeJSON(w, status, map[string]any{"error": map[string]string{"code": code, "message": message}})
}

func decode(w http.ResponseWriter, r *http.Request, target any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, 64*1024)
	d := json.NewDecoder(r.Body)
	d.DisallowUnknownFields()
	if err := d.Decode(target); err != nil {
		fail(w, 400, "INVALID_ARGUMENT", "请求必须是有效 JSON，字段名称和类型必须符合接口约定")
		return false
	}
	if err := d.Decode(new(any)); err != io.EOF {
		fail(w, 400, "INVALID_ARGUMENT", "请求只能包含一个 JSON 对象")
		return false
	}
	return true
}

func (a *api) create(w http.ResponseWriter, r *http.Request) {
	var in CreateIssue
	if !decode(w, r, &in) {
		return
	}
	if err := in.validate(); err != nil {
		fail(w, 400, "INVALID_ARGUMENT", err.Error())
		return
	}
	i, err := a.store.create(r.Context(), in)
	if err != nil {
		serverError(w, err)
		return
	}
	writeJSON(w, http.StatusCreated, i)
}

func (a *api) list(w http.ResponseWriter, r *http.Request) {
	items, err := a.store.list(r.Context())
	if err != nil {
		serverError(w, err)
		return
	}
	writeJSON(w, 200, map[string]any{"items": items})
}

func (a *api) update(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Status string `json:"status"`
	}
	if !decode(w, r, &in) {
		return
	}
	if !validStatus(in.Status) {
		fail(w, 400, "INVALID_ARGUMENT", "status 必须为 open、in_progress 或 resolved")
		return
	}
	i, err := a.store.updateStatus(r.Context(), r.PathValue("id"), in.Status)
	if errors.Is(err, sql.ErrNoRows) {
		fail(w, 404, "NOT_FOUND", "问题不存在")
		return
	}
	if err != nil {
		serverError(w, err)
		return
	}
	writeJSON(w, 200, i)
}

func serverError(w http.ResponseWriter, err error) {
	log.Printf("database: %v", err)
	fail(w, 500, "INTERNAL_ERROR", "数据操作失败，请稍后重试")
}

func newHandler(store *Store, origins string) http.Handler {
	a := &api{store}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, map[string]string{"status": "ok"})
	})
	mux.HandleFunc("POST /api/issues", a.create)
	mux.HandleFunc("GET /api/issues", a.list)
	mux.HandleFunc("PATCH /api/issues/{id}/status", a.update)
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) { fail(w, 404, "NOT_FOUND", "接口不存在") })
	allowed := map[string]bool{}
	for _, origin := range strings.Split(origins, ",") {
		allowed[strings.TrimSpace(origin)] = true
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Vary", "Origin")
		origin := r.Header.Get("Origin")
		if origin != "" && allowed[origin] {
			w.Header().Set("Access-Control-Allow-Origin", origin)
			w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
			w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
		}
		if r.Method == http.MethodOptions {
			if !allowed[origin] {
				fail(w, 403, "FORBIDDEN", "浏览器来源未被允许")
				return
			}
			w.WriteHeader(http.StatusNoContent)
			return
		}
		mux.ServeHTTP(w, r)
	})
}
