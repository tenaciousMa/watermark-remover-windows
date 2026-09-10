# Changelog

All notable changes to this project are documented in this file.

## [0.4.0] - 2026-09-10

### Added

- ProPainter strong repair mode with automatic long-video chunking.
- Processing time estimate in the editor.
- Dynamic remaining-time estimate in the queue.
- Multi-select queue action for cancel/remove.
- Priority processing for waiting jobs.
- Standard Windows installer and static project site.

### Fixed

- AI mode now honors the selected time range.
- AI mode uses ROI-only inpainting and multithreading.
- ProPainter no longer reads an entire long video into memory.
- FFmpeg Delogo edge boxes are clamped inside the frame.
- Progress bars fill left-to-right.

## [0.3.0] - 2026-09-09

### Added

- Background worker process.
- Multi-video queue.
- In-window queue progress view.

## [0.2.0] - 2026-09-09

### Added

- FFmpeg and OpenCV processing modes.
- Global themes.
- In-frame video timeline and playback controls.
