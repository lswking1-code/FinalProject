#!/usr/bin/env python3
"""
v2: Reskin Marco 64x64 pose silhouettes into the hooded tan character.
Pose fidelity from Marco; design from character.png / sideview palette.
"""

from __future__ import annotations

import math
import os
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
FRAMES_DIR = ROOT / "frames"
REF64 = ROOT / "ref_frames"
ASEPRITE = Path(r"E:\SteamLibrary\steamapps\common\Aseprite\Aseprite.exe")

OUTLINE = np.array([0x16, 0x11, 0x02, 255], dtype=np.uint8)
TAN_L = np.array([0xB2, 0xA3, 0x85, 255], dtype=np.uint8)
TAN_M = np.array([0x6A, 0x5E, 0x46, 255], dtype=np.uint8)
TAN_D = np.array([0x58, 0x4B, 0x37, 255], dtype=np.uint8)
TAN_S = np.array([0xAD, 0x9E, 0x82, 255], dtype=np.uint8)
HOOD_IN = np.array([0x13, 0x0F, 0x02, 255], dtype=np.uint8)
CYAN = np.array([0x47, 0xD4, 0xF4, 255], dtype=np.uint8)
BLUE_D = np.array([0x0D, 0x36, 0xA5, 255], dtype=np.uint8)
BLUE_M = np.array([0x11, 0x32, 0x98, 255], dtype=np.uint8)
WHITE = np.array([0xF4, 0xED, 0xEC, 255], dtype=np.uint8)
GUN_L = np.array([0x7A, 0x7A, 0x80, 255], dtype=np.uint8)
GUN_M = np.array([0x3A, 0x3A, 0x40, 255], dtype=np.uint8)
GUN_D = np.array([0x1A, 0x1A, 0x1E, 255], dtype=np.uint8)
BOOT = np.array([0x2C, 0x28, 0x20, 255], dtype=np.uint8)
BOOT_L = np.array([0x4A, 0x44, 0x38, 255], dtype=np.uint8)


def load_marco64(i: int) -> np.ndarray:
    path = REF64 / f"m64_{i:02d}.png"
    if not path.exists():
        # build on the fly
        files = [f for f in os.listdir(REF64) if f.startswith(f"f{i:02d}_")]
        im = Image.open(REF64 / files[0]).convert("RGBA")
        im2 = im.resize((im.width * 2, im.height * 2), Image.NEAREST)
        canvas = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        a = np.array(im2)
        ys, xs = np.where(a[:, :, 3] > 0)
        bottom, left = int(ys.max()), int(xs.min())
        ox = (64 - (int(xs.max()) - left + 1)) // 2 - left
        oy = 58 - bottom
        canvas.paste(im2, (ox, oy), im2)
        canvas.save(path)
        return np.array(canvas)
    return np.array(Image.open(path).convert("RGBA"))


def classify_pixels(marco: np.ndarray):
    """Return semantic masks from Marco colors."""
    r, g, b, a = marco[:, :, 0], marco[:, :, 1], marco[:, :, 2], marco[:, :, 3]
    sil = a > 128
    # hair / headband / face (upper)
    hair = sil & (r > 170) & (g > 140) & (b < 120) & (r + g > b * 2)
    skin = sil & (r > 180) & (g > 130) & (b > 90) & (r > g) & (g > b - 20) & ~hair
    headband = sil & (r > 200) & (g > 200) & (b > 200)
    red = sil & (r > 140) & (g < 110) & (b < 110)
    # pants olive
    pants = sil & (g + 10 > r) & (g > b) & (r < 170) & (g < 190) & (b < 140) & ~red & ~skin
    # boots gray/white-ish lower
    boots = sil & (r > 100) & (g > 100) & (b > 100) & (r < 210) & (np.abs(r.astype(int) - g.astype(int)) < 25)
    # gun / dark metal
    gun = sil & (r < 100) & (g < 100) & (b < 100) & ((r.astype(int) + g + b) > 40)
    # backpack often tan/gray behind
    pack = sil & ~red & ~skin & ~hair & ~pants & ~gun & ~headband & (r > 120) & (g > 110) & (b > 80)

    # refine head = top cluster of hair+skin+headband
    head = hair | skin | headband
    # anything remaining in silhouette
    other = sil & ~(head | red | pants | boots | gun | pack)

    return {
        "sil": sil,
        "head": head,
        "torso": red | other | pack,
        "pants": pants,
        "boots": boots,
        "gun": gun,
        "pack": pack,
        "skin": skin,
        "hair": hair,
    }


def shade_fill(out, mask, base, mid, dark, light=None):
    """Fill mask with simple top-light shading."""
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return
    y0, y1 = ys.min(), ys.max()
    h = max(1, y1 - y0)
    for y, x in zip(ys, xs):
        t = (y - y0) / h
        # also left/right: slightly darker toward back (left)
        xb = (x - xs.min()) / max(1, xs.max() - xs.min())
        if t < 0.25 and (light is not None) and xb > 0.35:
            out[y, x] = light
        elif t < 0.45:
            out[y, x] = base
        elif t < 0.75:
            out[y, x] = mid
        else:
            out[y, x] = dark
        # back edge darker
        if xb < 0.2:
            out[y, x] = mid if t < 0.5 else dark


def add_outline(out, sil):
    h, w = out.shape[:2]
    for y in range(h):
        for x in range(w):
            if sil[y, x]:
                continue
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and sil[ny, nx]:
                    out[y, x] = OUTLINE
                    break
    # also darken internal edges slightly: where neighbor different class — skip for now
    return out


def stamp_hood(out, masks, marco):
    """Replace head region with hood + cyan eyes."""
    head = masks["head"]
    sil = masks["sil"]
    ys, xs = np.where(head)
    if len(xs) == 0:
        # fallback: top of silhouette
        sy, sx = np.where(sil)
        if len(sx) == 0:
            return
        y_cut = sy.min() + 14
        head = sil & (np.arange(64)[:, None] <= y_cut)
        ys, xs = np.where(head)
        if len(xs) == 0:
            return

    cx, cy = int(xs.mean()), int(ys.mean())
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())

    # expand hood slightly over shoulders
    hood_mask = np.zeros_like(sil)
    for y in range(max(0, y0 - 1), min(64, y1 + 3)):
        for x in range(max(0, x0 - 2), min(64, x1 + 3)):
            # ellipse-ish hood
            nx = (x - cx) / max(6, (x1 - x0) / 2 + 2)
            ny = (y - cy) / max(6, (y1 - y0) / 2 + 2)
            if nx * nx + ny * ny <= 1.15:
                hood_mask[y, x] = True
            # rear point
            if x < cx - 2 and abs(y - cy) < 6 and x >= cx - 9:
                hood_mask[y, x] = True

    # paint hood
    for y, x in zip(*np.where(hood_mask)):
        nx = (x - cx) / 8.0
        ny = (y - cy) / 8.0
        # face cavity on front-right
        if nx > 0.15 and abs(ny) < 0.55 and nx < 0.85:
            out[y, x] = HOOD_IN
        elif ny < -0.2:
            out[y, x] = TAN_L
        elif nx < -0.2:
            out[y, x] = TAN_M
        else:
            out[y, x] = TAN_S if ny < 0 else TAN_M

    # cyan eyes in cavity
    eye_x = cx + max(2, (x1 - cx) // 2)
    eye_y = cy - 1
    for ey in (eye_y - 1, eye_y + 1):
        for ex in (eye_x, eye_x + 1):
            if 0 <= ex < 64 and 0 <= ey < 64:
                out[ey, ex] = CYAN

    # hood rim
    for x in range(cx - 1, min(64, eye_x + 2)):
        y = y0 + 1
        if 0 <= y < 64 and hood_mask[y, x]:
            if out[y, x, 0] != CYAN[0]:
                out[y, x] = TAN_L

    return hood_mask


def stamp_badge(out, masks):
    """Blue shoulder badge on upper torso front-ish (viewer side)."""
    torso = masks["torso"]
    ys, xs = np.where(torso)
    if len(xs) == 0:
        return
    # shoulder approx: upper torso, toward horizontal center-front
    y_top = ys.min()
    band = torso & (np.arange(64)[:, None] >= y_top) & (np.arange(64)[:, None] <= y_top + 10)
    bys, bxs = np.where(band)
    if len(bxs) == 0:
        return
    # place badge on mid-right of upper torso (side view shoulder)
    bx = int(np.percentile(bxs, 55))
    by = int(np.percentile(bys, 35))
    for dy in range(-3, 4):
        for dx in range(-3, 4):
            if dx * dx + dy * dy <= 9:
                x, y = bx + dx, by + dy
                if 0 <= x < 64 and 0 <= y < 64 and out[y, x, 3] > 0:
                    out[y, x] = BLUE_D if dx * dx + dy * dy > 4 else BLUE_M
    # white cross highlight
    for dx, dy in ((0, 0), (0, -1), (0, 1), (-1, 0), (1, 0)):
        x, y = bx + dx, by + dy
        if 0 <= x < 64 and 0 <= y < 64:
            out[y, x] = WHITE


def stamp_gun(out, masks):
    gun = masks["gun"]
    ys, xs = np.where(gun)
    if len(xs) == 0:
        # synthesize gun from torso front
        torso = masks["torso"]
        tys, txs = np.where(torso)
        if len(txs) == 0:
            return
        gx = int(txs.max()) + 1
        gy = int(tys.mean()) + 2
        # draw simple two-hand pistol
        for x in range(gx, min(64, gx + 8)):
            out[gy, x] = GUN_M
            out[gy - 1, x] = GUN_L
            out[gy + 1, x] = GUN_D
        for y in range(gy, min(64, gy + 4)):
            out[y, gx + 1] = GUN_D
            out[y, gx + 2] = GUN_M
        # hands
        for hx, hy in ((gx, gy + 2), (gx + 3, gy + 2)):
            if 0 <= hx < 64 and 0 <= hy < 64:
                out[hy, hx] = TAN_D
                if hx + 1 < 64:
                    out[hy, hx + 1] = TAN_M
        return

    cx, cy = int(xs.mean()), int(ys.mean())
    # paint gun body from mask
    for y, x in zip(ys, xs):
        if y <= cy:
            out[y, x] = GUN_L if x > cx else GUN_M
        else:
            out[y, x] = GUN_D

    # ensure barrel extends forward a bit
    xmax = int(xs.max())
    for x in range(xmax, min(64, xmax + 3)):
        out[cy, x] = GUN_D
        if cy - 1 >= 0:
            out[cy - 1, x] = GUN_M

    # two hands near grip (left of gun center)
    for hx, hy in ((cx - 2, cy + 1), (cx, cy + 2), (cx - 1, cy + 2)):
        if 0 <= hx < 64 and 0 <= hy < 64:
            out[hy, hx] = TAN_D
            if out[hy, hx - 1 if hx else 0, 3] > 0 or True:
                if hx > 0:
                    out[hy, hx - 1] = TAN_M


def armor_bands(out, mask):
    """Add horizontal armor segmentation."""
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return
    for y in range(ys.min() + 4, ys.max(), 5):
        row = mask[y]
        for x in np.where(row)[0]:
            if out[y, x, 3] > 0 and out[y, x, 0] > 40:  # not outline/gun
                # only if currently tan-ish
                if out[y, x, 2] < 150 and out[y, x, 0] > 80:
                    out[y, x] = TAN_D


def reskin_frame(i: int) -> Image.Image:
    marco = load_marco64(i)
    masks = classify_pixels(marco)
    out = np.zeros((64, 64, 4), dtype=np.uint8)

    # base fill order
    shade_fill(out, masks["pack"], TAN_M, TAN_D, TAN_D, TAN_L)
    shade_fill(out, masks["torso"], TAN_L, TAN_M, TAN_D, TAN_S)
    shade_fill(out, masks["pants"], TAN_L, TAN_M, TAN_D, TAN_S)
    shade_fill(out, masks["boots"], BOOT_L, BOOT, BOOT, BOOT_L)
    # arms were often skin in Marco — treat remaining skin-not-head as arms
    arm = masks["skin"] & ~masks["head"]
    # also some arm pixels classified wrong — use sil pixels that are mid-height and forward
    shade_fill(out, arm, TAN_L, TAN_M, TAN_D, TAN_S)

    # any silhouette still empty: fill tan
    empty_sil = masks["sil"] & (out[:, :, 3] == 0)
    shade_fill(out, empty_sil, TAN_L, TAN_M, TAN_D, TAN_S)

    armor_bands(out, masks["torso"] | masks["pants"])

    hood = stamp_hood(out, masks, marco)
    stamp_badge(out, masks)
    stamp_gun(out, masks)

    # rebuild sil including hood expansion
    sil = (out[:, :, 3] > 0) | masks["sil"]
    if hood is not None:
        sil = sil | hood

    # ensure opaque where we painted
    painted = out[:, :, 3] > 0
    sil = sil | painted

    out = add_outline(out, painted)

    # re-apply cyan eyes on top of outline
    # find hood cavity
    for y in range(64):
        for x in range(64):
            if np.array_equal(out[y, x], HOOD_IN):
                # nearby eye candidates
                pass
    # find cyan already placed or place again near hood_in centroid
    hin = np.all(out == HOOD_IN, axis=2)
    ys, xs = np.where(hin)
    if len(xs):
        cx, cy = int(xs.mean()), int(ys.mean())
        for ey in (cy - 1, cy + 1):
            for ex in (cx + 1, cx + 2):
                if 0 <= ex < 64 and 0 <= ey < 64:
                    out[ey, ex] = CYAN

    # ground feet to y=58
    ys, xs = np.where(out[:, :, 3] > 0)
    if len(ys):
        bottom = int(ys.max())
        shift = 58 - bottom
        if shift != 0:
            shifted = np.zeros_like(out)
            if shift > 0:
                shifted[shift:, :] = out[: 64 - shift, :]
            else:
                shifted[: 64 + shift, :] = out[-shift:, :]
            out = shifted

    return Image.fromarray(out)


def make_sheet(frames, cols=10):
    rows = math.ceil(len(frames) / cols)
    sheet = Image.new("RGBA", (64 * cols, 64 * rows), (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        r, c = divmod(i, cols)
        sheet.paste(fr, (c * 64, r * 64), fr)
    return sheet


def export_gif(frames, path):
    seq = []
    for fr in frames:
        bg = Image.new("RGBA", (64, 64), (24, 24, 28, 255))
        bg.paste(fr, (0, 0), fr)
        seq.append(bg.convert("P", palette=Image.ADAPTIVE, colors=48))
    durations = [70] * 12 + [90] * 8
    seq[0].save(path, save_all=True, append_images=seq[1:], duration=durations, loop=0, disposal=2)


def main():
    FRAMES_DIR.mkdir(exist_ok=True)
    frames = []
    paths = []
    for i in range(20):
        im = reskin_frame(i)
        p = FRAMES_DIR / f"run_stop_{i:02d}.png"
        im.save(p)
        frames.append(im)
        paths.append(p)
        print("wrote", p.name)

    sheet = make_sheet(frames)
    sheet.save(ROOT / "run_stop_sheet.png")
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(ROOT / "run_stop_sheet_x4.png")
    export_gif(frames, ROOT / "run_stop_preview.gif")
    print("sheet+gif done")

    if ASEPRITE.exists():
        r = subprocess.run(
            [str(ASEPRITE), "-b", *[str(p) for p in paths], "--save-as", str(ROOT / "character_run_stop.aseprite")],
            capture_output=True,
            text=True,
        )
        print("aseprite", r.returncode)


if __name__ == "__main__":
    main()
