#!/usr/bin/env python3
"""Audit H410M for WinUI3 build readiness (read-only).

Checks: Windows version/build, CPU/RAM, disk space, Visual Studio instances +
workloads (.NET desktop development, Windows application development),
dotnet SDKs, Windows SDKs, Developer Mode, git.
Runs a single PowerShell script via WinRemote shell_exec (one-shot, 64-bit).
Prints a JSON report to stdout. Never prints the WinRemote token.
"""
import base64
import json
import sys

sys.path.insert(0, "/home/hatch/workspace/winremote/client")
from winremote import WinRemote, WinRemoteError

PS = r"""
$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = [ordered]@{}

# --- OS ---
try {
  $nt = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
  $osv = [System.Environment]::OSVersion.Version
  $out['os'] = "$($nt.ProductName) | $($osv.Major).$($osv.Minor).$($nt.CurrentBuildNumber).$($nt.UBR)"
} catch { $out['os'] = 'unknown' }

# --- CPU / RAM ---
try {
  $out['cpu'] = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
  $out['ram_gb'] = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
} catch { $out['cpu'] = 'unknown' }

# --- Disks ---
try {
  $out['disks'] = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
    "$($_.DeviceID) $([math]::Round($_.FreeSpace/1GB,1))GB free of $([math]::Round($_.Size/1GB,1))GB"
  })
} catch { $out['disks'] = @() }

# --- Visual Studio ---
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$out['vswhere_found'] = Test-Path $vswhere
$instances = @()
if ($out['vswhere_found']) {
  $raw = & $vswhere -products * -format json 2>$null
  if ($raw) { $instances = @($raw | ConvertFrom-Json) }
}
$out['visual_studio'] = @($instances | ForEach-Object { "$($_.displayName) $($_.installationVersion)".Trim() })
$workloads = [ordered]@{
  'net_desktop_dev' = 'Microsoft.VisualStudio.Workload.ManagedDesktop'
  'win_app_dev'     = 'Microsoft.VisualStudio.Workload.Universal'
}
foreach ($k in $workloads.Keys) {
  $hit = $null
  if ($out['vswhere_found']) { $hit = & $vswhere -products * -requires $workloads[$k] -property installationPath 2>$null }
  $out['workload_' + $k] = ($null -ne $hit -and "$hit".Trim() -ne '')
}
# WinUI template presence (best-effort file check)
$tpl = @()
foreach ($i in $instances) {
  $p = Join-Path $i.installationPath 'Common7\IDE\ProjectTemplates'
  if (Test-Path $p) {
    $tpl += @(Get-ChildItem $p -Recurse -Include '*WinUI*' -ErrorAction SilentlyContinue |
      Select-Object -ExpandProperty Name)
  }
}
$out['winui_template_files'] = @($tpl | Select-Object -Unique)

# --- dotnet SDKs ---
$dotnet = $null
try { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source } catch {}
if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$out['dotnet_path'] = $dotnet
$out['dotnet_sdks'] = @()
if ($dotnet) { $out['dotnet_sdks'] = @(& $dotnet --list-sdks 2>$null) }

# --- Windows SDKs ---
$bin = 'C:\Program Files (x86)\Windows Kits\10\bin'
$out['windows_sdks'] = @()
if (Test-Path $bin) { $out['windows_sdks'] = @(Get-ChildItem $bin -Directory | Select-Object -ExpandProperty Name) }

# --- Developer Mode ---
$dev = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
  -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
$out['developer_mode'] = ($dev -eq 1)

# --- git ---
$out['git'] = $null
try { $out['git'] = (& git --version 2>$null) } catch {}

$out | ConvertTo-Json -Depth 4 -Compress
"""

def main():
    try:
        with open("/home/hatch/workspace/winremote/.token") as f:
            token = f.read().strip()
    except Exception:
        print(json.dumps({"ok": False, "error": "could not read winremote token"}))
        return 2
    try:
        agent = WinRemote("100.69.141.114", token=token, timeout=200)
        agent.health()
    except Exception as e:
        print(json.dumps({"ok": False, "error": f"winremote unreachable: {e}"}))
        return 3
    # NOTE: WinRemote shell_exec already runs inside PowerShell 5.1 — pass the
    # script directly. Do NOT wrap in `powershell -EncodedCommand` (powershell.exe
    # is not on PATH inside the agent's shell).
    try:
        r = agent.shell_exec(PS, timeout=180, bitness="64")
    except Exception as e:
        print(json.dumps({"ok": False, "error": f"shell_exec failed: {e}"}))
        return 1
    stdout = (r.get("stdout") or "").strip()
    try:
        report = json.loads(stdout)
        report["ok"] = True
        report["exit_code"] = r.get("exit_code")
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 0
    except Exception:
        print(json.dumps({"ok": False, "error": "unparseable output",
                          "exit_code": r.get("exit_code"),
                          "stdout_tail": stdout[-2000:]}))
        return 1

if __name__ == "__main__":
    sys.exit(main())
