# -*- coding: utf-8 -*-
"""Slice generated sheet, quantize to Jane palette, export 64x64 frames + gif."""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(r"D:\Github\FinalProject\sprite_anim")
SHEET = ROOT / "jane_run_stop_v1.png"
IDLE = ROOT / "jane_idle.png"
OUT = ROOT / "frames64"
OUT.mkdir(exist_ok=True)


def collect_palette(im: Image.Image):
    pal = []
    for y in range(im.height):
        for x in range(im.width):
            c = im.getpixel((x, y))
            if c[3] < 128:
                continue
            if c[:3] not in pal:
                pal.append(c[:3])
    return pal


def nearest(c, pal):
    r, g, b = c[:3]
    best = None
    best_d = 1e18
    for pr, pg, pb in pal:
        d = (r - pr) ** 2 + (g - pg) ** 2 + (b - pb) ** 2
        if d < best_d:
            best_d = d
            best = (pr, pg, pb)
    return best + (255,)


def to_palette(im: Image.Image, pal):
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sp = im.load()
    dp = out.load()
    for y in range(im.height):
        for x in range(im.width):
            c = sp[x, y]
            if c[3] < 40:
                continue
            # near-black background leftovers
            if c[0] < 18 and c[1] < 18 and c[2] < 18:
                continue
            dp[x, y] = nearest(c, pal)
    return out


def content_bbox(im: Image.Image, thresh=30):
    px = im.load()
    xs, ys = [], []
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a > 40 and (r + g + b) > thresh:
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def find_row_bands(im: Image.Image):
    px = im.load()
    rows = []
    for y in range(im.height):
        cnt = 0
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a > 20 and (r + g + b) > 40:
                cnt += 1
        if cnt > 25:
            rows.append(y)
    bands = []
    if not rows:
        return bands
    s = prev = rows[0]
    for y in rows[1:]:
        if y > prev + 5:
            bands.append((s, prev + 1))
            s = y
        prev = y
    bands.append((s, prev + 1))
    return bands


def split_cells_in_band(im: Image.Image, y0, y1, expected=None):
    """Split a horizontal band into character cells by vertical gutters."""
    px = im.load()
    cols = []
    for x in range(im.width):
        cnt = 0
        for y in range(y0, y1):
            r, g, b, a = px[x, y]
            if a > 20 and (r + g + b) > 40:
                cnt += 1
        if cnt > 8:
            cols.append(x)
    if not cols:
        return []
    segs = []
    s = prev = cols[0]
    for x in cols[1:]:
        if x > prev + 8:  # gap = new cell
            segs.append((s, prev + 1))
            s = x
        prev = x
    segs.append((s, prev + 1))
    # merge tiny noise
    segs = [(a, b) for a, b in segs if b - a > 40]
    return segs


def fit_64(sprite: Image.Image, target_h=42) -> Image.Image:
    bb = content_bbox(sprite)
    if not bb:
        return Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    crop = sprite.crop(bb)
    w, h = crop.size
    scale = target_h / h
    nw = max(1, int(round(w * scale)))
    nh = max(1, int(round(h * scale)))
    # keep within 60px width
    if nw > 56:
        scale = 56 / w
        nw = max(1, int(round(w * scale)))
        nh = max(1, int(round(h * scale)))
    resized = crop.resize((nw, nh), Image.NEAREST)
    canvas = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    # ground-align like idle (feet near y=52)
    x = (64 - nw) // 2
    y = 52 - nh
    if y < 2:
        y = 2
    canvas.paste(resized, (x, y), resized)
    return canvas


def outline_reinforce(im: Image.Image, outline=(22, 17, 2, 255)):
    """Add missing outline pixels on silhouette edge (light touch)."""
    px = im.load()
    w, h = im.size
    add = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] < 128:
                continue
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] < 128:
                    # only if neighbor is empty - mark outline candidate on current? skip
                    pass
    return im


def main():
    idle = Image.open(IDLE).convert("RGBA")
    pal = collect_palette(idle)
    print("palette size", len(pal))

    sheet = Image.open(SHEET).convert("RGBA")
    bands = find_row_bands(sheet)
    print("bands", bands)

    frames = []
    for bi, (y0, y1) in enumerate(bands):
        cells = split_cells_in_band(sheet, y0, y1)
        print(f"row {bi}: {len(cells)} cells", cells)
        for ci, (x0, x1) in enumerate(cells):
            cell = sheet.crop((x0 - 4, y0 - 4, x1 + 4, y1 + 4))
            # make black transparent-ish
            px = cell.load()
            for y in range(cell.height):
                for x in range(cell.width):
                    r, g, b, a = px[x, y]
                    if r < 25 and g < 25 and b < 25:
                        px[x, y] = (0, 0, 0, 0)
            fitted = fit_64(cell, target_h=42)
            # scale down from hi-res with intermediate for sharper pixels
            # already nearest; now palette map
            mapped = to_palette(fitted, pal)
            frames.append(mapped)

    print("total frames", len(frames))

    # Prefer: first 12 as run, remaining as stop (cap/pad)
    # If more than 20, subsample
    if len(frames) > 20:
        # take first 12 from top rows and last 8
        run = frames[:12]
        stop = frames[-8:]
        frames = run + stop
    elif len(frames) < 20:
        # pad stop with last
        while len(frames) < 20:
            frames.append(frames[-1].copy())

    for i, fr in enumerate(frames):
        tag = "run" if i < 12 else "stop"
        idx = i if i < 12 else i - 12
        fr.save(OUT / f"{tag}_{idx:02d}.png")
        fr.resize((256, 256), Image.NEAREST).save(OUT / f"_big_{tag}_{idx:02d}.png")

    # sheet preview
    cols = 6
    rows = math.ceil(len(frames) / cols)
    preview = Image.new("RGBA", (cols * 66 + 2, rows * 66 + 2), (0, 0, 0, 255))
    for i, fr in enumerate(frames):
        r, c = i // cols, i % cols
        preview.paste(fr, (2 + c * 66, 2 + r * 66), fr)
    preview.save(ROOT / "jane_run_stop_sheet64.png")
    preview.resize((preview.width * 4, preview.height * 4), Image.NEAREST).save(
        ROOT / "jane_run_stop_sheet64_x4.png"
    )

    # gif
    gif_frames = [f.resize((256, 256), Image.NEAREST) for f in frames[:12] * 2 + frames[12:]]
    gif_frames[0].save(
        ROOT / "jane_run_stop_preview.gif",
        save_all=True,
        append_images=gif_frames[1:],
        duration=90,
        loop=0,
        disposal=2,
    )
    print("done")


if __name__ == "__main__":
    main()
