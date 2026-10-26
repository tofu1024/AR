param(
    [string]$UnityPath = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = '',
    [string]$AdbPath = '',
    [string]$DeviceSerial = '',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityProject = if ($ProjectPath) { (Resolve-Path -LiteralPath $ProjectPath).Path } else { Join-Path $projectRoot 'unity' }
if ($unityProject -match '[^\x00-\x7F]') {
    throw 'Android requires an ASCII-only project path. Create an ASCII directory junction to the unity folder and pass its path with -ProjectPath. See README.'
}
$apkPath = Join-Path $unityProject 'Builds\inspection.apk'
$logDirectory = Join-Path $projectRoot '.tools\run'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

if (-not $UnityPath) {
    $unityCommand = Get-Command Unity -ErrorAction SilentlyContinue
    if ($unityCommand) { $UnityPath = $unityCommand.Source }
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw 'Unity Editor not found. Pass -UnityPath with the full path to Unity.exe. Install Android Build Support and activate the editor license first.'
}

# A timestamped log and a fresh output prevent a failed build from installing an old APK.
$buildLog = Join-Path $logDirectory ('unity-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$buildStarted = Get-Date
Write-Host "Building Android APK. Unity log: $buildLog"
$buildArguments = '-batchmode -quit -projectPath "' + $unityProject + '" -buildTarget Android -executeMethod InspectionProjectSetup.BuildAndroid -logFile "' + $buildLog + '"'
$buildProcess = Start-Process -FilePath $UnityPath -ArgumentList $buildArguments -WindowStyle Hidden -PassThru
# Wait for the editor itself; Unity's persistent licensing child must not block completion.
$buildProcess.WaitForExit()
$buildProcess.Refresh()
if ($buildProcess.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $buildLog) {
        Get-Content -LiteralPath $buildLog -Tail 60 | Where-Object { $_ -notmatch '(?i)\bId:|token|serial|password|authorization|email' }
    }
    throw "Unity build failed (exit $($buildProcess.ExitCode)). See $buildLog"
}
if (-not (Test-Path -LiteralPath $apkPath) -or (Get-Item -LiteralPath $apkPath).LastWriteTime -lt $buildStarted) {
    throw "Unity did not generate a fresh APK. See $buildLog"
}
Write-Host "APK ready: $apkPath"
if ($BuildOnly) { return }

if (-not $AdbPath) {
    $adbCommand = Get-Command adb -ErrorAction SilentlyContinue
    if ($adbCommand) { $AdbPath = $adbCommand.Source }
    else {
        $adbCandidates = @(
            (Join-Path $projectRoot '.tools\platform-tools\adb.exe'),
            (Join-Path (Split-Path -Parent $UnityPath) 'Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
        )
        if ($env:ANDROID_HOME) { $adbCandidates += Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe' }
        if ($env:ANDROID_SDK_ROOT) { $adbCandidates += Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe' }
        $AdbPath = $adbCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $AdbPath -or -not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) {
    throw 'ADB not found. Pass -AdbPath with the full path to adb.exe. The APK was built successfully.'
}

$deviceLines = @(& $AdbPath devices -l)
if ($LASTEXITCODE -ne 0) { throw 'ADB device discovery failed.' }
$readyDevices = @($deviceLines | Where-Object { $_ -match '^\S+\s+device\b' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($DeviceSerial) {
    if ($DeviceSerial -notin $readyDevices) { throw 'Selected device is unavailable or unauthorized. Unlock the phone and allow USB debugging.' }
} elseif ($readyDevices.Count -eq 1) {
    $DeviceSerial = $readyDevices[0]
} else {
    $deviceLines | Write-Host
    throw 'Connect one authorized Android phone, or pass -DeviceSerial when multiple devices are connected. APK is already built.'
}

$previousErrorPreference = $ErrorActionPreference
try {
    # Windows PowerShell 5 wraps native stderr in ErrorRecords; inspect the exit code ourselves.
    $ErrorActionPreference = 'Continue'
    $installOutput = @(& $AdbPath -s $DeviceSerial install -r $apkPath 2>&1)
    $installExit = $LASTEXITCODE
} finally { $ErrorActionPreference = $previousErrorPreference }
$installOutput | ForEach-Object { Write-Host $_ }
if ($installExit -ne 0) {
    if (($installOutput | Out-String) -match 'INSTALL_FAILED_USER_RESTRICTED') {
        & $AdbPath -s $DeviceSerial push $apkPath '/sdcard/Download/AR_Inspection.apk'
        throw 'Phone rejected ADB installation. APK is built; open Download/AR_Inspection.apk on the phone and complete installation manually.'
    }
    throw 'APK installation failed. Review the ADB output above.'
}
& $AdbPath -s $DeviceSerial shell am start -n com.inspection.prototype/com.unity3d.player.UnityPlayerActivity
if ($LASTEXITCODE -ne 0) { throw 'APK installed, but launch failed. Open AR Inspection on the phone manually.' }
Write-Host 'APK installed and launch requested. Allow camera permission on the phone, then set the computer LAN backend URL.'
