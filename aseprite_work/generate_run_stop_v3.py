#!/usr/bin/env python3
"""
v3: Pose-guided Metal Slug run+stop for hooded tan character.
- Marco frames scaled to ~48px tall inside 64x64 (match character.png scale)
- Clean silhouette fill + stamped hood/badge/gun/hands
- Part-based legs reinforced from pose angles for readable run/stop
"""

from __future__ import annotations

import math
import os
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent
FRAMES_DIR = ROOT / "frames"
REF = ROOT / "ref_frames"
ASEPRITE = Path(r"E:\SteamLibrary\steamapps\common\Aseprite\Aseprite.exe")

OUTLINE = (0x16, 0x11, 0x02, 255)
TAN_L = (0xB2, 0xA3, 0x85, 255)
TAN_M = (0x6A, 0x5E, 0x46, 255)
TAN_D = (0x58, 0x4B, 0x37, 255)
TAN_S = (0xAD, 0x9E, 0x82, 255)
HOOD_IN = (0x13, 0x0F, 0x02, 255)
CYAN = (0x47, 0xD4, 0xF4, 255)
BLUE_D = (0x0D, 0x36, 0xA5, 255)
BLUE_M = (0x11, 0x32, 0x98, 255)
WHITE = (0xF4, 0xED, 0xEC, 255)
GUN_L = (0x7A, 0x7A, 0x82, 255)
GUN_M = (0x3A, 0x3A, 0x42, 255)
GUN_D = (0x18, 0x18, 0x1C, 255)
BOOT = (0x2A, 0x26, 0x1E, 255)
BOOT_L = (0x48, 0x42, 0x36, 255)

GROUND = 58


def rgba(c):
    return np.array(c, dtype=np.uint8)


def new_canvas():
    return np.zeros((64, 64, 4), dtype=np.uint8)


def load_marco_scaled(i: int, target_h: int = 48) -> tuple[np.ndarray, dict]:
    files = [f for f in os.listdir(REF) if f.startswith(f"f{i:02d}_") and f.endswith(".png") and not f.startswith("m64")]
    files = [f for f in files if f.startswith(f"f{i:02d}_")]
    # prefer original crops
    files = sorted([f for f in os.listdir(REF) if f.startswith(f"f{i:02d}_r")])
    im = Image.open(REF / files[0]).convert("RGBA")
    scale = target_h / im.height
    nw, nh = max(1, int(round(im.width * scale))), target_h
    im2 = im.resize((nw, nh), Image.NEAREST)
    a = np.array(im2)
    ys, xs = np.where(a[:, :, 3] > 0)
    canvas = new_canvas()
    ox = (64 - (xs.max() - xs.min() + 1)) // 2 - xs.min()
    oy = GROUND - int(ys.max())
    # paste
    for y in range(nh):
        for x in range(nw):
            if a[y, x, 3] < 128:
                continue
            X, Y = x + ox, y + oy
            if 0 <= X < 64 and 0 <= Y < 64:
                canvas[Y, X] = a[y, x]

    # landmarks from colors
    r, g, b, al = canvas[:, :, 0], canvas[:, :, 1], canvas[:, :, 2], canvas[:, :, 3]
    sil = al > 128
    hair = sil & (r > 170) & (g > 140) & (b < 130)
    red = sil & (r > 140) & (g < 110) & (b < 110)
    gun = sil & (r < 95) & (g < 95) & (b < 95) & ((r.astype(int) + g + b) > 35)
    pants = sil & (g + 15 >= r) & (g > b) & (r < 165) & ~red & ~hair

    def com(m):
        yy, xx = np.where(m)
        if len(xx) == 0:
            return None
        return (float(xx.mean()), float(yy.mean()), int(xx.min()), int(yy.min()), int(xx.max()), int(yy.max()))

    meta = {
        "sil": sil,
        "hair": com(hair),
        "red": com(red),
        "gun": com(gun),
        "pants": com(pants),
        "bbox": com(sil),
    }
    return canvas, meta


# --- pose angles (degrees) tuned to Marco cycle; 0=down for legs ---
# hip_dx shifts mass for skid; lean>0 forward, lean<0 brake backward
POSES = [
    # run 0-11
    dict(lean=26, bob=1, hip_dx=0, fl=(-42, 18, 5), bl=(48, 52, 22), gun_dx=13, gun_dy=3),
    dict(lean=24, bob=2, hip_dx=0, fl=(-28, 28, 8), bl=(58, 68, 32), gun_dx=13, gun_dy=4),
    dict(lean=22, bob=0, hip_dx=0, fl=(-8, 38, 10), bl=(38, 38, 14), gun_dx=14, gun_dy=2),
    dict(lean=20, bob=-1, hip_dx=0, fl=(22, 52, 18), bl=(12, 26, 6), gun_dx=14, gun_dy=1),
    dict(lean=18, bob=-3, hip_dx=0, fl=(44, 18, -10), bl=(-22, 14, 0), gun_dx=15, gun_dy=-1),
    dict(lean=28, bob=3, hip_dx=0, fl=(52, 42, 12), bl=(-48, 24, 5), gun_dx=12, gun_dy=5),
    dict(lean=26, bob=1, hip_dx=0, fl=(48, 52, 22), bl=(-42, 18, 5), gun_dx=13, gun_dy=3),
    dict(lean=24, bob=2, hip_dx=0, fl=(58, 68, 32), bl=(-28, 28, 8), gun_dx=13, gun_dy=4),
    dict(lean=22, bob=0, hip_dx=0, fl=(38, 38, 14), bl=(-8, 38, 10), gun_dx=14, gun_dy=2),
    dict(lean=20, bob=-1, hip_dx=0, fl=(12, 26, 6), bl=(22, 52, 18), gun_dx=14, gun_dy=1),
    dict(lean=18, bob=-3, hip_dx=0, fl=(-22, 14, 0), bl=(44, 18, -10), gun_dx=15, gun_dy=-1),
    dict(lean=28, bob=3, hip_dx=0, fl=(-48, 24, 5), bl=(52, 42, 12), gun_dx=12, gun_dy=5),
    # stop 12-19 — lean back, drop, plant front, recover
    dict(lean=8, bob=0, hip_dx=1, fl=(-22, 4, 0), bl=(42, 42, 10), gun_dx=13, gun_dy=1),
    dict(lean=-10, bob=2, hip_dx=2, fl=(-40, -2, -6), bl=(52, 48, 14), gun_dx=12, gun_dy=2),
    dict(lean=-22, bob=5, hip_dx=4, fl=(-58, -8, -10), bl=(62, 52, 20), gun_dx=10, gun_dy=4),
    dict(lean=-30, bob=7, hip_dx=5, fl=(-68, -12, -12), bl=(68, 48, 22), gun_dx=9, gun_dy=6),
    dict(lean=-32, bob=8, hip_dx=6, fl=(-62, -8, -8), bl=(58, 42, 16), gun_dx=9, gun_dy=7),
    dict(lean=-18, bob=4, hip_dx=3, fl=(-38, 4, -2), bl=(40, 32, 8), gun_dx=11, gun_dy=3),
    dict(lean=-4, bob=1, hip_dx=1, fl=(-18, 12, 0), bl=(24, 24, 4), gun_dx=12, gun_dy=1),
    dict(lean=10, bob=0, hip_dx=0, fl=(-6, 18, 2), bl=(16, 28, 6), gun_dx=13, gun_dy=1),
]


def set_px(a, x, y, c):
    if 0 <= x < 64 and 0 <= y < 64:
        a[y, x] = c


def fill_ellipse(a, cx, cy, rx, ry, c, only_empty=False):
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if x < 0 or y < 0 or x >= 64 or y >= 64:
                continue
            if ((x - cx) / max(rx, 0.1)) ** 2 + ((y - cy) / max(ry, 0.1)) ** 2 <= 1.0:
                if only_empty and a[y, x, 3] > 0:
                    continue
                a[y, x] = c


def fill_rect(a, x0, y0, x1, y1, c):
    for y in range(int(y0), int(y1) + 1):
        for x in range(int(x0), int(x1) + 1):
            set_px(a, x, y, c)


def thick_line(a, x0, y0, x1, y1, c, rad=2):
    steps = max(abs(int(x1 - x0)), abs(int(y1 - y0)), 1)
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        fill_ellipse(a, x, y, rad, rad, c)


def outline(a):
    op = a[:, :, 3] > 0
    out = a.copy()
    for y in range(64):
        for x in range(64):
            if op[y, x]:
                continue
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < 64 and 0 <= nx < 64 and op[ny, nx]:
                    out[y, x] = OUTLINE
                    break
    return out


def draw_limb_capsule(a, x0, y0, x1, y1, rad, light, mid, dark):
    steps = max(abs(int(x1 - x0)), abs(int(y1 - y0)), 1)
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        # shading: top half light
        for yy in range(int(y - rad), int(y + rad) + 1):
            for xx in range(int(x - rad), int(x + rad) + 1):
                if (xx - x) ** 2 + (yy - y) ** 2 <= rad * rad:
                    c = light if yy <= y else (mid if yy <= y + rad * 0.5 else dark)
                    set_px(a, xx, yy, c)


def draw_leg(a, hip, thigh_ang, shin_ang, boot_ang, behind=False):
    # ang: 0 = straight down, + = forward (right)
    th_len, sh_len = 12, 11
    kx = hip[0] + math.sin(math.radians(thigh_ang)) * th_len
    ky = hip[1] + math.cos(math.radians(thigh_ang)) * th_len
    total = thigh_ang + shin_ang
    ax = kx + math.sin(math.radians(total)) * sh_len
    ay = ky + math.cos(math.radians(total)) * sh_len

    # if behind, draw darker
    L, M, D = (TAN_M, TAN_D, TAN_D) if behind else (TAN_L, TAN_M, TAN_D)
    draw_limb_capsule(a, hip[0], hip[1], kx, ky, 3.8, L, M, D)
    draw_limb_capsule(a, kx, ky, ax, ay, 3.1, L, M, D)
    # knee pad
    fill_ellipse(a, kx, ky, 2.5, 2.5, TAN_D if behind else TAN_M)

    # boot
    bdir = math.radians(boot_ang)
    toe_x = ax + math.cos(bdir) * 6
    toe_y = ay + abs(math.sin(bdir)) * 1 + 1
    fill_ellipse(a, ax + 1, ay + 1, 4, 2.5, BOOT_L)
    fill_ellipse(a, (ax + toe_x) / 2 + 1, ay + 2, 5, 2.2, BOOT)
    fill_rect(a, ax - 2, ay + 2, toe_x + 1, ay + 4, BOOT)
    return ax, ay


def draw_character(pose, marco_meta) -> np.ndarray:
    a = new_canvas()
    lean = pose["lean"]
    bob = pose["bob"]
    hip_dx = pose.get("hip_dx", 0)

    # Stable body root near canvas center; marco landmarks nudge gun/head
    hx = 30 + hip_dx
    hy = 36 + bob

    # torso — lean shifts upper mass strongly for skid readability
    lean_x = math.sin(math.radians(lean)) * 6
    lean_y = -math.cos(math.radians(lean)) * 1 + (2 if lean < 0 else 0)
    tx = hx + lean_x
    ty = hy - 10 + lean_y

    # backpack (behind)
    fill_ellipse(a, tx - 8 - lean_x * 0.2, ty + 1, 4.5, 6.5, TAN_M)
    fill_ellipse(a, tx - 8, ty, 3.5, 5.5, TAN_L)
    fill_rect(a, tx - 10, ty - 2, tx - 5, ty + 4, TAN_D)

    # torso body (chunkier)
    fill_ellipse(a, tx, ty, 8, 10, TAN_M)
    fill_ellipse(a, tx + 1, ty - 1, 7, 9, TAN_L)
    fill_ellipse(a, tx + 2, ty - 3, 5, 4, TAN_S)
    # armor bands
    fill_rect(a, tx - 5, ty - 4, tx + 6, ty - 3, TAN_M)
    fill_rect(a, tx - 5, ty, tx + 6, ty + 1, TAN_M)
    fill_rect(a, tx - 6, ty + 5, tx + 5, ty + 7, TAN_D)  # belt
    set_px(a, int(tx), int(ty + 6), BOOT_L)

    # legs (back then front)
    fl, bl = pose["fl"], pose["bl"]
    front_is_right = fl[0] >= bl[0]
    if front_is_right:
        draw_leg(a, (hx - 2, hy + 1), bl[0], bl[1], bl[2], behind=True)
        draw_leg(a, (hx + 3, hy), fl[0], fl[1], fl[2], behind=False)
    else:
        draw_leg(a, (hx - 2, hy + 1), fl[0], fl[1], fl[2], behind=True)
        draw_leg(a, (hx + 3, hy), bl[0], bl[1], bl[2], behind=False)

    # shoulder / head follow lean
    sx = tx + 2
    sy = ty - 4
    hx_h = sx + 3 + lean_x * 0.35
    hy_h = sy - 8

    # soft nudge from marco hair centroid
    if marco_meta["hair"]:
        hx_h = 0.65 * hx_h + 0.35 * marco_meta["hair"][0]
        hy_h = 0.65 * hy_h + 0.35 * marco_meta["hair"][1]

    # hood (larger, more character-like)
    fill_ellipse(a, hx_h - 3, hy_h, 8, 8, TAN_M)
    fill_ellipse(a, hx_h, hy_h - 1, 8, 8, TAN_L)
    fill_ellipse(a, hx_h - 6, hy_h + 1, 5.5, 5.5, TAN_M)  # rear hood
    fill_ellipse(a, hx_h - 1, hy_h - 3, 5, 3, TAN_S)  # top highlight
    # face cavity
    fill_ellipse(a, hx_h + 4, hy_h + 1, 4, 4.5, HOOD_IN)
    # eyes — stacked profile slits
    set_px(a, int(hx_h + 4), int(hy_h), CYAN)
    set_px(a, int(hx_h + 5), int(hy_h), CYAN)
    set_px(a, int(hx_h + 4), int(hy_h + 2), CYAN)
    set_px(a, int(hx_h + 5), int(hy_h + 2), CYAN)
    # chin / collar
    fill_ellipse(a, hx_h + 2, hy_h + 6, 4, 2.5, TAN_M)
    fill_ellipse(a, hx_h + 2, hy_h + 6, 3, 1.5, TAN_L)

    # blue badge on shoulder (viewer side)
    bx, by = int(sx - 2), int(sy + 1)
    fill_ellipse(a, bx, by, 3.5, 3.5, BLUE_D)
    fill_ellipse(a, bx, by, 2.2, 2.2, BLUE_M)
    set_px(a, bx, by, WHITE)
    set_px(a, bx, by - 1, WHITE)
    set_px(a, bx - 1, by, WHITE)
    set_px(a, bx + 1, by, WHITE)

    # arms + two-hand gun
    if marco_meta["gun"]:
        gx = 0.4 * marco_meta["gun"][0] + 0.6 * (sx + pose["gun_dx"])
        gy = 0.4 * marco_meta["gun"][1] + 0.6 * (sy + pose["gun_dy"])
    else:
        gx = sx + pose["gun_dx"]
        gy = sy + pose["gun_dy"]

    # arms (support then main)
    draw_limb_capsule(a, sx - 2, sy + 3, gx - 1, gy + 2, 2.6, TAN_M, TAN_D, TAN_D)
    draw_limb_capsule(a, sx + 3, sy + 1, gx, gy, 2.9, TAN_L, TAN_M, TAN_D)
    # forearm armor bands
    fill_ellipse(a, (sx + gx) / 2 + 1, (sy + gy) / 2, 2, 2, TAN_D)

    # gun — clearer pistol silhouette
    gx, gy = int(gx), int(gy)
    fill_rect(a, gx - 1, gy - 2, gx + 8, gy, GUN_M)
    fill_rect(a, gx - 1, gy - 3, gx + 7, gy - 2, GUN_L)
    fill_rect(a, gx + 8, gy - 2, gx + 11, gy - 1, GUN_D)  # barrel
    fill_rect(a, gx, gy, gx + 3, gy + 4, GUN_D)  # grip
    set_px(a, gx + 2, gy - 2, GUN_D)  # slide notch

    # two hands on grip
    fill_ellipse(a, gx + 1, gy + 2, 2.2, 2.2, TAN_D)
    fill_ellipse(a, gx + 4, gy + 2, 2.2, 2.2, TAN_M)
    set_px(a, gx + 1, gy + 1, TAN_M)
    set_px(a, gx + 4, gy + 1, TAN_L)

    a = outline(a)

    # re-stamp eyes & badge & barrel tip over outline
    set_px(a, int(hx_h + 4), int(hy_h), CYAN)
    set_px(a, int(hx_h + 5), int(hy_h), CYAN)
    set_px(a, int(hx_h + 4), int(hy_h + 2), CYAN)
    set_px(a, int(hx_h + 5), int(hy_h + 2), CYAN)
    set_px(a, bx, by, WHITE)
    set_px(a, gx + 10, gy - 1, GUN_L)

    # ground
    ys, xs = np.where(a[:, :, 3] > 0)
    if len(ys):
        shift = GROUND - int(ys.max())
        if shift:
            s = new_canvas()
            if shift > 0:
                s[shift:, :] = a[: 64 - shift, :]
            else:
                s[: 64 + shift, :] = a[-shift:, :]
            a = s
    return a


def maybe_blend_silhouette(a, marco_sil):
    """Subtle: ensure our opaque roughly covers marco sil for pose read — skip if too different."""
    return a


def export_gif(frames, path):
    palette_colors = [
        (28, 28, 34),
        (0x16, 0x11, 0x02),
        (0xB2, 0xA3, 0x85),
        (0x6A, 0x5E, 0x46),
        (0x58, 0x4B, 0x37),
        (0xAD, 0x9E, 0x82),
        (0x13, 0x0F, 0x02),
        (0x47, 0xD4, 0xF4),
        (0x0D, 0x36, 0xA5),
        (0x11, 0x32, 0x98),
        (0xF4, 0xED, 0xEC),
        (0x7A, 0x7A, 0x82),
        (0x3A, 0x3A, 0x42),
        (0x18, 0x18, 0x1C),
        (0x2A, 0x26, 0x1E),
        (0x48, 0x42, 0x36),
    ]
    while len(palette_colors) < 256:
        palette_colors.append((0, 0, 0))
    pal_img = Image.new("P", (1, 1))
    flat = []
    for c in palette_colors:
        flat.extend(c)
    pal_img.putpalette(flat)

    seq = []
    for fr in frames:
        bg = Image.new("RGBA", (64, 64), (28, 28, 34, 255))
        bg.paste(fr, (0, 0), fr)
        seq.append(bg.convert("RGB").quantize(palette=pal_img, dither=Image.Dither.NONE))
    durs = [70] * 12 + [95] * 8
    seq[0].save(path, save_all=True, append_images=seq[1:], duration=durs, loop=0, disposal=2)


def main():
    FRAMES_DIR.mkdir(exist_ok=True)
    frames = []
    paths = []
    for i in range(20):
        _, meta = load_marco_scaled(i, target_h=48)
        arr = draw_character(POSES[i], meta)
        im = Image.fromarray(arr)
        p = FRAMES_DIR / f"run_stop_{i:02d}.png"
        im.save(p)
        frames.append(im)
        paths.append(p)
        print("wrote", p.name, "pixels", int((arr[:, :, 3] > 0).sum()))

    cols = 10
    sheet = Image.new("RGBA", (64 * cols, 64 * 2), (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        r, c = divmod(i, cols)
        sheet.paste(fr, (c * 64, r * 64), fr)
    sheet.save(ROOT / "run_stop_sheet.png")
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(ROOT / "run_stop_sheet_x4.png")
    export_gif(frames, ROOT / "run_stop_preview.gif")

    # QC strips
    qc = Image.new("RGBA", (64 * 5 * 4, 64 * 4), (30, 30, 36, 255))
    for j, i in enumerate([0, 4, 8, 12, 16]):
        fr = frames[i].resize((256, 256), Image.NEAREST)
        bg = Image.new("RGBA", (256, 256), (30, 30, 36, 255))
        bg.paste(fr, (0, 0), fr)
        qc.paste(bg, (j * 256, 0))
    for j, i in enumerate([1, 5, 10, 15, 19]):
        fr = frames[i].resize((256, 256), Image.NEAREST)
        bg = Image.new("RGBA", (256, 256), (30, 30, 36, 255))
        bg.paste(fr, (0, 0), fr)
        qc.paste(bg, (j * 256, 256))
    qc.save(ROOT / "qc_strip.png")

    if ASEPRITE.exists():
        r = subprocess.run(
            [str(ASEPRITE), "-b", *[str(p) for p in paths], "--save-as", str(ROOT / "character_run_stop.aseprite")],
            capture_output=True,
            text=True,
        )
        print("aseprite", r.returncode)
    print("done")


if __name__ == "__main__":
    main()
