#!/usr/bin/env python3
"""Pack the eight original Car Otaku illustrations into the runtime decal atlas."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource" / "CarOtaku"
DESTINATION = ROOT / "Assets" / "MadeInArizona" / "Resources" / "Art" / "OtakuWraps.png"
NAMES = (
    "01-mechanic.png",
    "02-racer.png",
    "03-fast-food-mecha.png",
    "04-magical-tire.png",
    "05-noodle-hero.png",
    "06-fuel-bounty.png",
    "07-parking-ticket.png",
    "08-tire-idol.png",
)
CELL = 512
PADDING = 10


def main() -> None:
    atlas = Image.new("RGBA", (4 * CELL, 2 * CELL), (0, 0, 0, 0))
    for index, name in enumerate(NAMES):
        image = Image.open(SOURCE / name).convert("RGBA")
        bounds = image.getchannel("A").getbbox()
        if bounds is None:
            raise ValueError(f"Empty illustration: {name}")
        image = image.crop(bounds)
        image.thumbnail((CELL - 2 * PADDING, CELL - 2 * PADDING), Image.Resampling.LANCZOS)
        x = (index % 4) * CELL + (CELL - image.width) // 2
        y = (index // 4) * CELL + (CELL - image.height) // 2
        atlas.alpha_composite(image, (x, y))
    atlas.save(DESTINATION, optimize=True)
    print(f"Packed {len(NAMES)} Car Otaku wraps into {DESTINATION}")


if __name__ == "__main__":
    main()
