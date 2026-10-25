"""Assemble the README walkthrough from screenshots of the running Windows app.

Requires Pillow. See README.md in this directory for capture and refresh notes.
"""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


STEPS = [
    ("01-open.png", "Start with a screenshot", "Capture a region, open an image, or paste from your clipboard."),
    ("02-arrow.png", "Point to what matters", "Add an arrow and choose its color and stroke width."),
    ("03-note.png", "Add a clear note", "Use text to explain the change you have in mind."),
    ("04-adjust.png", "Keep every edit adjustable", "Move your note into place. Its text and style stay editable."),
    ("05-share.png", "Ready to share", "Copy the result, or export PNG, JPEG, or PDF."),
]
WIDTH = 1200
FOOTER_HEIGHT = 100
FRAMES_PER_STEP = 10
FRAME_DURATION_MS = 250


def compose(screenshot, index, progress, poster=False):
    height = round(screenshot.height * WIDTH / screenshot.width)
    canvas = Image.new("RGB", (WIDTH, height + FOOTER_HEIGHT), "#171a20")
    canvas.paste(screenshot.resize((WIDTH, height), Image.Resampling.LANCZOS))
    draw = ImageDraw.Draw(canvas)
    font_root = Path("C:/Windows/Fonts")
    heading = ImageFont.truetype(str(font_root / "segoeuib.ttf"), 23)
    detail = ImageFont.truetype(str(font_root / "segoeui.ttf"), 16)
    number = ImageFont.truetype(str(font_root / "segoeuib.ttf"), 19)
    draw.line((0, height, WIDTH, height), fill="#39404d")
    draw.rounded_rectangle((24, height + 19, 65, height + 60), radius=12, fill="#315372")
    draw.text((38, height + 25), str(index + 1), font=number, fill="#c3e5ff")
    title = "Capture. Annotate. Share." if poster else STEPS[index][1]
    subtitle = "SnapStudio for Windows - your images and editable notes stay on your PC." if poster else STEPS[index][2]
    draw.text((82, height + 12), title, font=heading, fill="#f2f5fa")
    draw.text((82, height + 46), subtitle, font=detail, fill="#b1bdce")
    draw.text((WIDTH - 150, height + 25), "SnapStudio", font=heading, fill="#8dcbff")
    for step in range(len(STEPS)):
        x = 25 + step * 232
        y = height + 84
        draw.rounded_rectangle((x, y, x + 220, y + 4), radius=2, fill="#3a424f")
        fill = 1 if step < index else progress if step == index else 0
        if fill > 0:
            draw.rounded_rectangle((x, y, x + max(1, round(220 * fill)), y + 4), radius=2, fill="#8dcbff")
    return canvas


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    screenshots = [Image.open(args.frames_dir / name).convert("RGB") for name, *_ in STEPS]
    if len({image.size for image in screenshots}) != 1:
        raise ValueError("Capture each step at the same window size and display scaling.")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    frames = [compose(image, index, (tick + 1) / FRAMES_PER_STEP)
              for index, image in enumerate(screenshots)
              for tick in range(FRAMES_PER_STEP)]
    # A shared palette keeps stationary UI pixels stable between frames.
    sample = Image.new("RGB", (WIDTH, frames[0].height * len(STEPS)))
    for index in range(len(STEPS)):
        sample.paste(frames[index * FRAMES_PER_STEP], (0, index * frames[0].height))
    palette = sample.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    gif_path = args.output_dir / "snapstudio-demo.gif"
    indexed[0].save(gif_path, save_all=True, append_images=indexed[1:],
                    duration=FRAME_DURATION_MS, loop=0, optimize=True, disposal=1)
    compose(screenshots[-1], len(STEPS) - 1, 1, poster=True).save(args.output_dir / "snapstudio-demo.png", optimize=True)
    print(f"Created {gif_path}: {len(indexed)} frames, {len(indexed) * FRAME_DURATION_MS / 1000:g}s, {gif_path.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
