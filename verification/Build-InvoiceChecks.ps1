param([string]$OutputRoot = (Join-Path $env:TEMP ('medrec-invoice-checks-' + [Guid]::NewGuid().ToString('N'))))
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
$startup = $startup.Replace('FindOrRegisterForKey("MedRecPlusOffline")', 'FindOrRegisterForKey("MedRecPlusInvoiceChecks")')
$startup = $startup.Replace('shell.Start();', 'shell.RunInvoiceChecks();')
[IO.File]::WriteAllText($startupPath, $startup)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InvoiceChecks.cs') -Destination (Join-Path $OutputRoot 'MedRecPlus.App\InvoiceChecks.cs')
dotnet build (Join-Path $OutputRoot 'MedRecPlus.App\MedRecPlus.App.csproj') -c Debug -p:Platform=x64 -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Invoice check build failed' }
$executable = Join-Path $OutputRoot 'MedRecPlus.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MedRecPlus.App.exe'
$process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(45000)) { throw ('Invoice checks did not finish. Inspect process ' + $process.Id) }
$resultPath = Join-Path ([IO.Path]::GetDirectoryName($executable)) 'invoice-check-results.txt'
Get-Content -LiteralPath $resultPath
Write-Output ('Results: ' + $resultPath)
if ((Get-Content -LiteralPath $resultPath -Raw) -notmatch 'INVOICE_RESULT=PASS') { throw 'Invoice checks failed' }
