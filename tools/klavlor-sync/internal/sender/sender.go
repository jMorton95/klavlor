package sender

import (
	"bytes"
	"context"
	"crypto/tls"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"sync"
	"time"

	"golang.org/x/time/rate"

	"github.com/klavlor/klavlor-sync/internal/model"
)

type Sender struct {
	apiURL    string
	apiKey    string
	batchSize int
	flushInt  time.Duration
	client    *http.Client
	limiter   *rate.Limiter

	mu      sync.Mutex
	pending []model.LootRecord

	// onSent is called after a successful batch send with the number of records sent.
	onSent func(n int)
}

func New(apiURL, apiKey string, batchSize int, flushInterval time.Duration, insecure bool) *Sender {
	client := &http.Client{Timeout: 30 * time.Second}

	// TLS verification is only ever skipped for localhost/loopback targets, so
	// the option cannot weaken security against a real server. Against any
	// non-loopback host it is refused and verification stays on.
	if insecure {
		if isLoopback(apiURL) {
			client.Transport = &http.Transport{TLSClientConfig: &tls.Config{InsecureSkipVerify: true}}
			slog.Warn("TLS verification disabled for local target (--insecure)", "api_url", apiURL)
		} else {
			slog.Warn("ignoring --insecure: only honored for localhost/loopback targets, not real servers", "api_url", apiURL)
		}
	}

	return &Sender{
		apiURL:    apiURL,
		apiKey:    apiKey,
		batchSize: batchSize,
		flushInt:  flushInterval,
		client:    client,
		limiter:   rate.NewLimiter(rate.Every(1*time.Second), 1), // 1 req/s
	}
}

// isLoopback reports whether the URL's host is a loopback address (localhost,
// 127.0.0.0/8, or ::1). Used to gate the --insecure TLS skip to local dev only.
func isLoopback(rawURL string) bool {
	u, err := url.Parse(rawURL)
	if err != nil {
		return false
	}
	host := u.Hostname()
	if host == "localhost" {
		return true
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

// OnSent registers a callback fired after each successful batch.
func (s *Sender) OnSent(fn func(n int)) {
	s.onSent = fn
}

// Enqueue adds records to the pending buffer. If the buffer reaches batchSize,
// records are sent immediately.
func (s *Sender) Enqueue(ctx context.Context, records []model.LootRecord) {
	s.mu.Lock()
	s.pending = append(s.pending, records...)
	shouldFlush := len(s.pending) >= s.batchSize
	s.mu.Unlock()

	if shouldFlush {
		s.Flush(ctx)
	}
}

// FlushLoop runs a periodic flush in a goroutine until the context is cancelled.
func (s *Sender) FlushLoop(ctx context.Context) {
	ticker := time.NewTicker(s.flushInt)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			s.Flush(ctx)
		}
	}
}

// Flush sends all pending records in batches.
func (s *Sender) Flush(ctx context.Context) {
	for {
		if ctx.Err() != nil {
			return
		}
		batch := s.takeBatch()
		if len(batch) == 0 {
			return
		}
		if err := s.sendBatch(ctx, batch); err != nil {
			slog.Error("batch send failed, re-queuing", "count", len(batch), "error", err)
			s.mu.Lock()
			s.pending = append(batch, s.pending...) // put back at front
			s.mu.Unlock()
			return
		}
		slog.Info("batch sent", "count", len(batch))
		if s.onSent != nil {
			s.onSent(len(batch))
		}
	}
}

// Shutdown flushes all remaining records.
func (s *Sender) Shutdown() {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	s.Flush(ctx)
}

func (s *Sender) takeBatch() []model.LootRecord {
	s.mu.Lock()
	defer s.mu.Unlock()

	if len(s.pending) == 0 {
		return nil
	}

	n := s.batchSize
	if n > len(s.pending) {
		n = len(s.pending)
	}
	batch := make([]model.LootRecord, n)
	copy(batch, s.pending[:n])
	s.pending = s.pending[n:]
	return batch
}

func (s *Sender) sendBatch(ctx context.Context, records []model.LootRecord) error {
	// Rate limit.
	if err := s.limiter.Wait(ctx); err != nil {
		return err
	}

	body, err := json.Marshal(records)
	if err != nil {
		return fmt.Errorf("marshal batch: %w", err)
	}

	url := s.apiURL + "/api/loot/ingest/batch"

	var lastErr error
	backoff := 1 * time.Second

	for attempt := 0; attempt < 3; attempt++ {
		if attempt > 0 {
			slog.Debug("retrying batch send", "attempt", attempt+1, "backoff", backoff)
			select {
			case <-ctx.Done():
				return ctx.Err()
			case <-time.After(backoff):
			}
			backoff *= 4
		}

		req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(body))
		if err != nil {
			return fmt.Errorf("create request: %w", err)
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", "Bearer "+s.apiKey)
		req.Header.Set("X-Sync-Version", "2")

		resp, err := s.client.Do(req)
		if err != nil {
			lastErr = fmt.Errorf("http request: %w", err)
			continue // network error, retry
		}

		respBody, _ := io.ReadAll(resp.Body)
		resp.Body.Close()

		switch {
		case resp.StatusCode == http.StatusCreated:
			return nil // success
		case resp.StatusCode == http.StatusTooManyRequests:
			wait := 60 * time.Second
			if ra := resp.Header.Get("Retry-After"); ra != "" {
				if secs, err := strconv.Atoi(ra); err == nil {
					wait = time.Duration(secs) * time.Second
				}
			}
			slog.Warn("rate limited, pausing", "retry_after", wait)
			select {
			case <-ctx.Done():
				return ctx.Err()
			case <-time.After(wait):
			}
			lastErr = fmt.Errorf("rate limited (429)")
			continue
		case resp.StatusCode == http.StatusUpgradeRequired:
			// Client version too old — fatal, cannot continue.
			slog.Error("sync client is outdated, server requires a newer version — please update klavlor-sync",
				"response", string(respBody))
			return fmt.Errorf("client outdated: server requires newer sync version (426)")
		case resp.StatusCode == http.StatusBadRequest:
			// Validation error — retrying won't help, skip this batch.
			slog.Error("batch rejected by server (400), dropping",
				"count", len(records), "response", string(respBody))
			return nil
		case resp.StatusCode >= 500:
			lastErr = fmt.Errorf("server error %d: %s", resp.StatusCode, string(respBody))
			continue // retry
		default:
			return fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(respBody))
		}
	}

	return fmt.Errorf("all retries exhausted: %w", lastErr)
}

// PendingCount returns the number of unsent records.
func (s *Sender) PendingCount() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.pending)
}
