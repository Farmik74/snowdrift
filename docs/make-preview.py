"""Превью Snowdrift 1280x640: рабочий стол со снегом, окнами, сугробом, трактором и снеговиком."""
import math, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 2                      # рисуем вдвое крупнее, потом уменьшаем — сглаживание
W, H = 1280, 640
random.seed(7)

def P(v): return int(round(v * S))

img = Image.new("RGB", (P(W), P(H)))
d = ImageDraw.Draw(img)

# --- обои: зимний вечер ---
top, mid, bot = (14, 24, 54), (38, 58, 110), (92, 112, 160)
for y in range(P(H)):
    t = y / P(H)
    if t < 0.6:
        k = t / 0.6; c = [int(top[i] + (mid[i] - top[i]) * k) for i in range(3)]
    else:
        k = (t - 0.6) / 0.4; c = [int(mid[i] + (bot[i] - mid[i]) * k) for i in range(3)]
    d.line([(0, y), (P(W), y)], fill=tuple(c))

# луна и свечение
glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
mx, my = 1150, 90
for r in range(120, 0, -4):
    a = int(40 * (1 - r / 120) ** 2)
    gd.ellipse([P(mx - r), P(my - r), P(mx + r), P(my + r)], fill=(200, 220, 255, a))
gd.ellipse([P(mx - 26), P(my - 26), P(mx + 26), P(my + 26)], fill=(245, 248, 255, 255))
img.paste(glow, (0, 0), glow)

# далёкие заснеженные холмы
hill = Image.new("RGBA", img.size, (0, 0, 0, 0))
hd = ImageDraw.Draw(hill)
for base, amp, col, ph in [(500, 40, (70, 88, 135, 255), 0.3), (540, 30, (110, 128, 170, 255), 1.7)]:
    pts = [(0, P(H))]
    for x in range(0, W + 10, 10):
        y = base - amp * (0.6 * math.sin(x / 170 + ph) + 0.4 * math.sin(x / 63 + ph * 2))
        pts.append((P(x), P(y)))
    pts.append((P(W), P(H)))
    hd.polygon(pts, fill=col)
img.paste(hill, (0, 0), hill)

# --- снежинки (за окнами их не видно, поэтому рисуем до окон) ---
def flakes(n, rmin, rmax, amin, amax, blur):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(lay)
    for _ in range(n):
        x, y = random.uniform(0, W), random.uniform(0, 590)
        r = random.uniform(rmin, rmax)
        a = int(random.uniform(amin, amax))
        ld.ellipse([P(x - r), P(y - r), P(x + r), P(y + r)], fill=(255, 255, 255, a))
    if blur: lay = lay.filter(ImageFilter.GaussianBlur(P(blur)))
    img.paste(lay, (0, 0), lay)

flakes(900, 0.8, 1.6, 90, 160, 0.4)    # дальние
flakes(380, 1.6, 2.8, 170, 230, 0.6)   # средние
flakes(90, 3.0, 4.6, 200, 255, 1.0)    # ближние

# --- сугроб-шапка на кромке окна (обтекает скруглённые углы) ---
def snow_cap(x0, x1, y, hmax, seed, r=10):
    rnd = random.Random(seed)
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(lay)
    pts = []
    ph1, ph2 = rnd.uniform(0, 6), rnd.uniform(0, 6)
    xs = list(range(int(x0), int(x1) + 1, 2))
    for x in xs:
        u = (x - x0) / (x1 - x0)
        h = hmax * (0.75 + 0.18 * math.sin(u * 9 + ph1) + 0.1 * math.sin(u * 23 + ph2))
        # у краёв снег сползает по скруглению угла
        edge = min(x - x0, x1 - x)
        if edge < r:
            h = h * (0.55 + 0.45 * edge / r) + (r - edge) * 0.25
        pts.append((P(x), P(y - h)))
    # спуск по боковинам угла
    poly = [(P(x0 - 1.5), P(y + r * 0.7))] + pts + [(P(x1 + 1.5), P(y + r * 0.7))]
    poly += [(P(x1), P(y + 2)), (P(x0), P(y + 2))]
    ld.polygon(poly, fill=(250, 252, 255, 255))
    # тень снизу шапки
    ld.line(pts, fill=(255, 255, 255, 255), width=P(1.2))
    lay2 = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay2).polygon([(P(x0), P(y + 1)), (P(x1), P(y + 1)), (P(x1), P(y + 4)), (P(x0), P(y + 4))], fill=(190, 205, 230, 160))
    img.paste(lay2, (0, 0), lay2)
    img.paste(lay, (0, 0), lay)

def window(x0, y0, x1, y1, title, kind):
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([P(x0 + 4), P(y0 + 10), P(x1 + 4), P(y1 + 14)], P(10), fill=(0, 0, 20, 120))
    sh = sh.filter(ImageFilter.GaussianBlur(P(12)))
    img.paste(sh, (0, 0), sh)
    d.rounded_rectangle([P(x0), P(y0), P(x1), P(y1)], P(10), fill=(250, 251, 253), outline=(200, 205, 215), width=P(1))
    d.rounded_rectangle([P(x0), P(y0), P(x1), P(y0 + 34)], P(10), fill=(236, 239, 245))
    d.rectangle([P(x0), P(y0 + 20), P(x1), P(y0 + 34)], fill=(236, 239, 245))
    f = ImageFont.truetype("DejaVuSans.ttf", P(13))
    d.text((P(x0 + 14), P(y0 + 9)), title, font=f, fill=(60, 66, 80))
    for i, cx in enumerate([x1 - 92, x1 - 58, x1 - 24]):
        if i == 0: d.line([P(cx - 6), P(y0 + 17), P(cx + 6), P(y0 + 17)], fill=(90, 95, 110), width=P(1.3))
        elif i == 1: d.rectangle([P(cx - 6), P(y0 + 11), P(cx + 6), P(y0 + 23)], outline=(90, 95, 110), width=P(1.3))
        else:
            d.line([P(cx - 6), P(y0 + 11), P(cx + 6), P(y0 + 23)], fill=(90, 95, 110), width=P(1.3))
            d.line([P(cx - 6), P(y0 + 23), P(cx + 6), P(y0 + 11)], fill=(90, 95, 110), width=P(1.3))
    if kind == "doc":
        y = y0 + 60
        d.rounded_rectangle([P(x0 + 30), P(y), P(x0 + 260), P(y + 16)], P(4), fill=(52, 92, 170))
        y += 36
        rnd = random.Random(3)
        while y < y1 - 30:
            d.rounded_rectangle([P(x0 + 30), P(y), P(x1 - 30 - rnd.uniform(0, 120)), P(y + 8)], P(4), fill=(200, 205, 216))
            y += 22
    else:  # таблица
        y = y0 + 52
        for r in range(9):
            for c in range(4):
                cx = x0 + 20 + c * (x1 - x0 - 40) / 4
                cw = (x1 - x0 - 40) / 4 - 6
                col = (52, 140, 90) if r == 0 else ((228, 232, 240) if r % 2 else (240, 243, 248))
                d.rectangle([P(cx), P(y), P(cx + cw), P(y + 18)], fill=col)
            y += 22

# окно сзади (таблица), потом окно спереди (документ)
window(860, 240, 1225, 520, "Отчёт за квартал.xlsx", "table")
snow_cap(1022, 1225, 240, 9, 11)      # видимая часть кромки — справа от переднего окна
window(560, 150, 1020, 470, "Письмо.docx", "doc")
snow_cap(560, 1020, 150, 10, 5)

# --- панель задач ---
TB = 592
tb = Image.new("RGBA", img.size, (0, 0, 0, 0))
td = ImageDraw.Draw(tb)
td.rectangle([0, P(TB), P(W), P(H)], fill=(24, 28, 40, 240))
td.line([0, P(TB), P(W), P(TB)], fill=(70, 78, 100, 255), width=P(1))
icols = [(90, 160, 255), (255, 196, 60), (60, 180, 110), (230, 90, 80), (150, 110, 230), (80, 200, 220)]
n = len(icols); x = W / 2 - n * 24
for c in icols:
    td.rounded_rectangle([P(x + 8), P(TB + 12), P(x + 32), P(TB + 36)], P(6), fill=c + (255,))
    x += 48
fs = ImageFont.truetype("DejaVuSans.ttf", P(12))
td.text((P(W - 70), P(TB + 10)), "18:30", font=fs, fill=(230, 235, 245, 255))
td.text((P(W - 78), P(TB + 26)), "15.12.2026", font=fs, fill=(190, 198, 215, 255))
img.paste(tb, (0, 0), tb)

# --- сугроб на панели: слева трактор уже почистил, справа высокий ---
TX = 470   # отвал трактора (едет вправо)
def drift_h(x):
    if x < TX - 175: return 3 + 1.5 * math.sin(x / 13)
    if x < TX: return 0
    base = 26 + 6 * math.sin(x / 70) + 3 * math.sin(x / 23 + 1)
    if x > W - 30: base *= max(0, (W - x) / 30)
    return base
lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
ld = ImageDraw.Draw(lay)
pts = [(P(x), P(TB - drift_h(x))) for x in range(0, W + 1, 2)]
ld.polygon([(0, P(TB + 2))] + pts + [(P(W), P(TB + 2))], fill=(248, 251, 255, 255))
ld.line(pts, fill=(255, 255, 255, 255), width=P(1.5))
img.paste(lay, (0, 0), lay)
shade = Image.new("RGBA", img.size, (0, 0, 0, 0))
sd = ImageDraw.Draw(shade)
for i in range(12):
    sd.line([0, P(TB - i), P(W), P(TB - i)], fill=(170, 190, 225, int(110 * (1 - i / 12))))
mask = Image.new("L", img.size, 0)
ImageDraw.Draw(mask).polygon([(0, P(TB + 2))] + pts + [(P(W), P(TB + 2))], fill=255)
img.paste(shade, (0, 0), Image.composite(shade, Image.new("RGBA", img.size, (0, 0, 0, 0)), mask))

# вал перед отвалом
mh, mw = 40, 80
lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
ld = ImageDraw.Draw(lay)
mp = []
for i in range(31):
    u = i / 30
    y = mh * math.sqrt(max(0, 1 - u * u)) * (1 + 0.06 * math.sin(u * 17))
    mp.append((P(TX + 4 + u * mw), P(TB - y)))
ld.polygon(mp + [(P(TX + 4 + mw), P(TB)), (P(TX + 4), P(TB))], fill=(255, 255, 255, 255))
ld.line(mp, fill=(205, 218, 240, 255), width=P(1.5))
# летящие комья
for _ in range(14):
    cx, cy, r = TX + random.uniform(40, 120), TB - random.uniform(40, 80), random.uniform(2, 4.5)
    ld.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], fill=(255, 255, 255, 235))
img.paste(lay, (0, 0), lay)

# --- трактор из настоящего спрайта программы ---
sprite = [
    "........................B.........",
    ".......................KBK........",
    "..................KKKKKKKKKKKK....",
    "..................KwwWWWWWWWYK....",
    "..................KwWWWRRWWWYK....",
    "..................KWWWRRRRHWYK....",
    "..............KK..KWWWHHHHWWYK....",
    "..............KK..KWWWPPPPWWYK....",
    "..............KK..KWWWPKPKWWYK....",
    ".........KKKKKKKKKKYYYYYYYYYYKKK..",
    ".........KLLYYYYYYYYYYYYYYYYYYYK..",
    "ss.......KLLYYYYYYYYYYYYYYYYYYYK..",
    "sSs......KYYYYYYYYYYYYYYYYYYYYYK..",
    "sSSs.....KOOOOOOOOOOOOOOOOOOOOOK..",
    "sSSs.GGGGKYYYYYYYYYYYYYYYYYYYYYK..",
    "sSSsGG...KyyyyyyyyyyyyyyyyyyyyyK..",
    "sSSs....KKKKKKKKKKKKKKKKKKKKKKKK..",
    "sSSs...KTtTtTtTtTtTtTtTtTtTtTtTtK.",
    "sSs...KTgGGgKgGGgKgGGgKgGGgKgGGgTK",
    "ss....KTgGGgKgGGgKgGGgKgGGgKgGGgTK",
    ".......KtTtTtTtTtTtTtTtTtTtTtTtTK.",
    "........KKKKKKKKKKKKKKKKKKKKKKKK..",
]
col = {'K': (40, 40, 48), 'Y': (255, 196, 0), 'y': (220, 150, 0), 'O': (230, 100, 20), 'W': (150, 200, 240),
       'w': (220, 240, 255), 'S': (200, 212, 225), 's': (120, 135, 155), 'G': (150, 150, 160), 'g': (90, 90, 100),
       'R': (220, 30, 40), 'H': (255, 255, 255), 'P': (255, 205, 170), 'L': (255, 250, 190), 'B': (255, 140, 0),
       'T': (70, 70, 80), 't': (125, 125, 135)}
px = 5
left = TX - 34 * px
topy = TB - 22 * px
for r, row in enumerate(sprite):
    for c, ch in enumerate(row[::-1]):   # едет вправо — зеркально
        if ch in col:
            d.rectangle([P(left + c * px), P(topy + r * px), P(left + (c + 1) * px) - 1, P(topy + (r + 1) * px) - 1], fill=col[ch])
# мигалка светится
bl = Image.new("RGBA", img.size, (0, 0, 0, 0))
bx = left + (33 - 24) * px + px / 2; by = topy + px / 2
ImageDraw.Draw(bl).ellipse([P(bx - 16), P(by - 16), P(bx + 16), P(by + 16)], fill=(255, 150, 20, 90))
bl = bl.filter(ImageFilter.GaussianBlur(P(6)))
img.paste(bl, (0, 0), bl)
# дымок
sm = Image.new("RGBA", img.size, (0, 0, 0, 0))
smd = ImageDraw.Draw(sm)
pxp = left + (34 - 14.5) * px
for i in range(5):
    r = 4 + i * 2.5
    smd.ellipse([P(pxp - 18 * i * 0.6 - r), P(topy + 30 - 14 * i - r), P(pxp - 18 * i * 0.6 + r), P(topy + 30 - 14 * i + r)], fill=(190, 195, 205, 150 - i * 25))
sm = sm.filter(ImageFilter.GaussianBlur(P(2)))
img.paste(sm, (0, 0), sm)

# --- снеговик на сугробе справа ---
sx = 1135
sb = TB - drift_h(sx) + 6
k = 1.35
r1, r2, r3 = 30 * k, 22.5 * k, 16.9 * k
y1 = sb - r1; y2 = y1 - r1 * 0.82 - r2 * 0.78; y3 = y2 - r2 * 0.82 - r3 * 0.8
def ball(cx, cy, r):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(lay)
    ld.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], fill=(214, 226, 246, 255))
    ld.ellipse([P(cx - r * 0.92), P(cy - r * 0.97), P(cx + r * 0.8), P(cy + r * 0.75)], fill=(250, 252, 255, 255))
    lay2 = lay.filter(ImageFilter.GaussianBlur(P(1.2)))
    img.paste(lay2, (0, 0), lay2)
arm = (105, 68, 38)
d.line([P(sx - r2 * 0.8), P(y2 - r2 * 0.1), P(sx - r2 * 2.2), P(y2 - r2 * 0.9)], fill=arm, width=P(3.5))
d.line([P(sx - r2 * 1.8), P(y2 - r2 * 0.65), P(sx - r2 * 2.1), P(y2 - r2 * 1.25)], fill=arm, width=P(2.5))
d.line([P(sx + r2 * 0.8), P(y2 - r2 * 0.1), P(sx + r2 * 2.1), P(y2 - r2 * 1.1)], fill=arm, width=P(3.5))
d.line([P(sx + r2 * 1.7), P(y2 - r2 * 0.75), P(sx + r2 * 2.2), P(y2 - r2 * 0.6)], fill=arm, width=P(2.5))
ball(sx, y1, r1); ball(sx, y2, r2); ball(sx, y3, r3)
coal = (32, 32, 40)
for yy in (y2 - r2 * 0.3, y2 + r2 * 0.15, y1 - r1 * 0.35):
    d.ellipse([P(sx - 3.2), P(yy - 3.2), P(sx + 3.2), P(yy + 3.2)], fill=coal)
# шарф
d.rounded_rectangle([P(sx - r3 * 1.05), P(y3 + r3 * 0.62), P(sx + r3 * 1.05), P(y3 + r3 * 1.05)], P(5), fill=(205, 40, 50))
d.polygon([(P(sx + r3 * 0.3), P(y3 + r3 * 0.9)), (P(sx + r3 * 0.95), P(y3 + r3 * 0.9)), (P(sx + r3 * 1.15), P(y3 + r3 * 2.1)), (P(sx + r3 * 0.6), P(y3 + r3 * 2.15))], fill=(185, 30, 42))
for i in range(4):
    fx = sx + r3 * (0.66 + i * 0.15)
    d.line([P(fx), P(y3 + r3 * 2.1), P(fx), P(y3 + r3 * 2.35)], fill=(185, 30, 42), width=P(1.5))
# лицо
for ex in (-0.38, 0.30):
    d.ellipse([P(sx + ex * r3 - 2.6), P(y3 - 0.28 * r3 - 2.6), P(sx + ex * r3 + 2.6), P(y3 - 0.28 * r3 + 2.6)], fill=coal)
d.polygon([(P(sx - 1), P(y3 - 0.08 * r3)), (P(sx + r3 * 0.95), P(y3 + 0.05 * r3)), (P(sx - 1), P(y3 + 0.16 * r3))], fill=(240, 120, 30))
for i in range(5):
    a = math.pi * (0.2 + 0.15 * i)
    cx, cy = sx + math.cos(a) * r3 * 0.5 - r3 * 0.05, y3 + math.sin(a) * r3 * 0.45
    d.ellipse([P(cx - 1.7), P(cy - 1.7), P(cx + 1.7), P(cy + 1.7)], fill=coal)
# ведро набекрень
bucket = Image.new("RGBA", (P(60), P(50)), (0, 0, 0, 0))
bd = ImageDraw.Draw(bucket)
bd.polygon([(P(12), P(8)), (P(48), P(8)), (P(53), P(38)), (P(7), P(38))], fill=(95, 120, 160))
bd.polygon([(P(12), P(8)), (P(24), P(8)), (P(21), P(38)), (P(7), P(38))], fill=(125, 150, 190))
bd.rectangle([P(4), P(36), P(56), P(41)], fill=(70, 92, 128))
bd.ellipse([P(13), P(4), P(47), P(12)], fill=(250, 252, 255))
bucket = bucket.rotate(14, resample=Image.BICUBIC, expand=True)
img.paste(bucket, (P(sx - 31), P(y3 - r3 - 33)), bucket)
# сугробик у подошвы
lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
ImageDraw.Draw(lay).ellipse([P(sx - r1 * 1.4), P(sb - 12), P(sx + r1 * 1.4), P(sb + 10)], fill=(250, 252, 255, 255))
img.paste(lay, (0, 0), lay)

# --- надпись ---
panel = Image.new("RGBA", img.size, (0, 0, 0, 0))
pd = ImageDraw.Draw(panel)
pd.rounded_rectangle([P(40), P(60), P(500), P(330)], P(22), fill=(10, 18, 40, 120))
panel = panel.filter(ImageFilter.GaussianBlur(P(1)))
img.paste(panel, (0, 0), panel)
fb = ImageFont.truetype("DejaVuSans-Bold.ttf", P(66))
fm = ImageFont.truetype("DejaVuSans.ttf", P(25))
fsml = ImageFont.truetype("DejaVuSans.ttf", P(18))
d.text((P(70), P(84)), "Snowdrift", font=fb, fill=(255, 255, 255))
d.text((P(74), P(166)), "Сугроб", font=ImageFont.truetype("DejaVuSans-Bold.ttf", P(30)), fill=(160, 200, 255))
d.text((P(74), P(214)), "Снег на рабочем столе Windows", font=fm, fill=(230, 238, 252))
d.text((P(74), P(262)), "Бесплатно  ·  без установки  ·  Windows 10/11", font=fsml, fill=(180, 196, 225))

out = img.resize((W, H), Image.LANCZOS)
out.save(sys.argv[1])
print("ok", sys.argv[1])
