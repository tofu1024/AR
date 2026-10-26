$ErrorActionPreference = 'Stop'
Push-Location (Join-Path (Split-Path -Parent $PSScriptRoot) 'web')
try {
    if (-not (Test-Path -LiteralPath 'node_modules')) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    & npm.cmd run dev
}
finally { Pop-Location }
