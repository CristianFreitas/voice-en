"""Gera icon.ico (multi-tamanho) e icon.png do VoiceEn. Uso: uv run --with pillow python make_icon.py"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

S = 1024  # desenha grande e reduz, para as bordas sairem suaves
SIZES = [16, 24, 32, 48, 64, 128, 256]
TOP, BOTTOM = (124, 92, 255), (59, 107, 255)
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def background():
    gradient = Image.new("RGB", (1, S))
    for y in range(S):
        t = y / (S - 1)
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=224, fill=255)
    image = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    image.paste(gradient.resize((S, S)), (0, 0), mask)
    return image


def draw(badge):
    image = background()
    d = ImageDraw.Draw(image)
    white = (255, 255, 255, 255)
    # capsula do microfone
    d.rounded_rectangle((397, 170, 627, 590), radius=115, fill=white)
    # suporte em U, com as pontas arredondadas
    d.arc((297, 295, 727, 725), start=0, end=180, fill=white, width=56)
    for x in (325, 699):
        d.ellipse((x - 28, 482, x + 28, 538), fill=white)
    # haste e base
    d.rectangle((484, 715, 540, 830), fill=white)
    d.rounded_rectangle((382, 806, 642, 862), radius=28, fill=white)

    if badge and Path(FONT).exists():
        d.rounded_rectangle((610, 640, 960, 850), radius=70, fill=white)
        font = ImageFont.truetype(FONT, 150)
        d.text((785, 745), "EN", font=font, fill=BOTTOM, anchor="mm")
    return image


def main():
    here = Path(__file__).parent
    # o selo "EN" vira borrao abaixo de 48 px, entao os tamanhos pequenos levam so o microfone
    renders = [draw(badge=size >= 48).resize((size, size), Image.LANCZOS) for size in SIZES]
    renders[-1].save(here / "icon.ico", sizes=[(s, s) for s in SIZES], append_images=renders[:-1])
    renders[-1].save(here / "icon.png")


if __name__ == "__main__":
    main()
