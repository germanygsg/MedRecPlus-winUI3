param(
    [string]$OutputRoot = (Join-Path $env:TEMP ('medrec-update-checks-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$FullIdleWait,
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $PSScriptRoot
foreach ($projectName in @('MedRecPlus.App', 'MedRecPlus.Core')) {
    $sourceRoot = Join-Path $sourceDirectory $projectName
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relativePath = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        if ($relativePath -match '^(bin|obj)\\') { continue }
        $destination = Join-Path $OutputRoot ($projectName + '\' + $relativePath)
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
$startupPath = Join-Path $OutputRoot 'MedRecPlus.App\App.xaml.cs'
$startup = [IO.File]::ReadAllText($startupPath)
$startup = $startup.Replace('FindOrRegisterForKey("MedRecPlusOffline")', 'FindOrRegisterForKey("MedRecPlusUpdateChecks")')
$startup = $startup.Replace('shell.Start();', 'shell.RunUpdateChecks();')
[IO.File]::WriteAllText($startupPath, $startup)
$checks = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'UpdateChecks.cs'))
if ($FullIdleWait) { $checks = $checks.Replace('private static readonly bool FullIdleWait = false;', 'private static readonly bool FullIdleWait = true;') }
if ($Preview) { $checks = $checks.Replace('private static readonly bool Preview = false;', 'private static readonly bool Preview = true;') }
[IO.File]::WriteAllText((Join-Path $OutputRoot 'MedRecPlus.App\UpdateChecks.cs'), $checks)
dotnet build (Join-Path $OutputRoot 'MedRecPlus.App\MedRecPlus.App.csproj') -c Debug -p:Platform=x64 -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Update check build failed' }
$executable = Join-Path $OutputRoot 'MedRecPlus.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MedRecPlus.App.exe'
$process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
$resultPath = Join-Path ([IO.Path]::GetDirectoryName($executable)) 'update-check-results.txt'
Write-Output ('Update verification process: ' + $process.Id)
if ($Preview) { Write-Output ('Preview executable: ' + $executable); return }
$deadline = [DateTime]::UtcNow.AddSeconds($(if ($FullIdleWait) { 450 } else { 90 }))
while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2; $process.Refresh() }
if (!$process.HasExited) { throw ('Update checks did not finish. Inspect process ' + $process.Id) }
Get-Content -LiteralPath $resultPath
Write-Output ('Results: ' + $resultPath)
if ((Get-Content -LiteralPath $resultPath -Raw) -notmatch 'UPDATE_RESULT=PASS') { throw 'Update checks failed' }
