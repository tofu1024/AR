package main

import (
	"fmt"
	"math"
	"strings"
)

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Status      string   `json:"status"`
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}

// Pointers distinguish omitted/null coordinates from valid zero coordinates.
type CreateIssue struct {
	Title       string `json:"title"`
	Description string `json:"description"`
	Priority    string `json:"priority"`
	Position    *struct {
		X *float64 `json:"x"`
		Y *float64 `json:"y"`
		Z *float64 `json:"z"`
	} `json:"position"`
}

func validPriority(v string) bool { return v == "low" || v == "medium" || v == "high" }
func validStatus(v string) bool   { return v == "open" || v == "in_progress" || v == "resolved" }

func (in *CreateIssue) validate() error {
	in.Title = strings.TrimSpace(in.Title)
	if in.Title == "" {
		return fmt.Errorf("标题不能为空")
	}
	if len([]rune(in.Title)) > 200 {
		return fmt.Errorf("标题不能超过 200 个字符")
	}
	if len([]rune(in.Description)) > 5000 {
		return fmt.Errorf("描述不能超过 5000 个字符")
	}
	if !validPriority(in.Priority) {
		return fmt.Errorf("priority 必须为 low、medium 或 high")
	}
	if in.Position == nil || in.Position.X == nil || in.Position.Y == nil || in.Position.Z == nil {
		return fmt.Errorf("position 必须包含数值 x、y、z")
	}
	for _, v := range []float64{*in.Position.X, *in.Position.Y, *in.Position.Z} {
		if math.IsNaN(v) || math.IsInf(v, 0) {
			return fmt.Errorf("坐标必须为有限数值")
		}
	}
	return nil
}
