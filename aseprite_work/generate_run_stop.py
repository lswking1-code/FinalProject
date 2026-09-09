#!/usr/bin/env python3
"""
Polished Metal Slug-style 20-frame run+stop for hooded tan-armor character.
Hand-tuned pose table + part-based pixel drawing at exactly 64x64.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent
FRAMES_DIR = ROOT / "frames"
OUT_SHEET = ROOT / "run_stop_sheet.png"
OUT_GIF = ROOT / "run_stop_preview.gif"
OUT_ASE_SCRIPT = ROOT / "_export_ase.lua"

OUTLINE = (22, 17, 2, 255)
TAN = (178, 163, 133, 255)
MID = (106, 94, 70, 255)
DARK = (88, 75, 55, 255)
DEEP = (48, 42, 28, 255)
CYAN = (71, 212, 244, 255)
BLUE = (13, 54, 165, 255)
BLUE2 = (17, 50, 152, 255)
WHITE = (244, 237, 236, 255)
GUN = (55, 55, 62, 255)
GUN_M = (95, 95, 105, 255)
GUN_L = (140, 140, 150, 255)
SHADOW = (35, 30, 18, 255)

W = H = 64
GROUND = 61

# ---------------------------------------------------------------------------
# Hand-tuned poses matching Marco run(12)+stop(8) silhouettes
# hip angles: 0=down, +forward(+x), -back
# ---------------------------------------------------------------------------

# frame: lean, bob, hip_x, head_dx, head_dy, gun_ang, gun_dx, gun_dy, pack_dy,
#        fl_hip, fl_knee, fl_plant, fl_toe,
#        bl_hip, bl_knee, bl_plant, bl_toe

POSES = [
    # --- RUN 0-11 ---
    # 0 contact R forward
    (20, 1, 30, 1, 0, 10, 11, 2, 0,  42, 22, 1, 0,  -38, 48, 0, 3),
    # 1 pass
    (18, 2, 30, 1, 0, 8, 11, 1, 1,   28, 40, 1, 0,  -20, 70, 0, 4),
    # 2 lift back
    (16, 3, 31, 1, -1, 6, 11, 0, 1,  10, 50, 0, 2,   5, 75, 0, 5),
    # 3 extend L forward
    (18, 2, 30, 1, 0, 8, 11, 1, 0,  -15, 55, 0, 3,  30, 35, 0, 2),
    # 4 full stride L plant
    (22, 0, 29, 2, -1, 12, 12, 3, -1, -42, 40, 0, 4,  48, 18, 1, 0),
    # 5 recover
    (19, 2, 30, 1, 0, 10, 11, 2, 0,  -25, 60, 0, 3,  35, 30, 1, 0),
    # 6 (mirror half) L forward contact
    (20, 1, 28, 1, 0, 10, 11, 2, 0,  -36, 50, 0, 3,  44, 20, 1, 0),
    # 7
    (18, 2, 29, 1, 0, 8, 11, 1, 1,  -18, 72, 0, 4,  26, 42, 1, 0),
    # 8
    (16, 3, 30, 1, -1, 6, 11, 0, 1,   8, 78, 0, 5,   8, 52, 0, 2),
    # 9 R forward extend
    (18, 1, 31, 1, 0, 8, 11, 1, 0,  32, 32, 0, 1,  -12, 58, 0, 3),
    # 10 full stride R
    (22, 0, 32, 2, -1, 12, 12, 3, -1, 50, 16, 1, 0,  -44, 42, 0, 4),
    # 11 recover into loop
    (19, 2, 30, 1, 0, 10, 11, 2, 0,  36, 28, 1, 0,  -28, 58, 0, 3),
    # --- STOP 12-19 ---
    # 12 plant hard
    (14, 0, 29, 1, 0, 8, 10, 2, 0,   40, 25, 1, 0,  -30, 55, 0, 2),
    # 13 weight on front
    (4, 0, 28, 0, 0, 6, 10, 1, 0,    48, 18, 1, 0,  -18, 62, 1, 0),
    # 14 lean back start
    (-10, 1, 27, -1, 1, 3, 9, 0, 1,  55, 12, 1, 0,   -5, 70, 1, 0),
    # 15 max skid
    (-18, 1, 26, -2, 1, 0, 8, -1, 1, 58, 8, 1, 0,     8, 72, 1, 0),
    # 16 skid hold
    (-14, 1, 26, -1, 1, 2, 8, 0, 0,  52, 14, 1, 0,    5, 65, 1, 0),
    # 17 settle
    (-4, 0, 28, 0, 0, 5, 9, 1, 0,    38, 28, 1, 0,   12, 50, 1, 0),
    # 18 ready crouch
    (8, 0, 29, 1, 0, 8, 10, 2, 0,    28, 36, 1, 0,   10, 42, 1, 0),
    # 19 combat ready
    (12, 0, 30, 1, 0, 10, 10, 2, 0,  24, 40, 1, 0,    8, 44, 1, 0),
]


def pose_dict(i: int) -> dict:
    p = POSES[i]
    return {
        "lean": p[0],
        "bob": p[1],
        "hip_x": p[2],
        "head_dx": p[3],
        "head_dy": p[4],
        "gun_ang": p[5],
        "gun_dx": p[6],
        "gun_dy": p[7],
        "pack_dy": p[8],
        "fl": {"hip": p[9], "knee": p[10], "plant": bool(p[11]), "toe": p[12]},
        "bl": {"hip": p[13], "knee": p[14], "plant": bool(p[15]), "toe": p[16]},
    }


def px(img, x, y, c):
    if 0 <= x < W and 0 <= y < H and c[3] > 0:
        img.putpixel((int(x), int(y)), c)


def fill_disk(img, cx, cy, rx, ry, c):
    rx = max(1, int(rx))
    ry = max(1, int(ry))
    for dy in range(-ry, ry + 1):
        for dx in range(-rx, rx + 1):
            if (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.08:
                px(img, cx + dx, cy + dy, c)


def fill_rect(img, x0, y0, x1, y1, c):
    for y in range(int(min(y0, y1)), int(max(y0, y1)) + 1):
        for x in range(int(min(x0, x1)), int(max(x0, x1)) + 1):
            px(img, x, y, c)


def thick_line(img, x0, y0, x1, y1, radius, c):
    steps = max(int(math.hypot(x1 - x0, y1 - y0) * 2.5), 1)
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        fill_disk(img, round(x), round(y), radius, radius, c)


def outline_sprite(img):
    src = img.copy()
    sp = src.load()
    op = img.load()
    for y in range(H):
        for x in range(W):
            if sp[x, y][3] > 0:
                continue
            hit = False
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < W and 0 <= ny < H and sp[nx, ny][3] > 0:
                        hit = True
                        break
                if hit:
                    break
            if hit:
                op[x, y] = OUTLINE


def ang_vec(deg, length):
    rad = math.radians(deg)
    return length * math.sin(rad), length * math.cos(rad)


def draw_boot(img, ankle_x, ankle_y, shin_ang, plant, toe_lift):
    dir_x = 1 if shin_ang > -20 else -1
    bx = int(round(ankle_x))
    if plant:
        by = GROUND - 3
        sole_y = GROUND
    else:
        by = int(round(ankle_y)) - int(toe_lift)
        sole_y = by + 3

    fill_disk(img, bx, by, 4, 3, MID)
    fill_rect(img, bx, by - 1, bx + dir_x * 6, by + 2, TAN)
    fill_rect(img, bx, by + 1, bx + dir_x * 6, by + 3, MID)
    fill_disk(img, bx + dir_x * 4, by + 1, 2, 2, TAN)
    # sole
    fill_rect(img, bx - 2, sole_y - 1, bx + dir_x * 7, sole_y, DEEP if plant else DARK)
    # heel chunk
    fill_disk(img, bx - dir_x, by + 1, 2, 2, DARK)


def draw_leg(img, hip, leg, near=True):
    thigh_len, shin_len = 12, 11
    hip_ang = leg["hip"]
    knee_bend = leg["knee"]

    tx, ty = ang_vec(hip_ang, thigh_len)
    knee = (hip[0] + tx, hip[1] + ty)

    # Shin folds toward ground; more bend => shin more vertical relative to thigh
    if hip_ang >= 0:
        shin_ang = hip_ang - knee_bend * 0.95 + 8
    else:
        shin_ang = hip_ang + knee_bend * 0.9 + 6

    sx, sy = ang_vec(shin_ang, shin_len)
    ankle = (knee[0] + sx, knee[1] + sy)

    if leg["plant"]:
        # Snap foot to ground, keep thigh, pull shin
        ankle = (ankle[0], float(GROUND - 3))
        # prevent overstretch: if too far, pull knee
        dist = math.hypot(ankle[0] - knee[0], ankle[1] - knee[1])
        if dist > shin_len + 2:
            scale = (shin_len + 1) / dist
            knee = (
                ankle[0] + (knee[0] - ankle[0]) * scale,
                ankle[1] + (knee[1] - ankle[1]) * scale,
            )

    col = TAN if near else MID
    shade = MID if near else DARK
    r = 4 if near else 3

    thick_line(img, hip[0], hip[1], knee[0], knee[1], r, col)
    fill_disk(img, int(knee[0]), int(knee[1]), r, r - 1, shade)
    # kneepad
    fill_disk(img, int(knee[0] + (1 if near else 0)), int(knee[1] - 1), 2, 2, TAN if near else MID)
    thick_line(img, knee[0], knee[1], ankle[0], ankle[1], 3, col)
    # shin plate
    mid_sx = (knee[0] + ankle[0]) / 2
    mid_sy = (knee[1] + ankle[1]) / 2
    fill_disk(img, int(mid_sx), int(mid_sy), 2, 2, shade)

    draw_boot(img, ankle[0], ankle[1], shin_ang, leg["plant"], leg["toe"])
    return ankle


def draw_head(img, cx, cy):
    # Rounded hood volume matching character.png
    fill_disk(img, cx, cy, 9, 8, TAN)
    fill_disk(img, cx - 2, cy + 1, 7, 7, MID)
    fill_disk(img, cx + 1, cy - 2, 6, 5, TAN)
    # hood back flap
    fill_disk(img, cx - 6, cy, 4, 5, MID)
    fill_disk(img, cx - 7, cy + 3, 3, 3, DARK)
    # top rim highlight
    fill_rect(img, cx - 2, cy - 7, cx + 4, cy - 5, TAN)
    # face cavity
    fill_rect(img, cx + 1, cy - 2, cx + 7, cy + 4, SHADOW)
    fill_disk(img, cx + 5, cy + 1, 3, 3, SHADOW)
    # cyan eyes — two stacked slits (side-readable)
    for ey in (0, 2):
        px(img, cx + 4, cy + ey, CYAN)
        px(img, cx + 5, cy + ey, CYAN)
        px(img, cx + 6, cy + ey, CYAN)
    # chin / lower hood
    fill_disk(img, cx + 2, cy + 6, 4, 2, MID)
    fill_disk(img, cx + 3, cy + 5, 2, 2, TAN)


def draw_torso(img, hip, lean, pack_dy):
    rad = math.radians(lean)
    spine = 15
    sx = hip[0] + spine * math.sin(rad)
    sy = hip[1] - spine * math.cos(rad)
    shoulder = (sx, sy)
    chest = (sx + 2.5, sy + 5)

    # main body mass
    thick_line(img, hip[0], hip[1] - 1, shoulder[0], shoulder[1] + 3, 7, TAN)
    fill_disk(img, int(chest[0]), int(chest[1]), 8, 7, TAN)
    fill_disk(img, int(hip[0] + 1), int(hip[1] - 5), 6, 5, MID)
    # chest armor plates
    fill_rect(img, int(chest[0] - 3), int(chest[1] - 3), int(chest[0] + 4), int(chest[1]), TAN)
    fill_rect(img, int(chest[0] - 2), int(chest[1]), int(chest[0] + 3), int(chest[1] + 3), MID)
    # strap
    thick_line(img, shoulder[0] - 4, shoulder[1] + 2, hip[0] + 4, hip[1] - 3, 1, DARK)

    # backpack
    bx = int(shoulder[0] - 8)
    by = int(shoulder[1] + 1 + pack_dy)
    fill_rect(img, bx, by, bx + 7, by + 12, MID)
    fill_rect(img, bx + 1, by + 1, bx + 6, by + 5, TAN)
    fill_rect(img, bx + 1, by + 7, bx + 6, by + 11, DARK)
    px(img, bx + 3, by + 3, WHITE)

    # blue shoulder badge
    badge_x = int(shoulder[0] + 4)
    badge_y = int(shoulder[1] + 4)
    fill_disk(img, badge_x, badge_y, 3, 3, BLUE)
    fill_disk(img, badge_x, badge_y, 2, 2, BLUE2)
    px(img, badge_x, badge_y - 1, WHITE)
    px(img, badge_x, badge_y, CYAN)

    # belt
    fill_rect(img, int(hip[0] - 4), int(hip[1] - 2), int(hip[0] + 6), int(hip[1]), DARK)
    fill_rect(img, int(hip[0]), int(hip[1] - 3), int(hip[0] + 3), int(hip[1] - 1), MID)

    return shoulder, chest


def draw_arms_and_gun(img, chest, shoulder, pose):
    gang = math.radians(pose["gun_ang"])
    gx = chest[0] + pose["gun_dx"]
    gy = chest[1] + pose["gun_dy"]

    # far support arm
    far_elbow = (chest[0] + 1, chest[1] + 5)
    thick_line(img, shoulder[0] - 2, shoulder[1] + 5, far_elbow[0], far_elbow[1], 2, MID)
    thick_line(img, far_elbow[0], far_elbow[1], gx - 1, gy + 2, 2, MID)

    # near firing arm
    near_elbow = (chest[0] + 5, chest[1] + 4)
    thick_line(img, shoulder[0] + 3, shoulder[1] + 6, near_elbow[0], near_elbow[1], 3, TAN)
    thick_line(img, near_elbow[0], near_elbow[1], gx, gy, 3, TAN)

    # gauntlets / hands
    fill_disk(img, int(gx), int(gy), 3, 3, MID)
    fill_disk(img, int(gx + 1), int(gy - 1), 2, 2, TAN)
    fill_disk(img, int(gx + 3), int(gy + 2), 2, 2, MID)  # support hand

    # pistol
    bdx = 9 * math.cos(gang)
    bdy = 9 * math.sin(gang)
    thick_line(img, gx + 1, gy - 0.5, gx + 1 + bdx, gy + bdy, 1, GUN)
    thick_line(img, gx + 2, gy - 1.5, gx + 2 + bdx * 0.8, gy - 1.5 + bdy * 0.8, 1, GUN_M)
    fill_rect(img, int(gx - 1), int(gy), int(gx + 2), int(gy + 5), GUN)
    fill_rect(img, int(gx), int(gy + 1), int(gx + 1), int(gy + 4), GUN_L)
    mx, my = int(gx + 1 + bdx), int(gy + bdy)
    px(img, mx, my, GUN_L)
    px(img, mx + 1, my, OUTLINE)


def render_frame(pose) -> Image.Image:
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    lean = pose["lean"]
    bob = pose["bob"]
    hip = (float(pose["hip_x"]), float(GROUND - 24 - bob))

    # back leg
    draw_leg(img, (hip[0] - 2, hip[1]), pose["bl"], near=False)
    shoulder, chest = draw_torso(img, hip, lean, pose["pack_dy"])
    # front leg
    draw_leg(img, (hip[0] + 2, hip[1] + 1), pose["fl"], near=True)
    draw_arms_and_gun(img, chest, shoulder, pose)

    rad = math.radians(lean)
    hx = shoulder[0] + 2.5 * math.sin(rad) + pose["head_dx"]
    hy = shoulder[1] - 8 + pose["head_dy"]
    draw_head(img, int(round(hx)), int(round(hy)))

    outline_sprite(img)
    return img


def make_sheet(frames):
    layout = [6, 6, 5, 3]
    sheet = Image.new("RGBA", (6 * W, 4 * H), (0, 0, 0, 0))
    idx = 0
    for ri, n in enumerate(layout):
        for ci in range(n):
            sheet.paste(frames[idx], (ci * W, ri * H), frames[idx])
            idx += 1
    return sheet


def write_ase_lua(paths):
    lines = [
        "local spr = Sprite(64, 64, ColorMode.RGB)",
        "app.activeSprite = spr",
    ]
    for i, p in enumerate(paths):
        posix = str(p).replace("\\", "/")
        lines.append(f'local src = app.open("{posix}")')
        if i == 0:
            lines += [
                "spr.cels[1].image:clear()",
                "spr.cels[1].image = src.cels[1].image:clone()",
            ]
        else:
            lines += [
                "local fr = spr:newEmptyFrame()",
                "spr:newCel(spr.layers[1], fr, src.cels[1].image:clone(), Point(0,0))",
            ]
        lines.append("src:close()")
    out = str(ROOT / "character_run_stop.aseprite").replace("\\", "/")
    lines.append(f'spr:saveAs("{out}")')
    lines.append("app.exit()")
    OUT_ASE_SCRIPT.write_text("\n".join(lines), encoding="utf-8")


def main():
    FRAMES_DIR.mkdir(exist_ok=True)
    frames, paths = [], []
    for i in range(20):
        fr = render_frame(pose_dict(i))
        path = FRAMES_DIR / f"run_stop_{i:02d}.png"
        fr.save(path)
        frames.append(fr)
        paths.append(path)
        print("wrote", path.name)

    sheet = make_sheet(frames)
    sheet.save(OUT_SHEET)
    print("wrote", OUT_SHEET.name)

    # also save x4 sheet for inspection
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.Resampling.NEAREST).save(
        ROOT / "run_stop_sheet_x4.png"
    )

    durations = [70] * 12 + [85] * 8
    gif_frames = []
    for fr in frames:
        bg = Image.new("RGBA", (W, H), (18, 18, 22, 255))
        bg.alpha_composite(fr)
        gif_frames.append(
            bg.resize((W * 4, H * 4), Image.Resampling.NEAREST).convert(
                "P", palette=Image.ADAPTIVE, colors=64
            )
        )
    gif_frames[0].save(
        OUT_GIF,
        save_all=True,
        append_images=gif_frames[1:],
        duration=durations,
        loop=0,
    )
    print("wrote", OUT_GIF.name)

    write_ase_lua(paths)
    print("wrote", OUT_ASE_SCRIPT.name)


if __name__ == "__main__":
    main()
