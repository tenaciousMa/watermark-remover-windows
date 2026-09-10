# AI Inpainting Engine

`ai_runner.py` is the lightweight OpenCV repair worker used by the
`ai` processing mode.

It supports:

- frame-wise rectangular masks
- dynamic watermark keyframes
- automatic text/Logo detection
- ROI-only Telea/NS inpainting
- multithreaded frame repair
- FFmpeg audio muxing

Build the standalone Windows worker from the repository root:

```powershell
.\packaging\scripts\build-opencv-worker.ps1 `
  -PythonExecutable C:\Path\To\python.exe `
  -OutputDirectory .\release\staging\ai
```

Do not commit the resulting `ai_runner.exe`; it is a release artifact.
