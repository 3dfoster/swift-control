from collections import Counter
from io import BytesIO
from pathlib import Path
import struct
import sys

from PIL import Image, ImageDraw


SOURCE = Path(r"C:\Program Files\AcerQAAgent\AQAUserPS.exe")
TARGET_WIDTH = int(sys.argv[1]) if len(sys.argv) > 1 else 178
TARGET_HEIGHT = TARGET_WIDTH * 128 // 178
OUTPUT = Path(rf"C:\tmp\acer-osd-contact-{TARGET_WIDTH}.png")
SIGNATURE = b"\x89PNG\r\n\x1a\n"
END = b"IEND\xaeB\x60\x82"


data = SOURCE.read_bytes()
images = []
cursor = 0
while True:
    offset = data.find(SIGNATURE, cursor)
    if offset < 0:
        break
    end = data.find(END, offset)
    if end < 0:
        break
    width, height = struct.unpack(">II", data[offset + 16 : offset + 24])
    images.append((offset, width, height, data[offset : end + len(END)]))
    cursor = end + len(END)

base = [item for item in images if item[1:3] == (TARGET_WIDTH, TARGET_HEIGHT)]
columns = 8
cell_width, cell_height = 194, 154
rows = (len(base) + columns - 1) // columns
sheet = Image.new("RGB", (columns * cell_width, rows * cell_height), "#171a20")
draw = ImageDraw.Draw(sheet)

for index, (offset, width, height, payload) in enumerate(base):
    image = Image.open(BytesIO(payload)).convert("RGBA")
    if image.size != (178, 128):
        image.thumbnail((178, 128), Image.Resampling.LANCZOS)
    x = (index % columns) * cell_width + 8
    y = (index // columns) * cell_height + 5
    tile = Image.new("RGBA", image.size, "#242933")
    tile.alpha_composite(image)
    sheet.paste(tile.convert("RGB"), (x, y))
    draw.text((x, y + 131), f"{index:02d}  0x{offset:x}", fill="#f2f4f8")

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
sheet.save(OUTPUT)
print(f"Found {len(images)} PNGs at {Counter((x[1], x[2]) for x in images)}")
print(f"Wrote {len(base)} base-size images to {OUTPUT}")
