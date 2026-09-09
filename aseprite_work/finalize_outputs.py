#!/usr/bin/env python3
"""Promote polished frames/00-19.png into final deliverables."""

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent
FRAMES = ROOT / "frames"
W = H = 64


def main():
    frames = []
    for i in range(20):
        src = FRAMES / f"{i:02d}.png"
        if not src.exists():
            raise SystemExit(f"missing {src}")
        im = Image.open(src).convert("RGBA")
        if im.size != (W, H):
            # content-aware fit into 64x64 grounded
            bbox = im.getbbox()
            if bbox:
                im = im.crop(bbox)
            im = im.resize((W, H), Image.Resampling.NEAREST)
        dst = FRAMES / f"run_stop_{i:02d}.png"
        im.save(dst)
        frames.append(im)
        print("wrote", dst.name, im.size)

    # Marco-like layout: 6 / 6 / 5 / 3
    layout = [6, 6, 5, 3]
    sheet = Image.new("RGBA", (6 * W, 4 * H), (0, 0, 0, 0))
    idx = 0
    for ri, n in enumerate(layout):
        for ci in range(n):
            sheet.paste(frames[idx], (ci * W, ri * H), frames[idx])
            idx += 1
    sheet_path = ROOT / "run_stop_sheet.png"
    sheet.save(sheet_path)
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.Resampling.NEAREST).save(
        ROOT / "run_stop_sheet_x4.png"
    )
    print("wrote", sheet_path.name)

    # Also write compact 10x2 sheet for convenience
    compact = Image.new("RGBA", (10 * W, 2 * H), (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        compact.paste(fr, ((i % 10) * W, (i // 10) * H), fr)
    compact.save(ROOT / "run_stop_64_sheet.png")

    durations = [70] * 12 + [85] * 8
    gif_frames = []
    for fr in frames:
        bg = Image.new("RGBA", (W, H), (18, 18, 22, 255))
        bg.alpha_composite(fr)
        gif_frames.append(
            bg.resize((W * 4, H * 4), Image.Resampling.NEAREST).convert(
                "P", palette=Image.ADAPTIVE, colors=48
            )
        )
    gif_path = ROOT / "run_stop_preview.gif"
    gif_frames[0].save(
        gif_path,
        save_all=True,
        append_images=gif_frames[1:],
        duration=durations,
        loop=0,
    )
    print("wrote", gif_path.name)

    # Aseprite lua
    lines = [
        "local spr = Sprite(64, 64, ColorMode.RGB)",
        "app.activeSprite = spr",
    ]
    for i in range(20):
        posix = str(FRAMES / f"run_stop_{i:02d}.png").replace("\\", "/")
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
    (ROOT / "_export_ase.lua").write_text("\n".join(lines), encoding="utf-8")
    print("wrote _export_ase.lua")


if __name__ == "__main__":
    main()
