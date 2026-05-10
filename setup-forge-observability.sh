#!/usr/bin/env bash
# setup-forge-observability.sh
#
# Clones denmanjohn-maker/forge-observability (must already exist and be empty)
# and populates it with the full Railway-ready observability stack, then pushes.
#
# Usage:
#   chmod +x setup-forge-observability.sh
#   ./setup-forge-observability.sh
#
# Prerequisites: git, authenticated GitHub access (token, SSH, or gh CLI)

set -euo pipefail

REPO_URL="https://github.com/denmanjohn-maker/forge-observability.git"
REPO_DIR="forge-observability"

echo "Cloning $REPO_URL ..."
git clone "$REPO_URL" "$REPO_DIR"
cd "$REPO_DIR"

mkdir -p \
  alloy \
  tempo \
  loki \
  prometheus \
  grafana/provisioning/datasources

# ── docker-compose.yml ────────────────────────────────────────────────────────
cat > docker-compose.yml << 'COMPOSE'
version: '3.8'

# Observability stack for mtg-forge — Railway.app deployment
#
# Each service builds a self-contained image (configs baked in via Dockerfile)
# so Railway's no-bind-mount constraint is satisfied.
#
# Internal service communication uses Docker Compose service names locally and
# Railway internal DNS (<service>.railway.internal) when deployed.
#
# Required env vars (set in Railway dashboard or .env locally):
#   GRAFANA_ADMIN_PASSWORD  — Grafana admin password
#   ALLOY_SCRAPE_TARGET     — host:port of the mtg-forge API /metrics endpoint
#                             Local:   mtg-api:5000
#                             Railway: mtg-api.railway.internal:5000
#
# mtg-forge API env vars to configure (set on the API Railway service):
#   OTEL_EXPORTER_OTLP_ENDPOINT=http://alloy.railway.internal:4317
#   LOKI_URI=http://loki.railway.internal:3100

services:

  # ─── Grafana Alloy (telemetry pipeline) ─────────────────────────────────────
  # Receives OTLP from the mtg-forge API and fans out to Tempo / Prometheus / Loki.
  # gRPC :4317  |  HTTP :4318  |  Debug UI :12345
  alloy:
    build:
      context: ./alloy
      dockerfile: Dockerfile
    container_name: forge-alloy
    restart: unless-stopped
    environment:
      # Override in Railway to point at the API's Railway internal hostname.
      ALLOY_SCRAPE_TARGET: "${ALLOY_SCRAPE_TARGET:-mtg-api:5000}"
    ports:
      - "4317:4317"   # OTLP gRPC
      - "4318:4318"   # OTLP HTTP
      - "12345:12345" # Alloy debug UI
    volumes:
      - alloy_data:/var/lib/alloy
    depends_on:
      - tempo
      - prometheus
      - loki
    healthcheck:
      test: ["CMD-SHELL", "wget -q --spider http://localhost:12345/ready || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 15s

  # ─── Grafana Tempo (distributed tracing) ────────────────────────────────────
  tempo:
    build:
      context: ./tempo
      dockerfile: Dockerfile
    container_name: forge-tempo
    restart: unless-stopped
    ports:
      - "3200:3200"   # Tempo HTTP API (Grafana queries here)
    volumes:
      - tempo_data:/var/tempo
    healthcheck:
      test: ["CMD-SHELL", "wget -q --spider http://localhost:3200/ready || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 15s

  # ─── Grafana Loki (log aggregation) ─────────────────────────────────────────
  loki:
    build:
      context: ./loki
      dockerfile: Dockerfile
    container_name: forge-loki
    restart: unless-stopped
    volumes:
      - loki_data:/loki
    healthcheck:
      test: ["CMD-SHELL", "wget -q --spider http://localhost:3100/ready || exit 1"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  # ─── Prometheus (metrics storage) ───────────────────────────────────────────
  prometheus:
    build:
      context: ./prometheus
      dockerfile: Dockerfile
    container_name: forge-prometheus
    restart: unless-stopped
    ports:
      - "9090:9090"
    volumes:
      - prometheus_data:/prometheus

  # ─── Grafana (dashboards) ────────────────────────────────────────────────────
  grafana:
    build:
      context: ./grafana
      dockerfile: Dockerfile
    container_name: forge-grafana
    restart: unless-stopped
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: "${GRAFANA_ADMIN_PASSWORD:-admin}"
      GF_USERS_ALLOW_SIGN_UP: "false"
      GF_FEATURE_TOGGLES_ENABLE: "traceqlEditor traceToMetrics"
      # Public URL — tells Grafana its canonical domain for share links and OAuth callbacks.
      # The custom domain itself is wired up in the Railway service's Networking settings.
      GF_SERVER_DOMAIN: "grafana.jdtechprojects.net"
      GF_SERVER_ROOT_URL: "https://grafana.jdtechprojects.net"
    ports:
      - "3000:3000"
    volumes:
      - grafana_data:/var/lib/grafana
    depends_on:
      - prometheus
      - loki
      - tempo

volumes:
  alloy_data:
    driver: local
  tempo_data:
    driver: local
  loki_data:
    driver: local
  prometheus_data:
    driver: local
  grafana_data:
    driver: local
COMPOSE

# ── alloy/Dockerfile ──────────────────────────────────────────────────────────
cat > alloy/Dockerfile << 'DOCKERFILE'
FROM grafana/alloy:v1.2.0
COPY config.alloy /etc/alloy/config.alloy
EXPOSE 4317 4318 12345
CMD ["run", "--server.http.listen-addr=0.0.0.0:12345", "/etc/alloy/config.alloy"]
DOCKERFILE

# ── alloy/config.alloy ────────────────────────────────────────────────────────
cat > alloy/config.alloy << 'ALLOY'
// ── OTLP receiver ─────────────────────────────────────────────────────────────
// Accepts traces, metrics, and logs from the mtg-forge API via OTLP gRPC/HTTP.
// Railway internal DNS: alloy.railway.internal:4317 (gRPC) / :4318 (HTTP)
otelcol.receiver.otlp "default" {
  grpc { endpoint = "0.0.0.0:4317" }
  http { endpoint = "0.0.0.0:4318" }

  output {
    traces  = [otelcol.processor.batch.default.input]
    metrics = [otelcol.processor.batch.default.input]
    logs    = [otelcol.processor.batch.default.input]
  }
}

// ── Batch processor ───────────────────────────────────────────────────────────
otelcol.processor.batch "default" {
  timeout         = "1s"
  send_batch_size = 1024

  output {
    traces  = [otelcol.exporter.otlp.tempo.input]
    metrics = [otelcol.exporter.prometheus.default.input]
    logs    = [otelcol.exporter.loki.default.input]
  }
}

// ── Traces → Tempo ────────────────────────────────────────────────────────────
otelcol.exporter.otlp "tempo" {
  client {
    endpoint = "tempo:4317"
    tls { insecure = true }
  }
}

// ── Metrics → Prometheus (via remote_write) ───────────────────────────────────
otelcol.exporter.prometheus "default" {
  forward_to = [prometheus.remote_write.default.receiver]
}

prometheus.remote_write "default" {
  endpoint {
    url = "http://prometheus:9090/api/v1/write"
  }
}

// ── Metrics: scrape the mtg-forge API's Prometheus /metrics endpoint ──────────
// Set ALLOY_SCRAPE_TARGET to override the default (e.g. mtg-api.railway.internal:5000 on Railway).
prometheus.scrape "mtg_api" {
  targets = [{
    "__address__" = env("ALLOY_SCRAPE_TARGET"),
    "job"         = "mtg-deckforge",
    "app"         = "mtg-forge",
  }]
  scrape_interval = "15s"
  metrics_path    = "/metrics"

  forward_to = [prometheus.remote_write.default.receiver]
}

// ── Logs → Loki ───────────────────────────────────────────────────────────────
otelcol.exporter.loki "default" {
  forward_to = [loki.write.default.receiver]
}

loki.write "default" {
  endpoint {
    url = "http://loki:3100/loki/api/v1/push"
  }
}
ALLOY

# ── tempo/Dockerfile ──────────────────────────────────────────────────────────
cat > tempo/Dockerfile << 'DOCKERFILE'
FROM grafana/tempo:2.5.0
COPY tempo.yaml /etc/tempo.yaml
EXPOSE 3200 4317 4318
CMD ["-config.file=/etc/tempo.yaml"]
DOCKERFILE

# ── tempo/tempo.yaml ──────────────────────────────────────────────────────────
cat > tempo/tempo.yaml << 'TEMPO'
stream_over_http_enabled: true

server:
  http_listen_port: 3200

# Accept traces over OTLP gRPC and HTTP
distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          endpoint: 0.0.0.0:4317
        http:
          endpoint: 0.0.0.0:4318

# Ingester flushes completed traces to the store
ingester:
  max_block_duration: 5m

# Local filesystem backend — swap for s3/gcs in production
storage:
  trace:
    backend: local
    local:
      path: /var/tempo/traces
    wal:
      path: /var/tempo/wal

# Keep traces for 72 hours
compactor:
  compaction:
    block_retention: 72h

# Enable TraceQL search and tag autocomplete
query_frontend:
  search:
    duration_slo: 5s
    throughput_bytes_slo: 1.073741824e+09

# Link traces → Loki logs via the service.name and trace_id
overrides:
  defaults:
    metrics_generator:
      processors: []
TEMPO

# ── loki/Dockerfile ───────────────────────────────────────────────────────────
cat > loki/Dockerfile << 'DOCKERFILE'
FROM grafana/loki:3.0.0
COPY loki-config.yaml /etc/loki/local-config.yaml
EXPOSE 3100 9096
CMD ["-config.file=/etc/loki/local-config.yaml"]
DOCKERFILE

# ── loki/loki-config.yaml ─────────────────────────────────────────────────────
cat > loki/loki-config.yaml << 'LOKI'
auth_enabled: false

server:
  http_listen_port: 3100
  grpc_listen_port: 9096

common:
  instance_addr: 127.0.0.1
  path_prefix: /loki
  storage:
    filesystem:
      chunks_directory: /loki/chunks
      rules_directory: /loki/rules
  replication_factor: 1
  ring:
    kvstore:
      store: inmemory

schema_config:
  configs:
    - from: 2020-10-24
      store: tsdb
      object_store: filesystem
      schema: v13
      index:
        prefix: index_
        period: 24h

limits_config:
  allow_structured_metadata: true

analytics:
  reporting_enabled: false
LOKI

# ── prometheus/Dockerfile ─────────────────────────────────────────────────────
cat > prometheus/Dockerfile << 'DOCKERFILE'
FROM prom/prometheus:v2.53.0
COPY prometheus.yml /etc/prometheus/prometheus.yml
EXPOSE 9090
CMD [ \
  "--config.file=/etc/prometheus/prometheus.yml", \
  "--storage.tsdb.retention.time=30d", \
  "--web.enable-remote-write-receiver" \
]
DOCKERFILE

# ── prometheus/prometheus.yml ─────────────────────────────────────────────────
cat > prometheus/prometheus.yml << 'PROMETHEUS'
global:
  scrape_interval: 15s
  evaluation_interval: 15s
# Metrics arrive via Alloy remote_write — no static scrape targets needed here.
PROMETHEUS

# ── grafana/Dockerfile ────────────────────────────────────────────────────────
cat > grafana/Dockerfile << 'DOCKERFILE'
FROM grafana/grafana:11.1.0
COPY provisioning /etc/grafana/provisioning
EXPOSE 3000
DOCKERFILE

# ── grafana/provisioning/datasources/loki.yml ─────────────────────────────────
cat > grafana/provisioning/datasources/loki.yml << 'LOKI_DS'
apiVersion: 1

datasources:
  - name: Loki
    uid: loki
    type: loki
    access: proxy
    url: http://loki:3100
    isDefault: false
    editable: true
    jsonData:
      # Derive trace IDs from log lines so Grafana can link logs → Tempo traces
      derivedFields:
        - datasourceUid: tempo
          matcherRegex: '"TraceId"\s*:\s*"([a-f0-9]{32})"'
          name: TraceID
          url: "$${__value.raw}"
LOKI_DS

# ── grafana/provisioning/datasources/prometheus.yml ──────────────────────────
cat > grafana/provisioning/datasources/prometheus.yml << 'PROM_DS'
apiVersion: 1

datasources:
  - name: Prometheus
    uid: prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    editable: true
PROM_DS

# ── grafana/provisioning/datasources/tempo.yml ───────────────────────────────
cat > grafana/provisioning/datasources/tempo.yml << 'TEMPO_DS'
apiVersion: 1

datasources:
  - name: Tempo
    type: tempo
    access: proxy
    url: http://tempo:3200
    isDefault: false
    editable: true
    jsonData:
      httpMethod: GET
      tracesToLogsV2:
        # Link trace spans → Loki log lines using the shared trace ID
        datasourceUid: loki
        spanStartTimeShift: "-1m"
        spanEndTimeShift: "1m"
        filterByTraceID: true
        filterBySpanID: false
        customQuery: false
      tracesToMetrics:
        datasourceUid: prometheus
        spanStartTimeShift: "-1m"
        spanEndTimeShift: "1m"
        tags:
          - key: service.name
            value: service_name
      serviceMap:
        datasourceUid: prometheus
      nodeGraph:
        enabled: true
      search:
        hide: false
      lokiSearch:
        datasourceUid: loki
TEMPO_DS

# ── Commit and push ───────────────────────────────────────────────────────────
echo ""
echo "All files written. Committing..."

git add -A
git commit -m "Initial observability stack: Alloy, Tempo, Loki, Prometheus, Grafana

Standalone Railway.app-ready observability stack for mtg-forge.
Each service uses a custom Dockerfile to bake configs in (no bind mounts).
Grafana Alloy receives OTLP from the API and fans out to all backends.
Grafana configured for public domain grafana.jdtechprojects.net."

git push -u origin main

echo ""
echo "Done! forge-observability is live at:"
echo "  https://github.com/denmanjohn-maker/forge-observability"
echo ""
echo "Next steps:"
echo "  1. In Railway, deploy this repo as a new project (Docker Compose)"
echo "  2. Set env vars on each service:"
echo "       alloy:   ALLOY_SCRAPE_TARGET=mtg-api.railway.internal:5000"
echo "       grafana: GRAFANA_ADMIN_PASSWORD=<your-password>"
echo "  3. Set env vars on the mtg-forge API Railway service:"
echo "       OTEL_EXPORTER_OTLP_ENDPOINT=http://alloy.railway.internal:4317"
echo "       LOKI_URI=http://loki.railway.internal:3100"
echo "  4. In Railway → Grafana service → Networking, add custom domain:"
echo "       grafana.jdtechprojects.net"
