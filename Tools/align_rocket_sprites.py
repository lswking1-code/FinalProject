"""Align enemy rocket sprite sheets to enemy_gun_idle standard."""
from __future__ import annotations

import re
from pathlib import Path

from PIL import Image

BASE = Path(r"e:\Final Project\FinalProject\Assets\Arts\Enemies")
FRAME_SIZE = 64
GRID = 4
ANCHOR_X = 33
ANCHOR_Y = 50
PIVOT = (0.46875, 0.203125)
ALIGNMENT = 9

FILES = [
    "enemy_rocket_attack.png",
    "enemy_rocket_default.png",
    "enemy_rocket_die.png",
    "enemy_rocket_walk.png",
]

# Unity frame index -> sheet row/col (row 0 is top in image space).
FRAME_LAYOUT = [
    (0, 0),
    (0, 1),
    (0, 2),
    (0, 3),
    (1, 0),
    (1, 1),
    (1, 2),
    (1, 3),
    (2, 0),
    (2, 1),
    (2, 2),
    (2, 3),
    (3, 0),
    (3, 1),
    (3, 2),
    (3, 3),
]

# Unity y positions for each row (bottom-origin).
UNITY_Y = [192, 128, 64, 0]


def content_bbox(image: Image.Image, threshold: int = 10) -> tuple[int, int, int, int] | None:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    min_x, min_y, max_x, max_y = width, height, -1, -1

    for y in range(height):
        for x in range(width):
            red, green, blue, alpha = pixels[x, y]
            if alpha > threshold or (red + green + blue) > threshold:
                min_x = min(min_x, x)
                min_y = min(min_y, y)
                max_x = max(max_x, x)
                max_y = max(max_y, y)

    if max_x < 0:
        return None

    return min_x, min_y, max_x + 1, max_y + 1


def align_frame(frame: Image.Image) -> Image.Image:
    bbox = content_bbox(frame)
    if bbox is None:
        return Image.new("RGBA", (FRAME_SIZE, FRAME_SIZE), (0, 0, 0, 0))

    min_x, min_y, max_x, max_y = bbox
    content = frame.crop(bbox)
    content_w = max_x - min_x
    content_h = max_y - min_y

    anchor_src_x = (min_x + max_x - 1) // 2
    anchor_src_y = max_y - 1

    offset_x = ANCHOR_X - anchor_src_x
    offset_y = ANCHOR_Y - anchor_src_y

    paste_x = min_x + offset_x
    paste_y = min_y + offset_y

    if paste_x + content_w > FRAME_SIZE:
        paste_x = FRAME_SIZE - content_w
    if paste_x < 0:
        paste_x = 0
    if paste_y + content_h > FRAME_SIZE:
        paste_y = FRAME_SIZE - content_h
    if paste_y < 0:
        paste_y = 0

    aligned = Image.new("RGBA", (FRAME_SIZE, FRAME_SIZE), (0, 0, 0, 0))
    aligned.paste(content, (paste_x, paste_y), content)
    return aligned


def process_image(path: Path) -> None:
    source = Image.open(path).convert("RGBA")
    output = Image.new("RGBA", source.size, (0, 0, 0, 0))

    for index, (row, col) in enumerate(FRAME_LAYOUT):
        x0 = col * FRAME_SIZE
        y0 = row * FRAME_SIZE
        frame = source.crop((x0, y0, x0 + FRAME_SIZE, y0 + FRAME_SIZE))
        aligned = align_frame(frame)
        output.paste(aligned, (x0, y0))

    output.save(path)
    print(f"aligned {path.name}")


def parse_sprite_entries(meta_path: Path, prefix: str) -> list[dict[str, str]]:
    text = meta_path.read_text(encoding="utf-8")
    pattern = re.compile(
        rf"name: ({re.escape(prefix)}_\d+)\n"
        r"      rect:\n"
        r"        serializedVersion: 2\n"
        r"        x: (-?\d+)\n"
        r"        y: (-?\d+)\n"
        r"        width: (-?\d+)\n"
        r"        height: (-?\d+)\n"
        r"      alignment: (-?\d+)\n"
        r"      pivot: \{x: ([^,]+), y: ([^}]+)\}\n"
        r"      border: \{x: 0, y: 0, z: 0, w: 0\}\n"
        r"      customData: \n"
        r"      outline: \[\]\n"
        r"      physicsShape: \[\]\n"
        r"      tessellationDetail: -1\n"
        r"      bones: \[\]\n"
        r"      spriteID: (\w+)\n"
        r"      internalID: (-?\d+)",
        re.MULTILINE,
    )

    entries = []
    for match in pattern.finditer(text):
        name = match.group(1)
        frame_index = int(name.rsplit("_", 1)[-1])
        if frame_index > 15:
            continue
        entries.append(
            {
                "name": name,
                "index": frame_index,
                "sprite_id": match.group(9),
                "internal_id": match.group(10),
            }
        )

    entries.sort(key=lambda item: item["index"])
    return entries[:16]


def build_sprite_block(entry: dict[str, str], row: int, col: int) -> str:
    unity_y = UNITY_Y[row]
    unity_x = col * FRAME_SIZE
    return f"""    - serializedVersion: 2
      name: {entry['name']}
      rect:
        serializedVersion: 2
        x: {unity_x}
        y: {unity_y}
        width: {FRAME_SIZE}
        height: {FRAME_SIZE}
      alignment: {ALIGNMENT}
      pivot: {{x: {PIVOT[0]}, y: {PIVOT[1]}}}
      border: {{x: 0, y: 0, z: 0, w: 0}}
      customData: 
      outline: []
      physicsShape: []
      tessellationDetail: -1
      bones: []
      spriteID: {entry['sprite_id']}
      internalID: {entry['internal_id']}
      vertices: []
      indices: 
      edges: []
      weights: []"""


def update_meta(meta_path: Path, prefix: str) -> None:
    text = meta_path.read_text(encoding="utf-8")
    entries = parse_sprite_entries(meta_path, prefix)
    if len(entries) != 16:
        raise RuntimeError(f"{meta_path.name}: expected 16 sprite entries, got {len(entries)}")

    sprite_blocks = []
    for index, entry in enumerate(entries):
        row, col = FRAME_LAYOUT[index]
        sprite_blocks.append(build_sprite_block(entry, row, col))

    internal_table = []
    for entry in entries:
        internal_table.append(
            f"  - first:\n      213: {entry['internal_id']}\n    second: {entry['name']}"
        )

    name_file_id_lines = []
    for entry in sorted(entries, key=lambda item: item["name"]):
        name_file_id_lines.append(f"      {entry['name']}: {entry['internal_id']}")

    sprite_sheet = (
        "  spriteSheet:\n"
        "    serializedVersion: 2\n"
        "    sprites:\n"
        + "\n".join(sprite_blocks)
        + "\n    outline: []\n"
        "    customData: \n"
        "    physicsShape: []\n"
        "    bones: []\n"
        "    spriteID: \n"
        "    internalID: 0\n"
        "    vertices: []\n"
        "    indices: \n"
        "    edges: []\n"
        "    weights: []\n"
        "    secondaryTextures: []\n"
        "    spriteCustomMetadata:\n"
        "      entries: []\n"
        "    nameFileIdTable:\n"
        + "\n".join(name_file_id_lines)
    )

    text = re.sub(
        r"  internalIDToNameTable:\n(?:  - first:\n      213: -?\d+\n    second: .+\n)+",
        "  internalIDToNameTable:\n" + "\n".join(internal_table) + "\n",
        text,
        count=1,
    )
    text = re.sub(
        r"  spriteSheet:\n    serializedVersion: 2\n    sprites:\n(?:    - serializedVersion: 2\n(?:      .+\n)+?      weights: \[\]\n)+    outline: \[\]\n    customData: \n    physicsShape: \[\]\n    bones: \[\]\n    spriteID: \n    internalID: 0\n    vertices: \[\]\n    indices: \n    edges: \[\]\n    weights: \[\]\n    secondaryTextures: \[\]\n    spriteCustomMetadata:\n      entries: \[\]\n    nameFileIdTable:\n(?:      .+\n)+",
        sprite_sheet,
        text,
        count=1,
    )

    meta_path.write_text(text, encoding="utf-8")
    print(f"updated {meta_path.name}")


def main() -> None:
    for filename in FILES:
        image_path = BASE / filename
        meta_path = BASE / f"{filename}.meta"
        prefix = filename.replace(".png", "")
        process_image(image_path)
        update_meta(meta_path, prefix)


if __name__ == "__main__":
    main()
