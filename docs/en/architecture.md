# Architecture

The desktop application keeps a single visible WPF window. Video processing
runs in a separate, windowless worker process. The worker can call:

- FFmpeg for Delogo processing
- an OpenCV worker for ROI-only frame repair
- ProPainter for strong temporal video inpainting

Long ProPainter jobs are split into bounded chunks. The repository does not
store model weights or release binaries; these are published as release assets.
