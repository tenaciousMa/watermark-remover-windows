# Build

Build the WPF application with the .NET 8 SDK:

```powershell
dotnet publish .\src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\staging\app
```

Build the OpenCV worker with
`packaging\scripts\build-opencv-worker.ps1`. Set up the optional heavy engine
with `engines\heavy\bootstrap.ps1`.
