# -*- coding: utf-8 -*-
"""Builds the application icon at high resolution, then downscales it."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parent
SIZE = 1024
S = 4
W = SIZE * S


def gradient_background():
    y, x = np.mgrid[0:W, 0:W]
    falloff = ((x - W * 0.5) ** 2 + (y - W * 0.36) ** 2) / (2.0 * (W * 0.62) ** 2)
    top = np.array([9, 23, 49], dtype=np.float64)
    middle = np.array([11, 84, 115], dtype=np.float64)
    bottom = np.array([17, 172, 162], dtype=np.float64)
    v = np.clip((x / W) * 0.48 + (y / W) * 0.66, 0, 1)
    glow = np.exp(-falloff)
    rgb = np.zeros((W, W, 3), dtype=np.float64)
    for index, colors in enumerate(((top, middle), (middle, bottom))):
        a, b = colors
        rgb += np.clip(v * 2 - index, 0, 1)[..., None] * (b - a)[None, None, :]
    rgb += top[None, None, :]
    rgb += np.clip(glow * 0.35, 0, 1)[..., None] * np.array([72, 164, 178], dtype=np.float64)
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    alpha = np.full((W, W), 255, dtype=np.uint8)
    return Image.fromarray(np.dstack([rgb, alpha]), "RGBA")


def rounded_mask():
    mask = Image.new("L", (W, W), 0)
    draw = ImageDraw.Draw(mask)
    radius = 235 * S
    draw.rounded_rectangle((0, 0, W - 1, W - 1), radius=radius, fill=255)
    return mask


def draw_star(draw, cx, cy, outer, inner, color, angle=0.0):
    import math

    points = []
    for i in range(8):
        radius = outer if i % 2 == 0 else inner
        theta = angle + i * math.pi / 4
        points.append((cx + math.cos(theta) * radius, cy + math.sin(theta) * radius))
    draw.polygon(points, fill=color)


def build():
    canvas = gradient_background()
    canvas.putalpha(rounded_mask())
    canvas = canvas.filter(ImageFilter.GaussianBlur(0.7 * S))
    draw = ImageDraw.Draw(canvas)

    line = (199, 241, 234, 255)
    bright = (244, 252, 255, 255)
    accent = (36, 207, 187, 255)

    frame = (282 * S, 252 * S, 742 * S, 772 * S)
    draw.rounded_rectangle(frame, radius=54 * S, outline=line, width=11 * S)

    triangle = [(386 * S, 326 * S), (718 * S, 512 * S), (386 * S, 698 * S)]
    draw.polygon(triangle, fill=bright)

    # A clean diagonal "eraser streak" inside the play button keeps the icon
    # readable at small sizes and suggests removing content from the video.
    streak_left = (348 * S, 590 * S)
    streak_right = (744 * S, 365 * S)
    streak_width = 54 * S
    draw.line(
        (streak_left[0] + 20 * S, streak_left[1] - 14 * S,
         streak_right[0] + 14 * S, streak_right[1] - 16 * S),
        fill=accent,
        width=streak_width,
    )
    draw.line(
        (streak_left[0], streak_left[1],
         streak_right[0] - 4 * S, streak_right[1] - 4 * S),
        fill=(31, 175, 162, 255),
        width=streak_width,
    )

    # Corner sparkles echo the "AI" nature of the strongest repair mode.
    draw_star(draw, 668 * S, 312 * S, 56 * S, 22 * S, bright)
    draw_star(draw, 786 * S, 692 * S, 42 * S, 16 * S, (161, 232, 220, 255), angle=0.35)

    png = canvas.resize((SIZE, SIZE), Image.LANCZOS)
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    ico = png.resize((256, 256), Image.LANCZOS)
    ico.save(ROOT / "AppLogo.ico", sizes=[(s, s) for s in ico_sizes])
    png.save(ROOT / "AppLogo.png")
    print("logo assets written")


if __name__ == "__main__":
    build()
