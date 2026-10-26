$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localGo = Join-Path $projectRoot '.tools\go\bin\go.exe'
$goCommand = Get-Command go -ErrorAction SilentlyContinue
if ($goCommand) { $goExecutable = $goCommand.Source }
elseif (Test-Path -LiteralPath $localGo) { $goExecutable = $localGo }
else { throw 'Install Go 1.24 or later and add it to PATH.' }

if (-not $env:DB_PATH) { $env:DB_PATH = Join-Path $projectRoot 'backend\data\issues.db' }
Push-Location (Join-Path $projectRoot 'backend')
try { & $goExecutable run . }
finally { Pop-Location }
