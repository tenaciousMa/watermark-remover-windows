# ProPainter Heavy Engine

The heavy engine uses the upstream ProPainter project for video inpainting.
The upstream source and model weights are intentionally not vendored into this
repository. They are downloaded during release packaging and embedded into the
portable/installer release artifacts.

## Bootstrap for development

Run from the repository root:

```powershell
.\engines\heavy\bootstrap.ps1
```

The script creates `.runtime\propainter` and installs:

- upstream ProPainter source
- CUDA-enabled PyTorch 2.5.1 + cu121
- ProPainter inference dependencies

Then install the model weights by running the upstream inference script once.

## Runtime layout expected by the application

```text
heavy/
  .venv/
    Scripts/python.exe
  ProPainter/
    inference_propainter.py
    weights/
  heavy_runner.py
```

The C# worker locates the heavy engine relative to the installed application
directory.
