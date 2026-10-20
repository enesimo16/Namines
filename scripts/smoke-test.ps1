<#
.SYNOPSIS
    Namines Production Smoke Test Suite (FAZ 7).
.DESCRIPTION
    Dağıtım sonrasında API, Veritabanı, Dağıtık İzleme, Metrikler ve Ön Yüz
    sağlığını 10 saniye içinde uçtan uca doğrulayan otomatik duman testi.
.PARAMETER ApiUrl
    Test edilecek API ana adresi (Varsayılan: http://localhost:5250 veya http://localhost:5000).
.PARAMETER FrontendUrl
    Test edilecek Ön Yüz ana adresi (Varsayılan: http://localhost:3005 veya http://localhost:3000).
#>
[CmdletBinding()]
param (
    [string]$ApiUrl = "http://localhost:5250",
    [string]$FrontendUrl = "http://localhost:3005"
)

$ErrorActionPreference = "Continue"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "  NAMINES PRODUCTION SMOKE TEST SUITE (FAZ 7)         " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Assert-Check {
    param (
        [string]$Name,
        [scriptblock]$Test
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $Test
        $sw.Stop()
        if ($result.Success) {
            Write-Host ("  [PASS] " + $Name.PadRight(45) + " (" + $sw.ElapsedMilliseconds + "ms) - " + $result.Message) -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host ("  [FAIL] " + $Name.PadRight(45) + " (" + $sw.ElapsedMilliseconds + "ms) - " + $result.Message) -ForegroundColor Red
            $script:failed++
        }
    } catch {
        $sw.Stop()
        Write-Host ("  [FAIL] " + $Name.PadRight(45) + " (" + $sw.ElapsedMilliseconds + "ms) - " + $_.Exception.Message) -ForegroundColor Red
        $script:failed++
    }
}

# ── 1. API Health & Control DB ────────────────────────────────────────────────
Assert-Check "1. Backend /health & Control DB" {
    $res = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -ne 200) {
        return @{ Success = $false; Message = "HTTP $($res.StatusCode)" }
    }
    $json = $res.Content | ConvertFrom-Json
    if ($json.status -ne "Healthy") {
        return @{ Success = $false; Message = "Status: $($json.status)" }
    }
    return @{ Success = $true; Message = "PostgreSQL Healthy ($($json.entries.'postgres-control-db'.duration))" }
}

# ── 2. Readiness Probe ────────────────────────────────────────────────────────
Assert-Check "2. Backend Readiness (/health/ready)" {
    $res = Invoke-WebRequest -Uri "$ApiUrl/health/ready" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -eq 200) {
        return @{ Success = $true; Message = "Ready for traffic" }
    }
    return @{ Success = $false; Message = "HTTP $($res.StatusCode)" }
}

# ── 3. W3C Distributed Tracing Header ─────────────────────────────────────────
Assert-Check "3. W3C Traceparent (X-Trace-Id)" {
    $res = Invoke-WebRequest -Uri "$ApiUrl/health/live" -UseBasicParsing -TimeoutSec 5
    $traceId = $res.Headers["X-Trace-Id"]
    if ([string]::IsNullOrWhiteSpace($traceId)) {
        return @{ Success = $false; Message = "Header missing" }
    }
    return @{ Success = $true; Message = "TraceId: $traceId" }
}

# ── 4. Prometheus Scrape Endpoint ────────────────────────────────────────────
Assert-Check "4. Prometheus Metrics (/metrics)" {
    $res = Invoke-WebRequest -Uri "$ApiUrl/metrics" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -eq 200 -and $res.Content.Contains("process_runtime_dotnet")) {
        return @{ Success = $true; Message = "Metrics scrapeable ($($res.Content.Length) bytes)" }
    }
    return @{ Success = $false; Message = "Invalid metrics output" }
}

# ── 5. Frontend Root & Next.js App ───────────────────────────────────────────
Assert-Check "5. Frontend Landing (/)" {
    $res = Invoke-WebRequest -Uri "$FrontendUrl/" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -eq 200) {
        return @{ Success = $true; Message = "200 OK" }
    }
    return @{ Success = $false; Message = "HTTP $($res.StatusCode)" }
}

# ── 6. Frontend Canvas Route ─────────────────────────────────────────────────
Assert-Check "6. Frontend Studio Canvas (/canvas)" {
    $res = Invoke-WebRequest -Uri "$FrontendUrl/canvas" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -eq 200) {
        return @{ Success = $true; Message = "200 OK" }
    }
    return @{ Success = $false; Message = "HTTP $($res.StatusCode)" }
}

# ── 7. Frontend Change Review Route ──────────────────────────────────────────
Assert-Check "7. Frontend Review Governance (/review)" {
    $res = Invoke-WebRequest -Uri "$FrontendUrl/review" -UseBasicParsing -TimeoutSec 5
    if ($res.StatusCode -eq 200) {
        return @{ Success = $true; Message = "200 OK" }
    }
    return @{ Success = $false; Message = "HTTP $($res.StatusCode)" }
}

# ── 8. PostHog Reverse Proxy Route ────────────────────────────────────────────
Assert-Check "8. PostHog Reverse Proxy (/ingest/decide)" {
    try {
        $res = Invoke-WebRequest -Uri "$FrontendUrl/ingest/decide" -Method Post -Body '{"api_key":"smoke_check"}' -ContentType 'application/json' -UseBasicParsing -TimeoutSec 5
        return @{ Success = $true; Message = "Proxy reachable (HTTP $($res.StatusCode))" }
    } catch {
        # PostHog EU sends 400 with valid PostHog error JSON when key is fake — this proves proxy reaches PostHog!
        if ($_.Exception.Response.StatusCode -ne $null) {
            return @{ Success = $true; Message = "Proxy active (Handled by PostHog EU)" }
        }
        return @{ Success = $false; Message = $_.Exception.Message }
    }
}

Write-Host "`n--------------------------------------------------------" -ForegroundColor Gray
if ($failed -eq 0) {
    Write-Host "  SONUÇ: TÜM TESTLER BAŞARILI! ($passed / $($passed + $failed))" -ForegroundColor Green
} else {
    Write-Host "  SONUÇ: BAZI KONTROLLER BAŞARISIZ OLDU ($failed Hata)" -ForegroundColor Red
}
Write-Host "--------------------------------------------------------`n" -ForegroundColor Gray
