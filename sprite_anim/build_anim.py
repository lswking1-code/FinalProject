# -*- coding: utf-8 -*-
"""Build Metal Slug-style run/stop animation for Jane (64x64), preserving her style."""
from __future__ import annotations

import math
import os
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(r"D:\Github\FinalProject\sprite_anim")
SRC = ROOT / "jane_idle.png"
OUT_FRAMES = ROOT / "frames"
OUT_SHEET = ROOT / "jane_run_stop_sheet.png"
OUT_GIF = ROOT / "jane_run_stop_preview.gif"

# Palette from source (approx canonical)
OUTLINE = (22, 17, 2, 255)
OUTLINE2 = (19, 15, 2, 255)
TAN_L = (178, 163, 133, 255)
TAN_L2 = (181, 166, 137, 255)
TAN_M = (110, 97, 73, 255)
TAN_M2 = (106, 94, 70, 255)
TAN_D = (88, 75, 55, 255)
TAN_D2 = (79, 66, 30, 255)
CYAN = (71, 212, 244, 255)
CYAN2 = (82, 223, 250, 255)
WHITE = (244, 237, 236, 255)
BLUE = (17, 50, 152, 255)
BLUE2 = (13, 54, 165, 255)
GUN_METAL = (118, 117, 106, 255)
GUN_LIGHT = (171, 163, 142, 255)
GUN_DARK = (60, 50, 32, 255)


def opaque_bbox(im: Image.Image):
    a = im.split()[-1]
    return a.getbbox()


def crop_part(src: Image.Image, box, pad=0) -> Image.Image:
    x0, y0, x1, y1 = box
    x0 = max(0, x0 - pad)
    y0 = max(0, y0 - pad)
    x1 = min(src.width, x1 + pad)
    y1 = min(src.height, y1 + pad)
    return src.crop((x0, y0, x1, y1)).copy()


def rotate(im: Image.Image, angle: float, expand=True) -> Image.Image:
    return im.rotate(angle, resample=Image.NEAREST, expand=expand)


def paste(dst: Image.Image, part: Image.Image, xy, mask=None):
    if part is None:
        return
    dst.alpha_composite(part, dest=(int(xy[0]), int(xy[1])))


def blank(size=(64, 64)) -> Image.Image:
    return Image.new("RGBA", size, (0, 0, 0, 0))


def draw_pistol(facing_right=True) -> Image.Image:
    """Compact side-view pistol matching Jane palette (~14x8)."""
    g = blank((16, 10))
    d = ImageDraw.Draw(g)
    # body
    d.rectangle([2, 2, 12, 5], fill=GUN_METAL)
    d.rectangle([3, 1, 11, 2], fill=GUN_LIGHT)
    d.point([(12, 3), (13, 3), (13, 4)], fill=GUN_METAL)  # muzzle
    d.point([(14, 3)], fill=OUTLINE)
    # slide line
    d.point([(4, 2), (5, 2), (8, 2), (9, 2)], fill=TAN_L)
    # grip
    d.rectangle([4, 5, 7, 8], fill=GUN_DARK)
    d.point([(4, 5), (7, 5), (4, 8), (7, 8)], fill=OUTLINE)
    # outline accents
    for x, y in [(2, 2), (2, 5), (12, 2), (12, 5), (3, 1), (11, 1)]:
        d.point([(x, y)], fill=OUTLINE)
    if not facing_right:
        g = g.transpose(Image.FLIP_LEFT_RIGHT)
    return g


def draw_hands_two_grip() -> Image.Image:
    """Two dark-gloved hands stacked for two-hand pistol grip."""
    h = blank((10, 8))
    d = ImageDraw.Draw(h)
    # rear hand
    d.rectangle([1, 3, 4, 6], fill=OUTLINE)
    d.point([(2, 3), (3, 3)], fill=TAN_D)
    # front hand
    d.rectangle([3, 2, 7, 5], fill=OUTLINE2)
    d.point([(4, 2), (5, 2), (6, 2)], fill=TAN_D2)
    d.point([(4, 5), (5, 5)], fill=TAN_M)
    return h


def extract_parts(src: Image.Image):
    # Source bbox roughly 21,11 - 41,52
    head = crop_part(src, (21, 11, 42, 21), pad=0)  # hood + eyes
    # include a bit of neck
    head_full = crop_part(src, (21, 11, 42, 22), pad=0)

    torso = crop_part(src, (20, 20, 43, 37), pad=0)  # chest/armor/belt
    # Left arm (viewer left = back shoulder with emblem) — keep with torso mostly
    # Front arm for idle is along body; for run we redraw arms

    # Legs: left (back) and right (front) from crotch down
    leg_back = crop_part(src, (20, 36, 32, 53), pad=0)   # left boot column
    leg_front = crop_part(src, (31, 36, 43, 53), pad=0)  # right boot column

    # Full lower body for standing stop end
    legs_both = crop_part(src, (20, 35, 43, 53), pad=0)

    # Upper body without legs for lean poses
    upper = crop_part(src, (20, 11, 43, 37), pad=0)

    return {
        "head": head_full,
        "torso": torso,
        "upper": upper,
        "leg_back": leg_back,
        "leg_front": leg_front,
        "legs_both": legs_both,
        "src": src,
    }


def shade_ground_contact(im: Image.Image):
    """No-op placeholder; keep silhouette clean."""
    return im


def compose_frame(
    parts,
    *,
    lean: float = 0.0,
    bob: int = 0,
    head_dx: int = 0,
    head_dy: int = 0,
    torso_dx: int = 0,
    torso_dy: int = 0,
    leg_back_pose: tuple = (0, 0, 0),  # dx, dy, angle
    leg_front_pose: tuple = (0, 0, 0),
    gun_xy: tuple = (34, 28),
    arm_mode: str = "run",
    use_idle_legs: bool = False,
    body_scale_squash: float = 1.0,
):
    """
    lean: degrees, negative = lean forward (clockwise when facing right visually leans forward)
    Metal Slug facing right: forward lean is rotating clockwise slightly... 
    In image coords, forward lean = rotate body clockwise = negative angle in PIL? 
    PIL rotate is counter-clockwise positive. Forward lean (head toward +x) = clockwise = negative angle.
    """
    canvas = blank()
    cx, cy = 32, 32  # approx center

    # --- legs ---
    if use_idle_legs:
        legs = parts["legs_both"]
        paste(canvas, legs, (20 + torso_dx, 35 + torso_dy + bob))
    else:
        lb = rotate(parts["leg_back"], leg_back_pose[2])
        lf = rotate(parts["leg_front"], leg_front_pose[2])
        # anchor approx original positions
        paste(canvas, lb, (20 + leg_back_pose[0] + torso_dx, 36 + leg_back_pose[1] + bob + torso_dy))
        paste(canvas, lf, (31 + leg_front_pose[0] + torso_dx, 36 + leg_front_pose[1] + bob + torso_dy))

    # --- torso / upper ---
    # Build upper body: head + torso separately for independent bob
    torso = parts["torso"]
    head = parts["head"]

    if abs(lean) > 0.1:
        # rotate around mid-torso
        upper = blank((48, 40))
        paste(upper, torso, (0, 9))
        paste(upper, head, (1 + head_dx, 0 + head_dy))
        # shoulder fill / arm stubs drawn after
        pivoted = rotate(upper, lean)
        # center the rotated upper on body
        ux = 20 + torso_dx - (pivoted.width - upper.width) // 2
        uy = 11 + torso_dy + bob - (pivoted.height - upper.height) // 2
        paste(canvas, pivoted, (ux, uy))
        # gun position also shifted by lean approx
        rad = math.radians(lean)
        gx, gy = gun_xy
        # rotate gun pos around (32, 28)
        px, py = gx - 32, gy - 28
        rx = px * math.cos(rad) - py * math.sin(rad)
        ry = px * math.sin(rad) + py * math.cos(rad)
        gun_pos = (int(32 + rx + torso_dx), int(28 + ry + bob + torso_dy))
    else:
        paste(canvas, torso, (20 + torso_dx, 20 + torso_dy + bob))
        paste(canvas, head, (21 + head_dx + torso_dx, 11 + head_dy + torso_dy + bob))
        gun_pos = (gun_xy[0] + torso_dx, gun_xy[1] + bob + torso_dy)

    # --- arms + gun (two-handed) ---
    gun = draw_pistol()
    hands = draw_hands_two_grip()

    # Simple arm connectors in character style
    arm = blank((20, 14))
    ad = ImageDraw.Draw(arm)
    # forearm bar tan with outline
    for x in range(2, 14):
        ad.point([(x, 5), (x, 6), (x, 7)], fill=TAN_M if x % 2 == 0 else TAN_L)
    for x in range(1, 15):
        ad.point([(x, 4)], fill=OUTLINE)
        ad.point([(x, 8)], fill=OUTLINE)
    # elbow dark
    ad.rectangle([1, 5, 3, 7], fill=TAN_D)

    if arm_mode == "run":
        # arms extended forward holding gun
        paste(canvas, arm, (gun_pos[0] - 12, gun_pos[1] - 2))
        paste(canvas, hands, (gun_pos[0] - 2, gun_pos[1]))
        paste(canvas, gun, gun_pos)
    elif arm_mode == "stop":
        paste(canvas, arm, (gun_pos[0] - 11, gun_pos[1] - 1))
        paste(canvas, hands, (gun_pos[0] - 1, gun_pos[1] + 1))
        paste(canvas, gun, (gun_pos[0], gun_pos[1] + 1))

    # Ensure cyan eyes remain visible: re-stamp from head if needed
    # (already in head paste)

    # Clip to 64x64
    return canvas.crop((0, 0, 64, 64))


def make_run_frames(parts):
    """12-frame Metal Slug-like run cycle, two-hand pistol, forward lean."""
    frames = []
    # Leg cycle key poses (back_dx, back_dy, back_ang, front_dx, front_dy, front_ang, bob, lean)
    # Angles: negative = swing forward-ish visually after composite
    cycle = [
        # 0 contact R forward
        (4, 2, 25, -2, -1, -30, 1, -12),
        (3, 1, 15, -1, 0, -20, 0, -14),
        (1, 0, 5, 1, 1, -5, -1, -16),
        (-1, 1, -10, 3, 2, 15, 0, -14),
        (-3, 2, -25, 4, 1, 28, 1, -12),
        (-4, 1, -35, 3, 0, 35, 2, -10),
        # pass / other side
        (-2, 0, -20, 1, -1, 20, 1, -12),
        (0, -1, -5, -1, 0, 5, 0, -14),
        (2, 0, 10, -3, 1, -15, -1, -16),
        (3, 1, 22, -4, 2, -28, 0, -14),
        (4, 2, 30, -3, 1, -32, 1, -12),
        (5, 1, 28, -2, 0, -25, 2, -11),
    ]
    for i, (bdx, bdy, bang, fdx, fdy, fang, bob, lean) in enumerate(cycle):
        fr = compose_frame(
            parts,
            lean=lean,
            bob=bob,
            head_dx=1,
            head_dy=0,
            torso_dx=0,
            torso_dy=0,
            leg_back_pose=(bdx, bdy, bang),
            leg_front_pose=(fdx, fdy, fang),
            gun_xy=(36, 27),
            arm_mode="run",
        )
        frames.append(fr)
    return frames


def make_stop_frames(parts):
    """8-frame skid/stop from run into ready stance."""
    frames = []
    # progressive upright lean, plant front leg, gather back leg
    keys = [
        # still fast, begin plant
        {"lean": -10, "bob": 2, "lb": (5, 2, 20), "lf": (-1, 1, -25), "gun": (36, 28), "idle": False},
        {"lean": -6, "bob": 3, "lb": (3, 3, 10), "lf": (1, 2, -10), "gun": (35, 29), "idle": False},
        {"lean": -2, "bob": 4, "lb": (1, 3, 0), "lf": (2, 2, 5), "gun": (34, 30), "idle": False},
        {"lean": 2, "bob": 3, "lb": (-1, 2, -8), "lf": (3, 1, 12), "gun": (34, 29), "idle": False},
        {"lean": 4, "bob": 2, "lb": (0, 1, -5), "lf": (2, 0, 8), "gun": (34, 28), "idle": False},
        {"lean": 3, "bob": 1, "lb": (0, 0, 0), "lf": (1, 0, 0), "gun": (34, 27), "idle": False},
        {"lean": 1, "bob": 0, "lb": (0, 0, 0), "lf": (0, 0, 0), "gun": (34, 27), "idle": True},
        {"lean": 0, "bob": 0, "lb": (0, 0, 0), "lf": (0, 0, 0), "gun": (34, 27), "idle": True},
    ]
    for k in keys:
        fr = compose_frame(
            parts,
            lean=k["lean"],
            bob=k["bob"],
            head_dx=0,
            head_dy=0,
            leg_back_pose=k["lb"],
            leg_front_pose=k["lf"],
            gun_xy=k["gun"],
            arm_mode="stop",
            use_idle_legs=k["idle"],
        )
        frames.append(fr)
    return frames


def sheet(frames, cols=6, pad=2, bg=(0, 0, 0, 255)):
    w = frames[0].width
    h = frames[0].height
    rows = math.ceil(len(frames) / cols)
    sheet_im = Image.new("RGBA", (cols * (w + pad) + pad, rows * (h + pad) + pad), bg)
    for i, fr in enumerate(frames):
        r, c = divmod(i, cols)
        # actually c = i % cols, r = i // cols
        r = i // cols
        c = i % cols
        sheet_im.paste(fr, (pad + c * (w + pad), pad + r * (h + pad)), fr)
    return sheet_im


def main():
    OUT_FRAMES.mkdir(parents=True, exist_ok=True)
    src = Image.open(SRC).convert("RGBA")
    parts = extract_parts(src)

    # save parts debug
    for name, im in parts.items():
        if name == "src":
            continue
        im.save(OUT_FRAMES / f"_part_{name}.png")

    run = make_run_frames(parts)
    stop = make_stop_frames(parts)
    all_frames = run + stop

    for i, fr in enumerate(all_frames):
        tag = "run" if i < len(run) else "stop"
        idx = i if i < len(run) else i - len(run)
        fr.save(OUT_FRAMES / f"{tag}_{idx:02d}.png")

    sh = sheet(all_frames, cols=6)
    sh.save(OUT_SHEET)

    # preview gif: run loop then stop once
    preview = [f.copy() for f in run * 2 + stop]
    # scale up for visibility
    preview = [p.resize((256, 256), Image.NEAREST) for p in preview]
    preview[0].save(
        OUT_GIF,
        save_all=True,
        append_images=preview[1:],
        duration=80,
        loop=0,
        disposal=2,
    )
    print(f"Wrote {len(all_frames)} frames, sheet, gif")


if __name__ == "__main__":
    main()
