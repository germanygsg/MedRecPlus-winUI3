# MedRecPlus Offline (WinUI 3)

Native Windows 11 clinic management app — fully offline. See `../PRD.md` for the
full product requirements.

## Prerequisites (H410M)

- Visual Studio 2026 Community with workloads:
  - **.NET desktop development**
  - **WinUI application development**
- .NET 10 SDK, Windows SDK 10.0.26100+, Developer Mode ON
- Internet access for the first NuGet restore only

## Build

```powershell
# from the src/ directory
dotnet build MedRecPlus.sln -c Debug -p:Platform=x64
```

Or open `MedRecPlus.sln` in Visual Studio and press F5.

> WinUI 3 requires an architecture-specific platform (`x64`/`arm64`) —
> `AnyCPU` is not supported.

## Projects

- `MedRecPlus.Core` — net10.0 class library: entities, encrypted SQLite data
  layer (SQLCipher), domain services (numbering, money math, settings, audit,
  password hashing). No UI dependencies.
- `MedRecPlus.App` — WinUI 3 packaged app: NavigationView shell, pages,
  view-models (CommunityToolkit.Mvvm).

## Packaging

Release builds produce a self-signed MSIX (see PRD §11). The packaging identity
(`BSPCenterClinic.MedRecPlusOffline`, `CN=BSP Center Clinic`) must match the
signing certificate installed on the clinic PC.
