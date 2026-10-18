param(
    [string]$OutputRoot = (Join-Path $env:TEMP ('medrec-column-checks-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$PointerOnly
)
$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $PSScriptRoot
foreach ($projectName in @('MedRecPlus.App', 'MedRecPlus.Core')) {
    $sourceRoot = Join-Path $sourceDirectory $projectName
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relativePath = $file.FullName.Substring($sourceRoot.Length + 1)
        if ($relativePath -match '^(bin|obj)\\') { continue }
        $destination = Join-Path $OutputRoot ($projectName + '\' + $relativePath)
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
$startupPath = Join-Path $OutputRoot 'MedRecPlus.App\App.xaml.cs'
$startup = [IO.File]::ReadAllText($startupPath)
$startup = $startup.Replace('FindOrRegisterForKey("MedRecPlusOffline")', 'FindOrRegisterForKey("MedRecPlusColumnChecks")')
$checkSession = [Guid]::NewGuid().ToString('N')
$pointerArgument = if ($PointerOnly) { 'true' } else { 'false' }
$startup = $startup.Replace('shell.Start();', ('shell.RunColumnChecks("' + $checkSession + '", ' + $pointerArgument + ');'))
[IO.File]::WriteAllText($startupPath, $startup)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ColumnChecks.cs') -Destination (Join-Path $OutputRoot 'MedRecPlus.App\ColumnChecks.cs')
dotnet build (Join-Path $OutputRoot 'MedRecPlus.App\MedRecPlus.App.csproj') -c Debug -p:Platform=x64 -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Column check build failed' }
$executable = Join-Path $OutputRoot 'MedRecPlus.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MedRecPlus.App.exe'
$phases = if ($PointerOnly) { @('pointer') } else { @('initial', 'reopen') }
foreach ($phase in $phases) {
    $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { throw ('Column checks did not finish. Inspect process ' + $process.Id) }
    $resultPath = Join-Path ([IO.Path]::GetDirectoryName($executable)) ('column-check-results-' + $phase + '.txt')
    Get-Content -LiteralPath $resultPath
    if ((Get-Content -LiteralPath $resultPath -Raw) -notmatch 'COLUMN_UI_RESULT=PASS') { throw 'Column checks failed' }
}
Write-Output ('Results: ' + [IO.Path]::GetDirectoryName($executable))
