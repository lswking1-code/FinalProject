from PIL import Image
import os


def analyze(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    regions = {k: [] for k in ["hair", "vest", "pants", "skin", "boot", "gun", "pack"]}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 200 or (r + g + b) < 40:
                continue
            if r > 180 and g > 140 and b < 100:
                regions["hair"].append((x, y))
            elif r > 150 and g < 90 and b < 90:
                regions["vest"].append((x, y))
            elif r > 180 and g > 140 and b > 120 and r - b < 60:
                regions["skin"].append((x, y))
            elif 80 < r < 160 and 90 < g < 150 and 40 < b < 100 and g >= r - 20:
                regions["pants"].append((x, y))
            elif r < 90 and g < 90 and b < 90:
                regions["boot"].append((x, y))
            elif 60 < r < 120 and 60 < g < 120 and 70 < b < 140 and b >= r:
                regions["gun"].append((x, y))
            elif 100 < r < 160 and 110 < g < 170 and 90 < b < 140:
                regions["pack"].append((x, y))
    out = {}
    for k, pts in regions.items():
        if not pts:
            out[k] = None
            continue
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        out[k] = {
            "cx": sum(xs) / len(xs),
            "cy": sum(ys) / len(ys),
            "minx": min(xs),
            "maxx": max(xs),
            "miny": min(ys),
            "maxy": max(ys),
            "n": len(pts),
        }
    allp = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 200 and r + g + b > 40:
                allp.append((x, y))
    xs = [p[0] for p in allp]
    ys = [p[1] for p in allp]
    out["bbox"] = {
        "minx": min(xs),
        "maxx": max(xs),
        "miny": min(ys),
        "maxy": max(ys),
        "w": max(xs) - min(xs) + 1,
        "h": max(ys) - min(ys) + 1,
        "bottom": max(ys),
    }
    return out


def main():
    frames = sorted(os.listdir("ref_frames"))
    for f in frames:
        a = analyze(os.path.join("ref_frames", f))
        hair = a["hair"]
        vest = a["vest"]
        bb = a["bbox"]
        hc = (round(hair["cx"], 1), round(hair["cy"], 1)) if hair else None
        vc = (round(vest["cx"], 1), round(vest["cy"], 1)) if vest else None
        print(
            f"{f}: bbox={bb['w']}x{bb['h']} bot={bb['bottom']} "
            f"head={hc} vest={vc}"
        )


if __name__ == "__main__":
    main()
