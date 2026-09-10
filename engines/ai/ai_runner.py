#!/usr/bin/env python3
"""Local OpenCV inpainting bridge for the WPF app.
Generates frame-wise masks from static/dynamic rectangles, inpaints every
affected frame locally, then muxes the original audio back with FFmpeg.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import math
from collections import deque
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import cv2
import numpy as np


def progress(value: float):
    print(f"PROGRESS={max(0.0, min(100.0, value)):.2f}", flush=True)


def interp(region, t):
    keyframes = sorted(region.get("keyframes") or [], key=lambda k: float(k["time"]))
    if not keyframes:
        return region["x"], region["y"], region["width"], region["height"]
    if len(keyframes) == 1 or t <= keyframes[0]["time"]:
        k = keyframes[0]
        return k["x"], k["y"], k["width"], k["height"]
    if t >= keyframes[-1]["time"]:
        k = keyframes[-1]
        return k["x"], k["y"], k["width"], k["height"]
    for a, b in zip(keyframes, keyframes[1:]):
        if a["time"] <= t <= b["time"]:
            span = max(1e-9, float(b["time"]) - float(a["time"]))
            p = (t - float(a["time"])) / span
            vals = []
            for key in ("x", "y", "width", "height"):
                vals.append(round(float(a[key]) + (float(b[key]) - float(a[key])) * p))
            return tuple(vals)
    k = keyframes[-1]
    return k["x"], k["y"], k["width"], k["height"]


def create_mask(frame_shape, spec, time_seconds):
    h, w = frame_shape[:2]
    mask = np.zeros((h, w), dtype=np.uint8)
    actual_w = w
    actual_h = h
    spec_w = max(1, int(spec.get("width", actual_w)))
    spec_h = max(1, int(spec.get("height", actual_h)))
    sx, sy = actual_w / spec_w, actual_h / spec_h
    t = time_seconds
    start, end = float(spec.get("start", 0)), float(spec.get("end", 0))

    if start <= t <= end:
        for region in spec.get("regions", []):
            x, y, rw, rh = interp(region, t)
            x = int(round(x * sx))
            y = int(round(y * sy))
            rw = int(round(rw * sx))
            rh = int(round(rh * sy))
            x1 = max(0, min(w - 1, x))
            y1 = max(0, min(h - 1, y))
            x2 = max(x1 + 1, min(w, x + max(2, rw)))
            y2 = max(y1 + 1, min(h, y + max(2, rh)))
            cv2.rectangle(mask, (x1, y1), (x2 - 1, y2 - 1), 255, thickness=-1)

    return mask


def inpaint_video(video, spec, output_video, method, radius, expand_mask):
    progress(6)
    cap = cv2.VideoCapture(str(video))
    if not cap.isOpened():
        raise RuntimeError("OpenCV 无法读取输入视频")

    fps = cap.get(cv2.CAP_PROP_FPS)
    if not fps or not math.isfinite(fps) or fps <= 0:
        fps = 24.0
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    if width <= 0 or height <= 0:
        raise RuntimeError("无法读取视频尺寸")

    start = max(0.0, float(spec.get("start", 0.0)))
    end = float(spec.get("end", 0.0))
    if end <= start:
        end = max(start, float(spec.get("duration", 0.0)))
    duration = max(0.0, end - start)
    if duration <= 0:
        raise RuntimeError("处理时间范围无效")
    total = max(1, int(round(duration * fps)))
    if start > 0:
        cap.set(cv2.CAP_PROP_POS_MSEC, start * 1000.0)

    Path(output_video).parent.mkdir(parents=True, exist_ok=True)
    writer = None
    for codec in ("avc1", "H264", "mp4v"):
        candidate = cv2.VideoWriter(
            str(output_video),
            cv2.VideoWriter_fourcc(*codec),
            fps,
            (width, height),
        )
        if candidate.isOpened():
            writer = candidate
            break
        candidate.release()
    if writer is None:
        raise RuntimeError("OpenCV 无法创建临时输出视频")
    progress(8)

    flag = cv2.INPAINT_TELEA if method == "telea" else cv2.INPAINT_NS
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    index = 0

    def restore_frame(frame, mask):
        if not np.any(mask):
            return frame

        points = cv2.findNonZero(mask)
        if points is None:
            return frame

        x, y, rw, rh = cv2.boundingRect(points)
        margin = max(24, min(96, max(rw, rh) // 2))
        x1 = max(0, x - margin)
        y1 = max(0, y - margin)
        x2 = min(width, x + rw + margin)
        y2 = min(height, y + rh + margin)
        region = frame[y1:y2, x1:x2].copy()
        region_mask = mask[y1:y2, x1:x2]
        if np.any(region_mask):
            restored = cv2.inpaint(region, region_mask, radius, flag)
            frame[y1:y2, x1:x2] = restored
        return frame

    workers = max(1, min(4, os.cpu_count() or 1))
    executor = ThreadPoolExecutor(max_workers=workers, thread_name_prefix="wmr-inpaint")
    pending = deque()
    completed = 0
    try:
        while True:
            if index >= total:
                break
            ok, frame = cap.read()
            if not ok:
                break

            mask = create_mask(frame.shape, spec, start + index / fps)
            if expand_mask and np.any(mask):
                mask = cv2.dilate(mask, kernel, iterations=1)

            pending.append(executor.submit(restore_frame, frame, mask))
            if len(pending) >= workers * 2:
                writer.write(pending.popleft().result())
                completed += 1

            index += 1
            if completed and completed % max(1, total // 100) == 0:
                progress(8 + 80 * min(completed, total) / total)

        while pending:
            writer.write(pending.popleft().result())
            completed += 1
        progress(88)
    finally:
        executor.shutdown(wait=True, cancel_futures=False)
        cap.release()
        writer.release()

    if index == 0:
        raise RuntimeError("输入视频没有可读取的视频帧")


def detect_regions(video, seconds, max_regions, padding, display_width, display_height):
    progress(8)
    cap = cv2.VideoCapture(str(video))
    if not cap.isOpened():
        raise RuntimeError("OpenCV 无法读取输入视频")

    try:
        fps = cap.get(cv2.CAP_PROP_FPS)
        if fps and math.isfinite(fps) and fps > 0:
            cap.set(cv2.CAP_PROP_POS_FRAMES, max(0, int(round(seconds * fps))))
        else:
            cap.set(cv2.CAP_PROP_POS_MSEC, max(0, seconds * 1000.0))

        ok, frame = cap.read()
        if not ok:
            raise RuntimeError("无法读取当前时间点的视频画面")
    finally:
        cap.release()
    progress(28)

    height, width = frame.shape[:2]
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    gray = cv2.GaussianBlur(gray, (3, 3), 0)

    # Text and logos usually create compact local contrast. Top-hat and
    # black-hat avoid turning an entire bright/dark background into the mask.
    contrast_w = max(9, min(41, width // 30))
    contrast_h = max(5, min(21, height // 60))
    contrast_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (contrast_w, contrast_h))
    light_detail = cv2.morphologyEx(gray, cv2.MORPH_TOPHAT, contrast_kernel)
    dark_detail = cv2.morphologyEx(gray, cv2.MORPH_BLACKHAT, contrast_kernel)
    _, bright = cv2.threshold(light_detail, 18, 255, cv2.THRESH_BINARY)
    _, dark = cv2.threshold(dark_detail, 18, 255, cv2.THRESH_BINARY)
    edges = cv2.Canny(gray, 60, 160)
    mask = cv2.bitwise_or(cv2.bitwise_or(bright, dark), edges)
    progress(45)

    close_w = max(9, width // 80)
    close_h = max(3, height // 180)
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (close_w, close_h))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel, iterations=2)
    mask = cv2.dilate(mask, cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3)), iterations=1)

    # RETR_LIST keeps inner contours as well. This matters when a bright logo
    # sits inside a large dark background contour that is later discarded.
    contours, _ = cv2.findContours(mask, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    progress(65)
    frame_area = width * height
    boxes = []
    for contour in contours:
        x, y, w, h = cv2.boundingRect(contour)
        area = w * h
        if area < frame_area * 0.00008 or area > frame_area * 0.18:
            continue
        if w < 10 or h < 6:
            continue
        aspect = w / max(1, h)
        if aspect > 45 or aspect < 0.08:
            continue

        stroke_density = cv2.countNonZero(mask[y:y + h, x:x + w]) / max(1, area)
        if stroke_density < 0.025:
            continue

        score = area * (1.0 + stroke_density)
        if y > height * 0.62:
            score *= 1.35
        if x < width * 0.18 or x + w > width * 0.82 or y < height * 0.20:
            score *= 1.12

        pad = max(0, int(padding))
        x1 = max(0, x - pad)
        y1 = max(0, y - pad)
        x2 = min(width, x + w + pad)
        y2 = min(height, y + h + pad)
        boxes.append((score, x1, y1, x2 - x1, y2 - y1))

    boxes.sort(reverse=True, key=lambda item: item[0])
    selected = []
    for _, x, y, w, h in boxes:
        candidate = np.array([x, y, x + w, y + h], dtype=float)
        keep = True
        for existing in selected:
            ex = np.array([existing["x"], existing["y"], existing["x"] + existing["width"], existing["y"] + existing["height"]], dtype=float)
            ix1, iy1 = np.maximum(candidate[:2], ex[:2])
            ix2, iy2 = np.minimum(candidate[2:], ex[2:])
            inter = max(0.0, ix2 - ix1) * max(0.0, iy2 - iy1)
            union = w * h + existing["width"] * existing["height"] - inter
            if union > 0 and inter / union > 0.35:
                keep = False
                break
        if not keep:
            continue

        sx = display_width / width if display_width else 1.0
        sy = display_height / height if display_height else 1.0
        selected.append({
            "x": int(round(x * sx)),
            "y": int(round(y * sy)),
            "width": max(2, int(round(w * sx))),
            "height": max(2, int(round(h * sy))),
        })
        if len(selected) >= max_regions:
            break

    return selected


def parse_ffmpeg_time(value):
    value = value.strip()
    if not value or value == "N/A":
        return None
    try:
        if ":" not in value:
            return float(value)
        hours, minutes, seconds = value.split(":")
        return int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    except Exception:
        return None


def run_ffmpeg_with_progress(cmd, total_seconds, start_percent=90.0, end_percent=99.0):
    proc = subprocess.Popen(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    last_output = []

    try:
        assert proc.stdout is not None
        for line in proc.stdout:
            line = line.strip()
            if not line:
                continue
            if line.startswith("out_time="):
                seconds = parse_ffmpeg_time(line.split("=", 1)[1])
                if seconds is not None and total_seconds > 0:
                    pct = start_percent + (end_percent - start_percent) * min(1.0, seconds / total_seconds)
                    progress(pct)
            elif line == "progress=end":
                progress(end_percent)
            else:
                last_output.append(line)
                if len(last_output) > 40:
                    del last_output[0]
        code = proc.wait()
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass
        raise

    if code != 0:
        detail = "\n".join(last_output)
        raise RuntimeError(f"命令执行失败，ExitCode={code}: {' '.join(map(str, cmd))}\n{detail}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--video", required=True)
    ap.add_argument("--detect", action="store_true")
    ap.add_argument("--time", type=float, default=0.0)
    ap.add_argument("--max-regions", type=int, default=8)
    ap.add_argument("--padding", type=int, default=8)
    ap.add_argument("--display-width", type=int, default=0)
    ap.add_argument("--display-height", type=int, default=0)
    ap.add_argument("--spec")
    ap.add_argument("--output")
    ap.add_argument("--ffmpeg")
    ap.add_argument("--method", choices=("telea", "ns"), default="telea")
    ap.add_argument("--radius", type=float, default=3.0)
    ap.add_argument("--expand-mask", action="store_true")
    args = ap.parse_args()

    video = Path(args.video).resolve()

    if args.detect:
        progress(5)
        regions = detect_regions(video, args.time, args.max_regions, args.padding, args.display_width, args.display_height)
        progress(100)
        print("DETECTIONS=" + json.dumps(regions, ensure_ascii=False), flush=True)
        return

    if not args.spec or not args.output or not args.ffmpeg:
        raise RuntimeError("--spec, --output, and --ffmpeg are required unless --detect is used")

    output = Path(args.output).resolve()

    with open(args.spec, "r", encoding="utf-8") as f:
        spec = json.load(f)

    work = Path(tempfile.mkdtemp(prefix="wmr_ai_"))
    try:
        painted = work / "inpainted_no_audio.mp4"
        progress(2)
        inpaint_video(video, spec, painted, args.method, args.radius, args.expand_mask)
        progress(88)

        start = max(0.0, float(spec.get("start", 0.0)))
        end = float(spec.get("end", 0.0))
        if end <= start:
            end = max(start, float(spec.get("duration", start + 1.0)))
        total_seconds = max(0.1, end - start)
        output.parent.mkdir(parents=True, exist_ok=True)
        progress(90)
        mux_cmd = [
            args.ffmpeg, "-y", "-hide_banner", "-nostdin",
            "-i", str(painted),
            "-ss", f"{start:.6f}", "-t", f"{total_seconds:.6f}", "-i", str(video),
            "-map", "0:v:0", "-map", "1:a?",
            "-c:v", "libx264", "-preset", "medium", "-crf", "18",
            "-c:a", "aac", "-b:a", "192k",
            "-shortest", "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats", str(output),
        ]
        run_ffmpeg_with_progress(mux_cmd, total_seconds)
        progress(100)
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"AI_RUNNER_ERROR: {exc}", file=sys.stderr, flush=True)
        sys.exit(1)
