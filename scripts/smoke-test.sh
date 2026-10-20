#!/usr/bin/env bash
# Namines — Production Smoke Test Suite (FAZ 7)
set -eo pipefail

API_URL="${1:-http://localhost:5250}"
FRONTEND_URL="${2:-http://localhost:3005}"

echo -e "\n========================================================"
echo "  NAMINES PRODUCTION SMOKE TEST SUITE (FAZ 7)          "
echo -e "========================================================\n"

PASSED=0
FAILED=0

check() {
    local name="$1"
    local cmd="$2"
    local start
    local end
    local duration

    start=$(date +%s%N)
    if eval "$cmd" > /dev/null 2>&1; then
        end=$(date +%s%N)
        duration=$(( (end - start) / 1000000 ))
        printf "  \033[0;32m[PASS]\033[0m %-42s (%dms)\n" "$name" "$duration"
        PASSED=$((PASSED + 1))
    else
        end=$(date +%s%N)
        duration=$(( (end - start) / 1000000 ))
        printf "  \033[0;31m[FAIL]\033[0m %-42s (%dms)\n" "$name" "$duration"
        FAILED=$((FAILED + 1))
    fi
}

check "1. Backend /health & Control DB" "curl -sf --max-time 5 '$API_URL/health' | grep -q 'Healthy'"
check "2. Backend Readiness (/health/ready)" "curl -sf --max-time 5 '$API_URL/health/ready' | grep -q 'Healthy'"
check "3. W3C Traceparent (X-Trace-Id)" "curl -sI --max-time 5 '$API_URL/health/live' | grep -qi 'x-trace-id:'"
check "4. Prometheus Metrics (/metrics)" "curl -sf --max-time 5 '$API_URL/metrics' | grep -q 'process_runtime_dotnet'"
check "5. Frontend Landing (/)" "curl -sf --max-time 5 '$FRONTEND_URL/'"
check "6. Frontend Canvas (/canvas)" "curl -sf --max-time 5 '$FRONTEND_URL/canvas'"
check "7. Frontend Review (/review)" "curl -sf --max-time 5 '$FRONTEND_URL/review'"
check "8. PostHog Reverse Proxy (/ingest/decide)" "curl -s --max-time 5 -X POST '$FRONTEND_URL/ingest/decide' -H 'Content-Type: application/json' -d '{\"api_key\":\"smoke_check\"}'"

echo -e "\n--------------------------------------------------------"
if [ "$FAILED" -eq 0 ]; then
    echo -e "  \033[0;32mSONUÇ: TÜM TESTLER BAŞARILI! ($PASSED / $((PASSED + FAILED)))\033[0m"
else
    echo -e "  \033[0;31mSONUÇ: BAZI KONTROLLER BAŞARISIZ OLDU ($FAILED Hata)\033[0m"
fi
echo -e "--------------------------------------------------------\n"

exit "$FAILED"
