from pathlib import Path
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "source" / "rekall-age-key-art.png"
OUT = ROOT / "assets"
FONT_BOLD = Path("C:/Windows/Fonts/seguisb.ttf")
FONT_REGULAR = Path("C:/Windows/Fonts/segoeui.ttf")


def cover(image: Image.Image, size: tuple[int, int], focus_x: float = 0.55) -> Image.Image:
    target_w, target_h = size
    scale = max(target_w / image.width, target_h / image.height)
    resized = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
    left = round((resized.width - target_w) * focus_x)
    top = (resized.height - target_h) // 2
    return resized.crop((left, top, left + target_w, top + target_h))


def shade(image: Image.Image, strength: int = 165) -> Image.Image:
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    width, height = image.size
    for x in range(width):
        alpha = int(strength * (1 - x / max(width - 1, 1)) ** 1.8)
        draw.line((x, 0, x, height), fill=(5, 12, 17, alpha))
    return Image.alpha_composite(image.convert("RGBA"), overlay)


def logo_layer(size: tuple[int, int], anchor: tuple[float, float], scale: float) -> Image.Image:
    width, height = size
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    title_size = max(18, round(height * scale))
    sub_size = max(10, round(title_size * 0.32))
    title_font = ImageFont.truetype(str(FONT_BOLD), title_size)
    sub_font = ImageFont.truetype(str(FONT_REGULAR), sub_size)
    x, y = round(width * anchor[0]), round(height * anchor[1])
    mark = round(title_size * 0.24)
    gap = round(title_size * 0.17)
    draw.polygon(
        [(x, y + title_size // 2), (x + mark, y), (x + mark * 2, y + title_size // 2), (x + mark, y + title_size)],
        fill=(30, 219, 203, 255),
    )
    text_x = x + mark * 2 + gap
    draw.text((text_x + 2, y + 2), "REKALL", font=title_font, fill=(0, 0, 0, 150), stroke_width=max(1, title_size // 45))
    draw.text((text_x, y), "REKALL", font=title_font, fill=(242, 247, 248, 255))
    title_box = draw.textbbox((text_x, y), "REKALL", font=title_font)
    age_x = title_box[2] + gap
    draw.text((age_x, y), "AGE", font=title_font, fill=(255, 125, 91, 255))
    draw.text((text_x + 2, y + title_size + 2), "AGENTIC GAME ENGINE", font=sub_font, fill=(0, 0, 0, 150))
    draw.text((text_x, y + title_size), "AGENTIC GAME ENGINE", font=sub_font, fill=(171, 200, 205, 255))
    return layer


def branded(source: Image.Image, size: tuple[int, int], focus_x: float, scale: float, anchor=(0.06, 0.12)) -> Image.Image:
    image = shade(cover(source, size, focus_x))
    image = Image.alpha_composite(image, logo_layer(size, anchor, scale))
    return image.convert("RGB")


def save_jpg(image: Image.Image, name: str) -> None:
    image.save(OUT / name, quality=94, subsampling=0, optimize=True)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    source = Image.open(SOURCE).convert("RGB")
    source = ImageEnhance.Contrast(source).enhance(1.05)
    source = ImageEnhance.Sharpness(source).enhance(1.12)

    save_jpg(branded(source, (920, 430), 0.48, 0.115), "header_capsule_920x430.jpg")
    save_jpg(branded(source, (462, 174), 0.44, 0.17, (0.045, 0.12)), "small_capsule_462x174.jpg")
    save_jpg(branded(source, (1232, 706), 0.50, 0.105), "main_capsule_1232x706.jpg")
    save_jpg(branded(source, (748, 896), 0.76, 0.075, (0.07, 0.08)), "vertical_capsule_748x896.jpg")
    save_jpg(branded(source, (600, 900), 0.82, 0.073, (0.07, 0.08)), "library_capsule_600x900.jpg")
    save_jpg(branded(source, (920, 430), 0.48, 0.115), "library_header_920x430.jpg")

    cover(source, (3840, 1240), 0.56).save(OUT / "library_hero_3840x1240.jpg", quality=94, subsampling=0, optimize=True)
    cover(source, (1438, 810), 0.54).filter(ImageFilter.GaussianBlur(1.2)).save(
        OUT / "page_background_1438x810.jpg", quality=92, optimize=True
    )

    logo = logo_layer((1280, 360), (0.02, 0.08), 0.32)
    logo.save(OUT / "library_logo_1280x360.png", optimize=True)

    icon = Image.new("RGBA", (256, 256), (7, 16, 22, 255))
    icon_draw = ImageDraw.Draw(icon)
    icon_draw.rounded_rectangle((8, 8, 248, 248), radius=42, outline=(28, 207, 192, 255), width=8)
    icon_draw.polygon([(58, 128), (112, 56), (164, 128), (112, 200)], fill=(28, 207, 192, 255))
    icon_draw.polygon([(116, 128), (168, 74), (218, 128), (168, 182)], fill=(255, 122, 87, 255))
    icon.save(OUT / "shortcut_icon_256.png", optimize=True)
    icon.save(OUT / "shortcut_icon.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    icon.convert("RGB").resize((184, 184), Image.Resampling.LANCZOS).save(
        OUT / "app_icon_184.jpg", quality=95, subsampling=0, optimize=True
    )


if __name__ == "__main__":
    main()
