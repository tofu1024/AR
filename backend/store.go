package main

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"time"

	_ "modernc.org/sqlite"
)

type Store struct{ db *sql.DB }

func openStore(path string) (*Store, error) {
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		return nil, err
	}
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}
	// One connection serializes the tiny prototype's database access.
	db.SetMaxOpenConns(1)
	_, err = db.Exec(`PRAGMA busy_timeout = 5000;
	CREATE TABLE IF NOT EXISTS issues (
		id TEXT PRIMARY KEY,
		title TEXT NOT NULL,
		description TEXT NOT NULL,
		priority TEXT NOT NULL CHECK(priority IN ('low','medium','high')),
		status TEXT NOT NULL CHECK(status IN ('open','in_progress','resolved')),
		position_x REAL NOT NULL, position_y REAL NOT NULL, position_z REAL NOT NULL,
		created_at TEXT NOT NULL, updated_at TEXT NOT NULL
	)`)
	if err != nil {
		db.Close()
		return nil, err
	}
	return &Store{db: db}, nil
}

const columns = `id, title, description, priority, status, position_x, position_y, position_z, created_at, updated_at`

type scanner interface{ Scan(...any) error }

func scanIssue(row scanner) (Issue, error) {
	var i Issue
	err := row.Scan(&i.ID, &i.Title, &i.Description, &i.Priority, &i.Status,
		&i.Position.X, &i.Position.Y, &i.Position.Z, &i.CreatedAt, &i.UpdatedAt)
	return i, err
}

func (s *Store) create(ctx context.Context, in CreateIssue) (Issue, error) {
	var bytes [16]byte
	if _, err := rand.Read(bytes[:]); err != nil {
		return Issue{}, err
	}
	now := time.Now().UTC().Format(time.RFC3339Nano)
	i := Issue{ID: hex.EncodeToString(bytes[:]), Title: in.Title, Description: in.Description,
		Priority: in.Priority, Status: "open", Position: Position{*in.Position.X, *in.Position.Y, *in.Position.Z},
		CreatedAt: now, UpdatedAt: now}
	_, err := s.db.ExecContext(ctx, `INSERT INTO issues (`+columns+`) VALUES (?,?,?,?,?,?,?,?,?,?)`,
		i.ID, i.Title, i.Description, i.Priority, i.Status, i.Position.X, i.Position.Y, i.Position.Z, i.CreatedAt, i.UpdatedAt)
	return i, err
}

func (s *Store) list(ctx context.Context) ([]Issue, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT `+columns+` FROM issues ORDER BY created_at DESC, id DESC`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	items := make([]Issue, 0)
	for rows.Next() {
		i, err := scanIssue(rows)
		if err != nil {
			return nil, err
		}
		items = append(items, i)
	}
	return items, rows.Err()
}

func (s *Store) updateStatus(ctx context.Context, id, status string) (Issue, error) {
	// RETURNING makes updating and reading the result one atomic statement.
	return scanIssue(s.db.QueryRowContext(ctx, `UPDATE issues SET status=?, updated_at=? WHERE id=? RETURNING `+columns,
		status, time.Now().UTC().Format(time.RFC3339Nano), id))
}

func databasePath() (string, error) {
	path := os.Getenv("DB_PATH")
	if path == "" {
		// User config directory stays stable for go run and compiled binaries.
		path = filepath.Join(defaultDataDir, "issues.db")
	}
	abs, err := filepath.Abs(path)
	if err != nil {
		return "", fmt.Errorf("数据库路径: %w", err)
	}
	return abs, nil
}
