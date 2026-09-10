# -*- coding: utf-8 -*-
"""ProPainter-backed strong video repair bridge for Watermark Remover v0.4.

The WPF worker process launches this script in the bundled Python runtime.
Progress is streamed as ``PROGRESS=`` and ``STATUS=`` lines so the C# worker
can update its status file without blocking the main UI.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import cv2
import numpy as np


def emit_progress(value: float):
    print(f"PROGRESS={max(0.0, min(100.0, value)):.2f}", flush=True)


def emit_status(text: str):
    print(f"STATUS={text}", flush=True)


def parse_time(text: str, fallback: float) -> float:
    text = (text or "").strip()
    if not text:
        return fallback
    try:
        return float(text)
    except ValueError:
        pass
    parts = text.replace(",", ".").split(":")
    try:
        values = [float(p) for p in parts]
    except ValueError:
        return fallback
    if len(values) == 3:
        return values[0] * 3600 + values[1] * 60 + values[2]
    if len(values) == 2:
        return values[0] * 60 + values[1]
    return fallback


def probe_video(ffprobe: Path, video: Path):
    cmd = [
        str(ffprobe), "-v", "error", "-select_streams", "v:0",
        "-show_entries", "stream=width,height,avg_frame_rate,nb_frames,duration",
        "-of", "json", str(video),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if proc.returncode != 0:
        raise RuntimeError(f"ffprobe 读取视频信息失败：{proc.stderr.strip()}")
    data = json.loads(proc.stdout or "{}")
    streams = data.get("streams") or []
    if not streams:
        raise RuntimeError("ffprobe 没有找到视频流。")
    stream = streams[0]
    width = int(stream.get("width") or 0)
    height = int(stream.get("height") or 0)
    duration = float(stream.get("duration") or 0.0)
    fps = parse_fraction(stream.get("avg_frame_rate") or stream.get("r_frame_rate") or "25")
    if duration <= 0:
        raise RuntimeError("无法读取视频时长。")
    if width <= 0 or height <= 0:
        raise RuntimeError("无法读取视频尺寸。")
    return {
        "width": width,
        "height": height,
        "fps": fps,
        "duration": duration,
    }


def parse_fraction(text: str) -> float:
    text = (text or "").strip()
    if not text or text in ("N/A", "0/0"):
        return 25.0
    if "/" in text:
        left, right = text.split("/", 1)
        try:
            den = float(right)
            if den:
                return float(left) / den
        except ValueError:
            pass
    try:
        value = float(text)
        return value if value > 0 else 25.0
    except ValueError:
        return 25.0


def get_value(item: dict, names):
    for name in names:
        if name in item and item[name] is not None:
            return item[name]
    return None


def normalize_regions(regions_file: Path):
    with open(regions_file, "r", encoding="utf-8-sig") as handle:
        raw = json.load(handle)
    regions = raw if isinstance(raw, list) else raw.get("regions") or []
    normalized = []
    for region in regions:
        if not isinstance(region, dict):
            continue
        base = {
            "name": str(get_value(region, ("name", "Name")) or "水印区域"),
            "x": float(get_value(region, ("x", "X")) or 0),
            "y": float(get_value(region, ("y", "Y")) or 0),
            "width": float(get_value(region, ("width", "Width")) or 1),
            "height": float(get_value(region, ("height", "Height")) or 1),
            "keyframes": [],
        }
        keyframes_raw = region.get("keyframes") or region.get("Keyframes") or []
        for keyframe in keyframes_raw:
            if not isinstance(keyframe, dict):
                continue
            time_value = get_value(keyframe, ("time", "timeSeconds", "TimeSeconds"))
            base["keyframes"].append({
                "time": float(time_value or 0),
                "x": float(get_value(keyframe, ("x", "X")) or base["x"]),
                "y": float(get_value(keyframe, ("y", "Y")) or base["y"]),
                "width": float(get_value(keyframe, ("width", "Width")) or base["width"]),
                "height": float(get_value(keyframe, ("height", "Height")) or base["height"]),
            })
        base["keyframes"].sort(key=lambda item: item["time"])
        normalized.append(base)
    if not normalized:
        raise RuntimeError("没有可用的水印区域：请先手动框选，或先执行自动检测。")
    return normalized


def geometry_at(region: dict, time_seconds: float):
    keyframes = region["keyframes"]
    if not keyframes:
        return (region["x"], region["y"], region["width"], region["height"])
    if time_seconds <= keyframes[0]["time"]:
        frame = keyframes[0]
        return (frame["x"], frame["y"], frame["width"], frame["height"])
    if time_seconds >= keyframes[-1]["time"]:
        frame = keyframes[-1]
        return (frame["x"], frame["y"], frame["width"], frame["height"])
    for left, right in zip(keyframes, keyframes[1:]):
        if left["time"] <= time_seconds <= right["time"]:
            span = max(1e-9, right["time"] - left["time"])
            amount = (time_seconds - left["time"]) / span
            return tuple(
                float(left[key]) + (float(right[key]) - float(left[key])) * amount
                for key in ("x", "y", "width", "height")
            )
    frame = keyframes[-1]
    return (frame["x"], frame["y"], frame["width"], frame["height"])


def clamp_box(box, width, height):
    x, y, w, h = box
    x1 = max(0, min(width, int(round(x))))
    y1 = max(0, min(height, int(round(y))))
    x2 = max(x1 + 1, min(width, int(round(x + w))))
    y2 = max(y1 + 1, min(height, int(round(y + h))))
    return (x1, y1, x2 - x1, y2 - y1)


def estimate_times(regions: list, start: float, end: float):
    times = {start, end}
    for region in regions:
        for keyframe in region["keyframes"]:
            if start <= keyframe["time"] <= end:
                times.add(keyframe["time"])
    return sorted(times)


def region_union(regions: list, start: float, end: float, width: int, height: int):
    boxes = []
    for time_seconds in estimate_times(regions, start, end):
        for region in regions:
            boxes.append(clamp_box(geometry_at(region, time_seconds), width, height))
    if not boxes:
        raise RuntimeError("水印区域超出视频范围。")
    x1 = min(box[0] for box in boxes)
    y1 = min(box[1] for box in boxes)
    x2 = max(box[0] + box[2] for box in boxes)
    y2 = max(box[1] + box[3] for box in boxes)
    return (x1, y1, max(1, x2 - x1), max(1, y2 - y1))


def region_is_static(regions: list) -> bool:
    return all(not region["keyframes"] for region in regions)


def build_plan(regions: list, start: float, end: float, width: int, height: int, duration: float):
    # 6 GB laptop GPUs become unstable well before ProPainter's theoretical
    # memory ceiling. A compact patch plus a conservative internal area keeps
    # the GPU out of shared-memory thrashing and still preserves the original
    # output resolution when the patch is composited back.
    base_area_limit = int(os.environ.get("WMR_HEAVY_PROCESS_AREA", "180000"))
    force_long = os.environ.get("WMR_HEAVY_FORCE_LONG", "").lower() in ("1", "true", "yes")
    if duration > 120 or force_long:
        area_limit = min(base_area_limit, int(os.environ.get("WMR_HEAVY_LONG_AREA", "100000")))
    elif duration > 45:
        area_limit = min(base_area_limit, int(os.environ.get("WMR_HEAVY_MEDIUM_AREA", "150000")))
    else:
        area_limit = base_area_limit
    full_area = width * height
    union = region_union(regions, start, end, width, height)
    union_x, union_y, union_w, union_h = union

    if full_area <= area_limit:
        return {
            "mode": "full",
            "roi": (0, 0, width, height),
            "process_width": None,
            "process_height": None,
            "scale": 1.0,
            "area_limit": area_limit,
        }

    # A generous border around the watermark keeps motion context for the
    # model without making the whole high-resolution frame the input.
    margin_x = int(min(max(80, union_w * 0.9), 420))
    margin_y = int(min(max(80, union_h * 0.9), 420))
    crop_x1 = max(0, union_x - margin_x)
    crop_y1 = max(0, union_y - margin_y)
    crop_x2 = min(width, union_x + union_w + margin_x)
    crop_y2 = min(height, union_y + union_h + margin_y)
    crop_w = max(8, crop_x2 - crop_x1)
    crop_h = max(8, crop_y2 - crop_y1)
    crop_area = crop_w * crop_h

    if crop_area > area_limit and union_w * union_h <= area_limit:
        # Shrink only the margin until the patch fits in GPU memory.
        factor = math.sqrt(area_limit / max(1, crop_area))
        new_margin_x = max(0, int(margin_x * factor))
        new_margin_y = max(0, int(margin_y * factor))
        crop_x1 = max(0, union_x - new_margin_x)
        crop_y1 = max(0, union_y - new_margin_y)
        crop_x2 = min(width, union_x + union_w + new_margin_x)
        crop_y2 = min(height, union_y + union_h + new_margin_y)
        crop_w = max(8, crop_x2 - crop_x1)
        crop_h = max(8, crop_y2 - crop_y1)
        crop_area = crop_w * crop_h

    if crop_area <= area_limit:
        return {
            "mode": "crop",
            "roi": (crop_x1, crop_y1, crop_w, crop_h),
            "process_width": None,
            "process_height": None,
            "scale": 1.0,
            "area_limit": area_limit,
        }

    # A very large moving or multi-region watermark leaves no compact patch.
    # Fall back to whole-frame adaptive inference and upscale back.
    scale = math.sqrt(area_limit / max(1.0, full_area))
    return {
        "mode": "full",
        "roi": (0, 0, width, height),
        "process_width": None,
        "process_height": None,
        "scale": scale,
        "area_limit": area_limit,
    }


def make_segment(input_path: Path, output_path: Path, ffmpeg: Path, start: float, duration: float,
                 roi=None, scale_to=None):
    cmd = [
        str(ffmpeg), "-y", "-hide_banner", "-nostdin", "-ss", format_seconds(start),
        "-i", str(input_path), "-t", format_seconds(duration), "-an",
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "12",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
    ]
    filters = []
    if roi:
        x, y, w, h = roi
        filters.append(f"crop={w}:{h}:{x}:{y}")
    if scale_to:
        filters.append(f"scale={scale_to[0]}:{scale_to[1]}:flags=lanczos")
    if filters:
        cmd.extend(["-vf", ",".join(filters)])
    cmd.append(str(output_path))
    run_capture(cmd, ffmpeg_context="准备强力修复视频片段")
    if not output_path.exists() or output_path.stat().st_size < 1000:
        raise RuntimeError("FFmpeg 生成待修复视频片段失败。")


def draw_static_mask(mask_path: Path, regions: list, width: int, height: int, roi):
    mask = np.zeros((height, width), dtype=np.uint8)
    roi_x, roi_y = roi[0], roi[1]
    for region in regions:
        box = geometry_at(region, 0)
        x1 = max(0, min(width, int(round(box[0] - roi_x))))
        y1 = max(0, min(height, int(round(box[1] - roi_y))))
        x2 = max(x1 + 1, min(width, int(round(box[0] + box[2] - roi_x))))
        y2 = max(y1 + 1, min(height, int(round(box[1] + box[3] - roi_y))))
        cv2.rectangle(mask, (x1, y1), (x2 - 1, y2 - 1), 255, thickness=-1)
    if not np.any(mask):
        raise RuntimeError("水印遮罩没有覆盖任何画面区域。")
    cv2.imwrite(str(mask_path), mask)


def make_dynamic_masks(mask_dir: Path, segment_path: Path, regions: list, roi, fps: float, start: float):
    mask_dir.mkdir(parents=True, exist_ok=True)
    cap = cv2.VideoCapture(str(segment_path))
    if not cap.isOpened():
        raise RuntimeError("OpenCV 无法读取待修复视频片段。")
    roi_x, roi_y = roi[0], roi[1]
    index = 0
    try:
        while True:
            ok, _frame = cap.read()
            if not ok:
                break
            time_seconds = start + index / max(1.0, fps)
            mask = np.zeros((roi[3], roi[2]), dtype=np.uint8)
            for region in regions:
                x, y, w, h = geometry_at(region, time_seconds)
                x1 = max(0, min(roi[2], int(round(x - roi_x))))
                y1 = max(0, min(roi[3], int(round(y - roi_y))))
                x2 = max(x1 + 1, min(roi[2], int(round(x + w - roi_x))))
                y2 = max(y1 + 1, min(roi[3], int(round(y + h - roi_y))))
                cv2.rectangle(mask, (x1, y1), (x2 - 1, y2 - 1), 255, thickness=-1)
            cv2.imwrite(str(mask_dir / f"{index:08d}.png"), mask)
            index += 1
    finally:
        cap.release()
    if index == 0:
        raise RuntimeError("待修复视频没有可读取的视频帧。")
    emit_progress(10)
    return index


def run_capture(cmd, ffmpeg_context=""):
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if proc.returncode != 0:
        detail = (proc.stderr or proc.stdout or "").strip()
        raise RuntimeError(f"{ffmpeg_context}失败：{detail[-3000:]}")
    return proc


def run_propainter(engine_root: Path, segment_path: Path, mask_source: Path, attempt_dir: Path,
                   fps: float, mask_dilation: int, width: int | None = None, height: int | None = None,
                   on_progress=None, fast_long: bool = False):
    python = engine_root / ".venv" / "Scripts" / "python.exe"
    repo = engine_root / "ProPainter"
    script = repo / "inference_propainter.py"
    if not python.exists() or not script.exists():
        raise RuntimeError("未找到 ProPainter Python 环境或推理脚本。")

    subvideo_length = 12
    neighbor_length = 4
    ref_stride = 16
    raft_iter = 8
    cmd = [
        str(python), str(script), "--video", str(segment_path),
        "--mask", str(mask_source), "-o", str(attempt_dir),
        "--save_fps", str(int(round(fps))), "--fp16",
        "--subvideo_length", str(subvideo_length),
        "--neighbor_length", str(neighbor_length),
        "--ref_stride", str(ref_stride), "--raft_iter", str(raft_iter),
        "--mask_dilation", str(mask_dilation),
    ]
    if width is not None and height is not None:
        cmd.extend(["--width", str(width), "--height", str(height)])

    env = os.environ.copy()
    env["PYTHONIOENCODING"] = "utf-8"
    proc = subprocess.Popen(
        cmd,
        cwd=str(repo),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=env,
    )
    output_lines = []
    assert proc.stdout is not None
    try:
        for raw_line in proc.stdout:
            line = raw_line.strip("\r\n")
            if line.strip():
                output_lines.append(line)
                if len(output_lines) > 120:
                    output_lines.pop(0)
            lowered = line.lower()
            if "download" in lowered or "processing:" in lowered:
                if on_progress:
                    on_progress(14, "ProPainter 正在计算光流并重建受损区域…")
                continue
            if "all results are saved" in lowered:
                if on_progress:
                    on_progress(92, "强力修复完成，正在封装视频…")
                continue
            match = re.search(r"(\d{1,5})/(\d{1,5})", line)
            if match:
                try:
                    current = int(match.group(1))
                    total = int(match.group(2))
                    if total > 0 and on_progress:
                        on_progress(15 + 74.0 * current / total, "ProPainter 正在逐帧重建水印区域…")
                except ValueError:
                    pass
            if "cuda out of memory" in lowered:
                raise RuntimeError("CUDA 显存不足，ProPainter 推理中止。")
        code = proc.wait()
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass
        raise
    if code != 0:
        detail = "\n".join(output_lines[-60:])
        raise RuntimeError(f"ProPainter 推理失败（ExitCode={code}）。\n{detail[-4000:]}")

    video_name = segment_path.stem
    inpaint = attempt_dir / video_name / "inpaint_out.mp4"
    if not inpaint.exists():
        raise RuntimeError("ProPainter 推理结束，但没有生成输出视频。")
    return inpaint


def format_seconds(value: float) -> str:
    return f"{value:.3f}"


def final_mux(input_path: Path, inpainted: Path, output_path: Path, ffmpeg: Path,
              start: float, duration: float, roi, scale_to, fps: float, audio_available: bool):
    out_video = output_path.with_suffix(".video.mp4")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    roi_w, roi_h = roi[2], roi[3]
    src_w, src_h = scale_to
    base_filters = [
        f"[0:v]trim=start={format_seconds(start)}:end={format_seconds(start + duration)},setpts=PTS-STARTPTS[base0]"
    ]
    fixed_filters = [f"[1:v]scale={roi_w}:{roi_h}:flags=lanczos,setsar=1[fixed]"]
    overlay = f"[base0][fixed]overlay={roi[0]}:{roi[1]}:shortest=0[outv]"
    filter_complex = ";".join(base_filters + fixed_filters + [overlay])
    if roi == (0, 0, src_w, src_h) and roi_w == src_w and roi_h == src_h:
        # Adaptive whole-frame mode: the inpainted video is the full replacement.
        filter_complex = (
            f"[0:v]trim=start={format_seconds(start)}:end={format_seconds(start + duration)},setpts=PTS-STARTPTS[base0];"
            f"[1:v]scale={src_w}:{src_h}:flags=lanczos,setsar=1[fixed];"
            f"[base0][fixed]overlay=0:0:shortest=0[outv]"
        )

    cmd = [
        str(ffmpeg), "-y", "-hide_banner", "-nostdin",
        "-i", str(input_path), "-i", str(inpainted),
        "-filter_complex", filter_complex,
        "-map", "[outv]",
    ]
    if audio_available:
        cmd += [
            "-map", "0:a?",
            "-af", f"atrim=start={format_seconds(start)}:end={format_seconds(start + duration)},asetpts=PTS-STARTPTS",
            "-c:a", "aac", "-b:a", "192k",
        ]
    cmd += [
        "-c:v", "libx264", "-preset", "medium", "-crf", "18",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        "-progress", "pipe:1", "-nostats", str(out_video),
    ]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    assert proc.stdout is not None
    last_error = []
    try:
        for line in proc.stdout:
            if "out_time_ms=" in line:
                try:
                    ms = float(line.strip().split("=", 1)[1])
                    percent = 95 + 4.5 * min(1.0, ms / 1000.0 / max(0.1, duration))
                    emit_progress(percent)
                except ValueError:
                    pass
            elif line.strip():
                last_error.append(line)
                if len(last_error) > 60:
                    last_error.pop(0)
        code = proc.wait()
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass
        raise
    if code != 0:
        detail = "\n".join(last_error[-60:])
        raise RuntimeError(f"视频封装失败（ExitCode={code}）。\n{detail[-4000:]}")
    if not out_video.exists():
        raise RuntimeError("视频封装完成，但没有找到输出文件。")
    out_video.replace(output_path)


def check_audio(ffprobe: Path, video: Path) -> bool:
    cmd = [str(ffprobe), "-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "csv=p=0", str(video)]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return proc.returncode == 0 and bool((proc.stdout or "").strip())


def choose_chunk_seconds(info: dict, roi, process_size, duration: float) -> float:
    configured = float(os.environ.get("WMR_HEAVY_CHUNK_SECONDS", "10"))
    budget_mb = float(os.environ.get("WMR_HEAVY_RAM_BUDGET_MB", "1200"))
    if process_size:
        width, height = process_size
    else:
        width, height = roi[2], roi[3]
    bytes_per_second = max(1.0, width * height * 3.0 * max(1.0, info["fps"]))
    safe_seconds = budget_mb * 1024.0 * 1024.0 / bytes_per_second
    return max(2.0, min(duration, configured, safe_seconds))


def concat_videos(ffmpeg: Path, videos: list[Path], output: Path):
    if len(videos) == 1:
        shutil.copy2(videos[0], output)
        return
    list_path = output.with_suffix(".concat.txt")
    lines = []
    for video in videos:
        path = video.resolve().as_posix().replace("'", "'\\''")
        lines.append(f"file '{path}'")
    list_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    run_capture([
        str(ffmpeg), "-y", "-hide_banner", "-nostdin", "-f", "concat", "-safe", "0",
        "-i", str(list_path), "-c", "copy", "-movflags", "+faststart", str(output),
    ], ffmpeg_context="合并强力修复片段")
    if not output.exists() or output.stat().st_size < 1000:
        raise RuntimeError("强力修复片段合并失败。")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--regions-file", required=True)
    parser.add_argument("--start", default="0")
    parser.add_argument("--end", default="0")
    parser.add_argument("--ffmpeg", required=True)
    parser.add_argument("--ffprobe", required=True)
    parser.add_argument("--expand-mask", action="store_true")
    args = parser.parse_args()

    input_path = Path(args.input).resolve()
    output_path = Path(args.output).resolve()
    regions_file = Path(args.regions_file).resolve()
    ffmpeg = Path(args.ffmpeg).resolve()
    ffprobe = Path(args.ffprobe).resolve()
    if not input_path.exists() or not regions_file.exists():
        raise RuntimeError("输入视频或水印区域文件不存在。")
    if not ffmpeg.exists() or not ffprobe.exists():
        raise RuntimeError("未找到 FFmpeg/ffprobe，无法完成强力修复。")

    engine_root = Path(os.environ.get("WMR_HEAVY_ROOT") or Path(__file__).resolve().parent)
    regions = normalize_regions(regions_file)
    info = probe_video(ffprobe, input_path)
    start = parse_time(args.start, 0.0)
    end = parse_time(args.end, info["duration"])
    if end <= start:
        end = info["duration"]
    if end > info["duration"] + 0.01:
        end = info["duration"]
    duration = end - start
    if duration <= 0:
        raise RuntimeError("处理时间范围无效。")

    plan = build_plan(regions, start, end, info["width"], info["height"], duration)
    mask_dilation = 8 if args.expand_mask else 4
    temp_root = Path(tempfile.mkdtemp(prefix="wmr_heavy_"))
    try:
        emit_status("正在准备强力修复视频…")
        emit_progress(2)
        roi = plan["roi"]
        if plan["mode"] == "full" and plan["scale"] < 1.0:
            pw = max(8, int((info["width"] * plan["scale"]) // 8 * 8))
            ph = max(8, int((info["height"] * plan["scale"]) // 8 * 8))
            initial_process_size = (pw, ph)
        elif roi[2] * roi[3] > plan["area_limit"]:
            factor = math.sqrt(plan["area_limit"] / max(1, roi[2] * roi[3]))
            pw = max(8, int((roi[2] * factor) // 8 * 8))
            ph = max(8, int((roi[3] * factor) // 8 * 8))
            initial_process_size = (pw, ph)
        else:
            initial_process_size = None

        chunk_seconds = choose_chunk_seconds(info, roi, initial_process_size, duration)
        chunk_count = max(1, int(math.ceil(duration / chunk_seconds)))
        fast_long = duration > 120 or os.environ.get("WMR_HEAVY_FORCE_LONG", "").lower() in ("1", "true", "yes")
        static = region_is_static(regions)
        inpainted_chunks = []
        chosen_dims_initialized = False
        chosen_dims = initial_process_size

        for chunk_index in range(chunk_count):
            chunk_start = start + chunk_index * chunk_seconds
            chunk_duration = min(chunk_seconds, end - chunk_start)
            if chunk_duration <= 0:
                break
            chunk_root = temp_root / f"chunk_{chunk_index:04d}"
            chunk_root.mkdir(parents=True, exist_ok=True)
            emit_status(f"正在处理强力修复片段 {chunk_index + 1}/{chunk_count}…")
            emit_progress(11.0 + 80.0 * chunk_index / max(1, chunk_count))

            segment_path = chunk_root / "segment.mp4"
            segment_scale = initial_process_size
            make_segment(
                input_path,
                segment_path,
                ffmpeg,
                chunk_start,
                chunk_duration,
                roi if plan["mode"] == "crop" else None,
                segment_scale,
            )

            if static:
                mask_source = chunk_root / "mask.png"
                draw_static_mask(mask_source, regions, roi[2], roi[3], roi)
            else:
                mask_source = chunk_root / "masks"
                make_dynamic_masks(mask_source, segment_path, regions, roi, info["fps"], chunk_start)

            dimensions_to_try = []
            if chosen_dims_initialized:
                dimensions_to_try.append(chosen_dims)
            else:
                dimensions_to_try.append(initial_process_size)
            base_dims = chosen_dims if chosen_dims_initialized else initial_process_size
            if base_dims is None:
                base_dims = (roi[2], roi[3])
            for factor in (0.8, 0.62, 0.48, 0.36):
                candidate = (
                    max(8, int((base_dims[0] * factor) // 8 * 8)),
                    max(8, int((base_dims[1] * factor) // 8 * 8)),
                )
                if candidate not in dimensions_to_try:
                    dimensions_to_try.append(candidate)

            chunk_result = None
            last_error = None
            for dims in dimensions_to_try:
                attempt_dir = chunk_root / f"propaint_{len(list(chunk_root.glob('propaint_*')))}"
                try:
                    width_arg = dims[0] if dims else None
                    height_arg = dims[1] if dims else None

                    def map_progress(value, text, current_chunk=chunk_index):
                        local = max(0.0, min(1.0, (value - 15.0) / 77.0))
                        overall = 11.0 + 80.0 * (current_chunk + local) / max(1, chunk_count)
                        emit_progress(overall)
                        emit_status(text)

                    chunk_result = run_propainter(
                        engine_root,
                        segment_path,
                        mask_source,
                        attempt_dir,
                        info["fps"],
                        mask_dilation,
                        width=width_arg,
                        height=height_arg,
                        on_progress=map_progress,
                        fast_long=fast_long,
                    )
                    chosen_dims = dims
                    chosen_dims_initialized = True
                    break
                except Exception as exc:
                    message = str(exc)
                    lowered = message.lower()
                    memory_issue = (
                        "out of memory" in lowered or
                        "cannot allocate memory" in lowered or
                        "memoryerror" in lowered or
                        "显存不足" in message
                    )
                    if not memory_issue:
                        raise
                    last_error = exc
                    emit_status("内存或显存不足，自动降低处理尺寸后重试…")

            if chunk_result is None:
                raise RuntimeError(last_error or "ProPainter 多次尝试均因内存不足失败。")
            stable_chunk = temp_root / "inpainted_chunks" / f"{chunk_index:04d}.mp4"
            stable_chunk.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(chunk_result), str(stable_chunk))
            shutil.rmtree(chunk_root, ignore_errors=True)
            inpainted_chunks.append(stable_chunk)

        if not inpainted_chunks:
            raise RuntimeError("没有生成任何强力修复片段。")
        inpainted = temp_root / "inpainted_merged.mp4"
        concat_videos(ffmpeg, inpainted_chunks, inpainted)

        emit_status("正在把修复结果写回原始视频并保留音频…")
        emit_progress(94)
        final_mux(
            input_path,
            inpainted,
            output_path,
            ffmpeg,
            start,
            duration,
            roi,
            (info["width"], info["height"]),
            info["fps"],
            check_audio(ffprobe, input_path),
        )
        emit_status("处理完成")
        emit_progress(100)
    finally:
        shutil.rmtree(temp_root, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"HEAVY_ERROR: {exc}", file=sys.stderr, flush=True)
        emit_status("处理失败")
        sys.exit(1)
