package main

import (
	"context"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"
)

var defaultDataDir = func() string {
	config, err := os.UserConfigDir()
	if err != nil {
		log.Fatal(err)
	}
	return filepath.Join(config, "ARInspection", "data")
}()

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func main() {
	path, err := databasePath()
	if err != nil {
		log.Fatal(err)
	}
	store, err := openStore(path)
	if err != nil {
		log.Fatal(err)
	}
	defer store.db.Close()
	srv := &http.Server{
		Addr:              envOr("ADDR", "0.0.0.0:8080"),
		Handler:           newHandler(store, envOr("ALLOWED_ORIGINS", "http://localhost:5173,http://127.0.0.1:5173")),
		ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second, WriteTimeout: 15 * time.Second, IdleTimeout: 60 * time.Second,
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		<-ctx.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := srv.Shutdown(shutdown); err != nil {
			log.Printf("shutdown: %v", err)
		}
	}()
	log.Printf("listen=%s database=%s", srv.Addr, path)
	if err := srv.ListenAndServe(); !errors.Is(err, http.ErrServerClosed) {
		log.Fatal(err)
	}
}
