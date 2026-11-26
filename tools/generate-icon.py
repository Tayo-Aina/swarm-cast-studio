#!/usr/bin/env python3
"""Generate the Swarm Cast Studio application icon and header mark.

Artwork: a white swarm constellation (one hub node with three satellites)
on a near-black rounded tile. 16 px simplifies to four spaced dots with
shorter, chunkier links so it reads cleanly in the taskbar.

Outputs
-------
src/SwarmCastStudio/Assets/app.ico          (16, 24, 32, 48, 64, 128, 256)
src/SwarmCastStudio/Assets/swarm-mark.png   (256 px, transparent, white glyph)
artifacts/icon-preview-256.png
artifacts/icon-preview-32.png
artifacts/icon-16-zoom.png

Run from the repository root:  python tools/generate-icon.py
"""

from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "SwarmCastStudio" / "Assets"
ARTIFACTS = ROOT / "artifacts"

MASTER = 1024
ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)

# Near-black tile, matching the app theme. No purple anywhere.
TILE_START = (26, 26, 26)  # #1A1A1A
TILE_END = (0, 0, 0)  # #000000
NODE_INK = (255, 255, 255)

EDGE_LARGE = (42, 42, 42)  # #2A2A2A
EDGE_MEDIUM = (60, 60, 60)  # #3C3C3C
EDGE_SMALL = (74, 74, 74)  # #4A4A4A

# Tile-relative geometry: (x, y, radius), y grows downwards.
# Hub + three satellites, balanced but slightly asymmetric.
BIG_NODES = {
    "hub": (0.500, 0.575, 0.072),
    "top": (0.472, 0.245, 0.058),
    "left": (0.225, 0.700, 0.061),
    "right": (0.765, 0.655, 0.052),
}
BIG_EDGES = (("hub", "top"), ("hub", "left"), ("hub", "right"))
BIG_LINE = 0.042
BIG_SHIFT = 0.014

# 32/24 px keep the hub but use fatter lines and larger nodes.
MEDIUM_NODES = {
    "hub": (0.500, 0.580, 0.076),
    "top": (0.478, 0.250, 0.062),
    "left": (0.230, 0.703, 0.064),
    "right": (0.762, 0.660, 0.055),
}
MEDIUM_EDGES = BIG_EDGES
MEDIUM_SHIFT = 0.010

# 16 px: hub kept small, satellites pushed out so four dots stay distinct
# and the hub does not swallow the satellites.
TINY_NODES = {
    "hub": (0.500, 0.564, 0.062),
    "top": (0.500, 0.222, 0.082),
    "left": (0.215, 0.729, 0.085),
    "right": (0.790, 0.679, 0.078),
}
TINY_EDGES = BIG_EDGES
TINY_LINE = 0.055


# ---------------------------------------------------------------------------
# Drawing helpers
# ---------------------------------------------------------------------------


def diagonal_gradient(size: int, start: tuple[int, int, int], end: tuple[int, int, int]) -> Image.Image:
    """Exact per-pixel diagonal gradient with no resize artefacts."""
    steps = 2 * size - 1
    lut = []
    for i in range(steps):
        t = i / (steps - 1)
        lut.append(
            bytes(
                (
                    round(start[0] + (end[0] - start[0]) * t),
                    round(start[1] + (end[1] - start[1]) * t),
                    round(start[2] + (end[2] - start[2]) * t),
                )
            )
        )
    rows = (b"".join(lut[y : y + size]) for y in range(size))
    return Image.frombytes("RGB", (size, size), b"".join(rows))


def rounded_tile_mask(size: int, radius_ratio: float) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    radius = max(2, round(size * radius_ratio))
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def edge_mask(
    size: int,
    radius_ratio: float,
    scale: int,
    thickness_px: float,
    inset_px: float,
    fade: bool,
) -> Image.Image:
    """Inset rim measured in *target* pixels, drawn at render resolution."""
    inset = max(1, round(inset_px * scale))
    width = max(1, round(thickness_px * scale))
    radius = max(2, round(size * radius_ratio) - inset)
    shape = Image.new("L", (size, size), 0)
    ImageDraw.Draw(shape).rounded_rectangle(
        (inset, inset, size - 1 - inset, size - 1 - inset),
        radius=radius,
        outline=255,
        width=width,
    )
    if not fade:
        return shape
    vertical = Image.linear_gradient("L").resize((size, size), Image.BILINEAR)
    vertical = vertical.point(lambda v: int(255 - 70 * (v / 255)))
    return ImageChops.multiply(shape, vertical)


def draw_tile(
    size: int,
    scale: int,
    radius_ratio: float,
    edge_color: tuple[int, int, int],
    edge_thickness: float,
    edge_fade: bool,
) -> Image.Image:
    tile = diagonal_gradient(size, TILE_START, TILE_END).convert("RGBA")
    rim_mask = edge_mask(size, radius_ratio, scale, edge_thickness, 0.70, edge_fade)
    rim = Image.new("RGBA", (size, size), edge_color + (0,))
    rim.putalpha(rim_mask)
    tile.alpha_composite(rim)
    return tile


def constellation_mask(
    size: int,
    nodes: dict[str, tuple[float, float, float]],
    edges: tuple[tuple[str, str], ...],
    line_width: float,
    shift_y: float = 0.0,
) -> Image.Image:
    """White-on-transparent mark: thick links drawn first, nodes on top."""
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    points = {name: (x * size, (y + shift_y) * size) for name, (x, y, _) in nodes.items()}
    width = max(1, round(line_width * size))
    for start, end in edges:
        draw.line((points[start], points[end]), fill=255, width=width)
    for name, (_, _, radius) in nodes.items():
        cx, cy = points[name]
        r = radius * size
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    return mask


def _white(size: int) -> Image.Image:
    return Image.new("L", (size, size), 255)


def mark_art(size: int, mask: Image.Image, glow: bool) -> Image.Image:
    """Composite the optional soft white halo and the white mark."""
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if glow:
        blurred = mask.filter(ImageFilter.GaussianBlur(max(1.0, size * 0.012)))
        alpha = blurred.point(lambda v: int(v * 0.22))
        halo = Image.merge("RGBA", (_white(size), _white(size), _white(size), alpha))
        out.alpha_composite(halo)
    ink = Image.new("RGBA", (size, size), NODE_INK + (0,))
    ink.putalpha(mask)
    out.alpha_composite(ink)
    return out


def finish(tile: Image.Image, glyphs: Image.Image, radius_ratio: float) -> Image.Image:
    tile.alpha_composite(glyphs)
    tile.putalpha(rounded_tile_mask(tile.width, radius_ratio))
    return tile


# ---------------------------------------------------------------------------
# Size-specific artwork
# ---------------------------------------------------------------------------


def render_icon(target: int) -> Image.Image:
    """Render one ICO frame at 4x (large) or 8-16x (taskbar sizes)."""
    if target >= 48:
        scale = 4
        nodes, edges, line, shift = BIG_NODES, BIG_EDGES, BIG_LINE, BIG_SHIFT
        radius_ratio = 0.22
        edge_color = EDGE_LARGE
        edge_thickness = 1.25 if target >= 128 else 1.05
        glow = True
    else:
        scale = {32: 8, 24: 16, 16: 16}[target]
        if target == 16:
            nodes, edges, line, shift = TINY_NODES, TINY_EDGES, TINY_LINE, 0.004
            edge_color = EDGE_SMALL
        else:
            nodes, edges, line, shift = MEDIUM_NODES, MEDIUM_EDGES, {32: 0.050, 24: 0.058}[target], MEDIUM_SHIFT
            edge_color = EDGE_SMALL if target == 24 else EDGE_MEDIUM
        radius_ratio = {32: 0.225, 24: 0.235, 16: 0.240}[target]
        edge_thickness = 1.0
        glow = False

    size = target * scale
    mask = constellation_mask(size, nodes, edges, line, shift_y=shift)
    tile = draw_tile(
        size,
        scale,
        radius_ratio,
        edge_color,
        edge_thickness,
        edge_fade=target >= 32,
    )
    art = finish(tile, mark_art(size, mask, glow=glow), radius_ratio=radius_ratio)
    if scale > 1:
        art = art.resize((target, target), Image.LANCZOS)
    return art


def build_frames() -> dict[int, Image.Image]:
    return {size: render_icon(size) for size in ICO_SIZES}


def render_header_mark() -> Image.Image:
    """Transparent 256 px white glyph, tight-cropped to the constellation."""
    mask = constellation_mask(MASTER, BIG_NODES, BIG_EDGES, BIG_LINE, shift_y=BIG_SHIFT)
    bbox = mask.getbbox()
    if bbox is None:
        raise RuntimeError("Swarm mark rendered empty")
    left, top, right, bottom = bbox
    side = int(max(right - left, bottom - top) * 1.06)
    cx = (left + right) // 2
    cy = (top + bottom) // 2
    left = min(max(0, cx - side // 2), MASTER - side)
    top = min(max(0, cy - side // 2), MASTER - side)
    cropped = mask.crop((left, top, left + side, top + side))
    ink = Image.new("RGBA", cropped.size, NODE_INK + (0,))
    ink.putalpha(cropped)
    return ink.resize((256, 256), Image.LANCZOS)


# ---------------------------------------------------------------------------
# ICO writer (classic 32-bit BMP/DIB frames for maximum shell/WPF support)
# ---------------------------------------------------------------------------


def _bmp_frame(image: Image.Image) -> bytes:
    width, height = image.size
    pixels = image.convert("RGBA").load()

    rows = []
    for y in range(height - 1, -1, -1):
        row = bytearray()
        for x in range(width):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor_bitmap = b"".join(rows)

    mask_stride = ((width + 31) // 32) * 4
    mask_rows = []
    for y in range(height - 1, -1, -1):
        row = bytearray(mask_stride)
        for x in range(width):
            if pixels[x, y][3] < 128:
                row[x >> 3] |= 0x80 >> (x & 7)
        mask_rows.append(bytes(row))
    and_mask = b"".join(mask_rows)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,
        width,
        height * 2,
        1,
        32,
        0,
        len(xor_bitmap) + len(and_mask),
        0,
        0,
        0,
        0,
    )
    return header + xor_bitmap + and_mask


def write_ico(path: Path, frames: dict[int, Image.Image]) -> None:
    sizes = sorted(frames)
    payloads = [(size, _bmp_frame(frames[size])) for size in sizes]

    count = len(payloads)
    directory = bytearray(struct.pack("<HHH", 0, 1, count))
    offset = 6 + 16 * count
    entries = bytearray()
    blobs = bytearray()
    for size, payload in payloads:
        entries += struct.pack(
            "<BBBBHHII",
            0 if size == 256 else size,
            0 if size == 256 else size,
            0,
            0,
            1,
            32,
            len(payload),
            offset,
        )
        blobs += payload
        offset += len(payload)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bytes(directory + entries + blobs))


# ---------------------------------------------------------------------------
# Previews
# ---------------------------------------------------------------------------


def _caption_font(px: int):
    from PIL import ImageFont

    for candidate in (Path(r"C:\Windows\Fonts\segoeui.ttf"), Path(r"C:\Windows\Fonts\arial.ttf")):
        if candidate.exists():
            return ImageFont.truetype(str(candidate), px)
    return ImageFont.load_default()


def _split_board(width: int, height: int, footer: int) -> Image.Image:
    """Dark/light split so the near-black tile edge can be judged in both."""
    board = Image.new("RGBA", (width, height), (12, 12, 16, 255))
    draw = ImageDraw.Draw(board)
    draw.rectangle((width // 2, 0, width, height - footer), fill=(232, 232, 238, 255))
    draw.rectangle((0, height - footer, width, height), fill=(18, 18, 22, 255))
    return board


def preview_256(frame: Image.Image) -> Image.Image:
    board = _split_board(448, 384, 52)
    draw = ImageDraw.Draw(board)
    board.alpha_composite(frame, ((448 - 256) // 2, 40))
    draw.text((96, 342), "Swarm Cast Studio  ·  256 px", font=_caption_font(15), fill=(214, 214, 222, 255))
    draw.text((300, 346), "app.ico · 1:1", font=_caption_font(11), fill=(128, 128, 140, 255))
    return board


def preview_32(frame: Image.Image) -> Image.Image:
    board = Image.new("RGBA", (352, 224), (24, 24, 28, 255))
    draw = ImageDraw.Draw(board)
    board.alpha_composite(frame.resize((128, 128), Image.NEAREST), (24, 32))
    board.alpha_composite(frame, (184, 32))
    board.alpha_composite(frame.resize((16, 16), Image.NEAREST), (184, 72))
    draw.text((184, 100), "1:1  (32 px)", font=_caption_font(12), fill=(196, 196, 204, 255))
    draw.text((24, 176), "32 px  ·  4× nearest", font=_caption_font(14), fill=(196, 196, 204, 255))
    return board


def preview_16_zoom(frame: Image.Image) -> Image.Image:
    zoom = frame.resize((256, 256), Image.NEAREST)
    board = _split_board(448, 400, 64)
    draw = ImageDraw.Draw(board)
    board.alpha_composite(zoom, ((448 - 256) // 2, 36))
    board.alpha_composite(frame, (356, 348))
    draw.text((96, 346), "16 px  ·  16× nearest", font=_caption_font(15), fill=(214, 214, 222, 255))
    draw.text((96, 368), "shown split on dark / light", font=_caption_font(11), fill=(136, 136, 148, 255))
    return board


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    ARTIFACTS.mkdir(parents=True, exist_ok=True)

    frames = build_frames()
    ico_path = ASSETS / "app.ico"
    write_ico(ico_path, frames)

    mark_path = ASSETS / "swarm-mark.png"
    render_header_mark().save(mark_path, optimize=True)

    preview_256(frames[256]).save(ARTIFACTS / "icon-preview-256.png", optimize=True)
    preview_32(frames[32]).save(ARTIFACTS / "icon-preview-32.png", optimize=True)
    preview_16_zoom(frames[16]).save(ARTIFACTS / "icon-16-zoom.png", optimize=True)

    raw = ico_path.read_bytes()
    count = struct.unpack_from("<H", raw, 4)[0]
    print(f"ico:  {ico_path}  ({len(raw):,} bytes, {count} frames)")
    for index in range(count):
        base = 6 + index * 16
        width, height, _, _, planes, bpp, length, offset = struct.unpack_from("<BBBBHHII", raw, base)
        print(f"  {width or 256:>3}px  BMP  {length:,} bytes")
    print(f"mark: {mark_path}  ({mark_path.stat().st_size:,} bytes)")
    print(f"preview: {ARTIFACTS / 'icon-preview-256.png'}")
    print(f"preview: {ARTIFACTS / 'icon-preview-32.png'}")
    print(f"preview: {ARTIFACTS / 'icon-16-zoom.png'}")


if __name__ == "__main__":
    main()
