using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Snowdrift
{
    // Пиксельный снегоуборщик с отвалом и водителем в новогодней шапке.
    // Нарисован носом влево, вправо — зеркально. Кадр 0..3: бит 0 — мигалка, бит 1 — траки гусениц.
    internal static class Sprites
    {
        static readonly string[] Tractor =
        {
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
            "sSSs..KTgGGgKgGGgKgGGgKgGGgKgGGgTK",
            "sSSs..KTgGGgKgGGgKgGGgKgGGgKgGGgTK",
            "sSSs...KtTtTtTtTtTtTtTtTtTtTtTtTK.",
            "ssss....KKKKKKKKKKKKKKKKKKKKKKKK.."
        };

        public static readonly int Cols = 34;
        public static readonly int Rows = 22;
        public const float PipeCol = 14.5f; // выхлопная труба
        public const int PipeRow = 6;

        static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
        static readonly Dictionary<int, Bitmap> flakeCache = new Dictionary<int, Bitmap>();

        static Color ColorOf(char c, int frame)
        {
            switch (c)
            {
                case 'K': return Color.FromArgb(255, 40, 40, 48);
                case 'Y': return Color.FromArgb(255, 255, 196, 0);
                case 'y': return Color.FromArgb(255, 220, 150, 0);
                case 'O': return Color.FromArgb(255, 230, 100, 20);
                case 'W': return Color.FromArgb(255, 150, 200, 240);
                case 'w': return Color.FromArgb(255, 220, 240, 255);
                case 'S': return Color.FromArgb(255, 200, 212, 225);
                case 's': return Color.FromArgb(255, 120, 135, 155);
                case 'G': return Color.FromArgb(255, 150, 150, 160);
                case 'g': return Color.FromArgb(255, 90, 90, 100);
                case 'R': return Color.FromArgb(255, 220, 30, 40);
                case 'H': return Color.FromArgb(255, 255, 255, 255);
                case 'P': return Color.FromArgb(255, 255, 205, 170);
                case 'L': return Color.FromArgb(255, 255, 250, 190);
                case 'B': return (frame & 1) == 0 ? Color.FromArgb(255, 255, 140, 0) : Color.FromArgb(255, 150, 70, 10);
                case 'T': return (frame & 2) == 0 ? Color.FromArgb(255, 70, 70, 80) : Color.FromArgb(255, 125, 125, 135);
                case 't': return (frame & 2) == 0 ? Color.FromArgb(255, 125, 125, 135) : Color.FromArgb(255, 70, 70, 80);
                default: return Color.Transparent;
            }
        }

        public static int PixelSize(float scale)
        {
            return Math.Max(4, (int)Math.Round(4f * scale));
        }

        public static Bitmap Get(int p, bool faceRight, int frame)
        {
            int key = (p * 2 + (faceRight ? 1 : 0)) * 4 + frame;
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = new Bitmap(Cols * p, Rows * p, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                for (int y = 0; y < Rows; y++)
                {
                    string row = Tractor[y];
                    for (int x = 0; x < Cols; x++)
                    {
                        char ch = x < row.Length ? row[x] : '.';
                        if (ch == '.') continue;
                        int dx = faceRight ? (Cols - 1 - x) : x;
                        using (var br = new SolidBrush(ColorOf(ch, frame)))
                            g.FillRectangle(br, dx * p, y * p, p, p);
                    }
                }
            }
            cache[key] = b;
            return b;
        }

        // Мягкая снежинка: белый круг с размытым краем. r4 — радиус*4, a — прозрачность 1..16.
        public static Bitmap Flake(int r4, int a)
        {
            int key = r4 * 32 + a;
            Bitmap b;
            if (flakeCache.TryGetValue(key, out b)) return b;
            float r = r4 / 4f;
            int size = (int)Math.Ceiling(r * 2) + 2;
            b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / r;
                    if (d >= 1f) continue;
                    float k = 1f - d * d;
                    k = k * (float)Math.Sqrt(k);
                    int al = (int)(255f * a / 16f * Math.Min(1f, k * 1.6f));
                    if (al <= 0) continue;
                    b.SetPixel(x, y, Color.FromArgb(al, 255, 255, 255));
                }
            flakeCache[key] = b;
            return b;
        }
    }
}
