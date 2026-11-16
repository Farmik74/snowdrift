using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Snowdrift
{
    internal sealed class ScreenInfo
    {
        public Rectangle Bounds;
        public float Scale;
        public Overlay Overlay;
        public bool Paused;
        public bool Fullscreen; // пауза из-за окна на весь экран
        public Ledge Floor;
        public float SpawnAcc;
    }

    internal sealed class Flake
    {
        public float X, Y, R, Vy, Amp, Freq, Phase, Alpha, Z, S;
        public bool Hidden;
    }

    internal sealed class Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife, R, Gravity;
        public int Gray;
        public bool Settle; // ком снега: падает с ускорением и ложится на панель задач
    }

    // Полка, на которой копится снег: верхняя кромка окна, панель задач или низ экрана.
    internal sealed class Ledge
    {
        public const int Col = 2;
        public IntPtr Hwnd;
        public int Left, Right, Top, Z;
        public bool IsTaskbar;
        public float MaxH;
        public float[] H;
        public bool[] Exposed;
        public bool Seen;
        public float LastTractor;

        public Ledge(int left, int right, int top, float maxH)
        {
            Left = left; Right = right; Top = top; MaxH = maxH;
            int n = Math.Max(1, (right - left) / Col);
            H = new float[n];
            Exposed = new bool[n];
        }

        public int N { get { return H.Length; } }
        public float ColX(int c) { return Left + c * Col + Col * 0.5f; }

        public float Fill()
        {
            float s = 0;
            int cnt = 0;
            for (int i = 0; i < H.Length; i++)
            {
                if (!Exposed[i]) continue;
                s += H[i];
                cnt++;
            }
            return cnt == 0 ? 0 : s / (cnt * MaxH);
        }
    }

    internal sealed class Tractor
    {
        public Ledge L;
        public float X;   // координата отвала
        public int Dir;   // -1 влево, +1 вправо
        public float Scale;
        public float T, PuffAcc;
        public float Carry; // сколько снега толкает перед отвалом
        public bool Heavy;  // снега много: чистит половину, отъезжает и с разгона дочищает
        public int Stage;   // 0 — толкает, 1 — отъезжает назад, 2 — пауза, 3 — разгон
        public float StageT, Speed, Anim;
    }

    internal sealed class Snowman
    {
        public float X;          // доля ширины панели задач, где стоит на земле
        public float Growth;     // 0..1 — снеговик постепенно лепится
        public float T;
        public float JumpT = -1f, JumpDur, JumpH;
        public IntPtr Anchor;    // окно, на котором стоит (Zero — панель задач / земля)
        public Ledge AnchorLedge;
        public float Dx;         // смещение от левого края окна
        public bool Falling, Dragging;
        public float FX, FY, FVy;
        public float DragOffX, DragOffY;
        public float LastX, LastBase;
        public float FVx;                    // скорость броска
        public float DVx, DVy; public int LastDragMs;
        public bool PendingBreak, Broken;
        public float BrokenT;
        public List<Piece> Pieces = new List<Piece>();
    }

    // Часть разлетевшегося снеговика: комья, ведро, морковка, руки.
    internal sealed class Piece
    {
        public int Kind;   // 0,1,2 — комья снизу вверх, 3 ведро, 4 морковка, 5/6 руки
        public float X, Y, Vx, Vy, Ang, Va, R, RestOff;
        public float SX, SY, SAng;
    }

    internal sealed class World
    {
        public readonly List<ScreenInfo> Screens = new List<ScreenInfo>();
        List<WinInfo> wins = new List<WinInfo>();
        readonly Dictionary<IntPtr, Ledge> ledges = new Dictionary<IntPtr, Ledge>();
        readonly List<Flake> flakes = new List<Flake>();
        readonly List<Particle> parts = new List<Particle>();
        readonly List<Tractor> tractors = new List<Tractor>();
        readonly Random rnd = new Random();
        readonly Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        readonly Pen pileEdge = new Pen(Color.FromArgb(200, 190, 208, 232), 1f);

        public int Intensity = 2;          // 1 лёгкий, 2 обычный, 3 метель
        public bool SettleOnWindows = true;
        public bool TractorOn = true;
        public int TractorEvery = 0;     // минуты; 0 — когда много снега
        public bool SnowmanOn = true;
        public int CrashCount;           // сколько раз снеговик разбился — для звука
        public bool AnyTractor { get { return tractors.Count > 0; } }

        // Сугроб на панели задач может дорасти до подписей нижних значков рабочего стола.
        const float TaskbarPile = 40f;

        float time, wind, tractorCheck = 5f;

        float Rf(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        public void SetScreens(List<ScreenInfo> list)
        {
            Screens.Clear();
            Screens.AddRange(list);
            ledges.Clear();
            flakes.Clear();
            parts.Clear();
            tractors.Clear();
            foreach (var sc in Screens)
            {
                var f = new Ledge(sc.Bounds.Left, sc.Bounds.Right, sc.Bounds.Bottom, TaskbarPile * sc.Scale);
                f.Hwnd = IntPtr.Zero;
                f.Z = int.MaxValue;
                f.LastTractor = time;
                sc.Floor = f;
                int n = TargetCount(sc);
                for (int i = 0; i < n; i++) Spawn(sc, Rf(sc.Bounds.Top, sc.Bounds.Bottom));
            }
        }

        int BaseCount()
        {
            return Intensity == 1 ? 150 : (Intensity == 3 ? 14000 : 7000);
        }

        // Сколько снега даёт одна снежинка: при плотном снегопаде меньше, чтобы сугробы росли не мгновенно.
        float LandK()
        {
            return 2f * 380f / BaseCount() * (Intensity == 3 ? 6f : 1f); // ×2 — сугробы растут вдвое быстрее (15.11)
        }

        void Kill(int i)
        {
            int last = flakes.Count - 1;
            flakes[i] = flakes[last];
            flakes.RemoveAt(last);
        }

        int TargetCount(ScreenInfo sc)
        {
            int baseCount = BaseCount();
            return (int)(baseCount * sc.Bounds.Width / 1920f);
        }

        void Spawn(ScreenInfo sc, float y)
        {
            Spawn(sc, Rf(sc.Bounds.Left, sc.Bounds.Right), y);
        }

        // Глубина Z: 0 — далеко (мелкая, бледная, медленная), 1 — близко.
        void Spawn(ScreenInfo sc, float x, float y)
        {
            var f = new Flake();
            float s = sc.Scale;
            float z = (float)Math.Pow(rnd.NextDouble(), 1.6);
            f.Z = z;
            f.S = s;
            f.R = (0.8f + z * 2.6f) * s * Rf(0.85f, 1.15f);
            f.X = x;
            f.Y = y;
            f.Vy = (22f + z * 45f) * s * Rf(0.85f, 1.15f);
            f.Amp = (4f + z * 18f) * s;
            f.Freq = Rf(0.4f, 1.3f);
            f.Phase = Rf(0f, 6.28f);
            f.Alpha = 0.35f + z * 0.6f;
            flakes.Add(f);
        }

        Flake Loose(float x, float y, float s)
        {
            var f = new Flake();
            f.X = x;
            f.Y = y;
            f.Z = 1f;
            f.S = s;
            f.R = Rf(1.5f, 2.6f) * s;
            f.Vy = Rf(40f, 70f) * s;
            f.Amp = Rf(2f, 6f) * s;
            f.Freq = Rf(0.5f, 1.5f);
            f.Phase = Rf(0f, 6.28f);
            f.Alpha = 1f;
            return f;
        }

        ScreenInfo ScreenAt(float x, float y)
        {
            foreach (var sc in Screens)
                if (x >= sc.Bounds.Left && x < sc.Bounds.Right && y >= sc.Bounds.Top && y < sc.Bounds.Bottom) return sc;
            return null;
        }

        float ScaleAt(float x, float y)
        {
            var sc = ScreenAt(x, y);
            return sc == null ? 1f : sc.Scale;
        }

        WinInfo TopAt(float x, float y)
        {
            int ix = (int)x, iy = (int)y;
            foreach (var w in wins)
                if (w.Rect.Contains(ix, iy)) return w;
            return null;
        }

        // ---------- окна ----------

        float lastWinUpdate;

        public void UpdateWindows(List<WinInfo> list)
        {
            float snapDt = Math.Max(0.05f, time - lastWinUpdate);
            lastWinUpdate = time;
            wins = list;
            foreach (var l in ledges.Values) l.Seen = false;
            foreach (var w in list)
            {
                Ledge l;
                if (ledges.TryGetValue(w.Hwnd, out l))
                {
                    if (Math.Abs(l.Left - w.Rect.Left) > 1 || Math.Abs(l.Right - w.Rect.Right) > 1 || Math.Abs(l.Top - w.Rect.Top) > 1)
                    {
                        ShakeOff(l, (w.Rect.Left - l.Left) / snapDt, (w.Rect.Top - l.Top) / snapDt);
                        ledges.Remove(w.Hwnd);
                        l = null;
                    }
                }
                if (l == null)
                {
                    float s = ScaleAt(w.Rect.Left + w.Rect.Width / 2f, w.Rect.Top + 1);
                    l = new Ledge(w.Rect.Left, w.Rect.Right, w.Rect.Top, (w.IsTaskbar ? TaskbarPile : 8f) * s);
                    l.Hwnd = w.Hwnd;
                    l.IsTaskbar = w.IsTaskbar;
                    l.LastTractor = time;
                    ledges[w.Hwnd] = l;
                }
                l.Seen = true;
                l.Z = w.Z;
            }

            var dead = new List<IntPtr>();
            foreach (var kv in ledges)
                if (!kv.Value.Seen) { ShakeOff(kv.Value, 0f, 0f); dead.Add(kv.Key); }
            foreach (var h in dead) ledges.Remove(h);

            tractors.RemoveAll(delegate(Tractor t)
            {
                if (t.L.Hwnd == IntPtr.Zero) return false;
                Ledge cur;
                return !ledges.TryGetValue(t.L.Hwnd, out cur) || cur != t.L;
            });

            foreach (var l in ledges.Values) ComputeExposure(l);
            foreach (var sc in Screens) ComputeExposure(sc.Floor);
        }

        void ComputeExposure(Ledge l)
        {
            int py = l.Top - 1;
            for (int c = 0; c < l.N; c++)
            {
                int px = (int)l.ColX(c);
                bool exp = ScreenAt(px, py) != null;
                if (exp)
                {
                    // над кромкой панели задач всё, что там нарисуем, легло бы поверх окна
                    // (развёрнутый Word, редактор кода), поэтому для неё считаем все окна, не только выше по z
                    foreach (var w in wins)
                    {
                        if (w.Z >= l.Z && !l.IsTaskbar) break;
                        if (w.IsTaskbar) continue;
                        if (w.Rect.Contains(px, py)) { exp = false; break; }
                    }
                }
                l.Exposed[c] = exp;
            }
        }

        // Окно сдвинули или закрыли — снег с него осыпается.
        // Ком снега, который падает под действием тяжести поверх окон.
        Particle Clump(float x, float y, float vx, float vy, float r, float s)
        {
            var q = new Particle();
            q.X = x;
            q.Y = y;
            q.Vx = vx;
            q.Vy = vy;
            q.Gravity = 900f * s;
            q.Life = q.MaxLife = 6f;
            q.R = r;
            q.Gray = 255;
            q.Settle = true;
            return q;
        }

        // Окно сдвинули или закрыли — снег с него срывается комьями и падает вниз.
        // По инерции комья чуть летят вслед за окном.
        void ShakeOff(Ledge l, float winVx, float winVy)
        {
            int budget = 700;
            float s = ScaleAt(l.Left + 2, l.Top - 1);
            float ivx = Math.Max(-600f * s, Math.Min(600f * s, winVx * 0.3f));
            float ivy = Math.Max(-400f * s, Math.Min(0f, winVy * 0.3f));
            for (int c = 0; c < l.N && budget > 0; c += 2)
            {
                float h = Math.Max(l.H[c], c + 1 < l.N ? l.H[c + 1] : 0f);
                if (h < 1f || !l.Exposed[c]) continue;
                int k = 1 + (int)(h / 4f);
                for (int i = 0; i < k && budget > 0; i++, budget--)
                {
                    float r = Math.Max(1.2f * s, Math.Min(4.5f * s, h * Rf(0.25f, 0.55f)));
                    parts.Add(Clump(l.ColX(c) + Rf(-1f, 1f), l.Top - Rf(0f, h), ivx + Rf(-50f, 50f) * s, ivy + Rf(-60f, 15f) * s, r, s));
                }
            }
        }

        int RampCols(Ledge l)
        {
            return Math.Max(4, (int)(l.MaxH * 1.6f / Ledge.Col));
        }

        float CapAt(Ledge l, int c)
        {
            return l.MaxH;
        }

        void AddSnow(Ledge l, float x, float amount, float s)
        {
            int n = l.N;
            int c = (int)((x - l.Left) / Ledge.Col);
            if (c < 0 || c >= n) return;
            if (l.IsTaskbar && !l.Exposed[c]) return; // над панелью окно — снег не копится
            // снег скатывается к более низкому соседу
            for (int k = 0; k < 8; k++)
            {
                int best = c;
                float bh = l.H[c] - 0.7f;
                if (c > 0 && l.H[c - 1] < bh) { best = c - 1; bh = l.H[c - 1]; }
                if (c < n - 1 && l.H[c + 1] < bh) { best = c + 1; }
                if (best == c) break;
                c = best;
            }
            float cap = CapAt(l, c);
            float nh = l.H[c] + amount;
            if (nh <= cap) { l.H[c] = nh; return; }
            l.H[c] = cap;
            // у края лишний снег не висит козырьком, а осыпается вниз
            int d = Math.Min(c, n - 1 - c);
            if (!l.IsTaskbar && l.Hwnd != IntPtr.Zero && d < 3 && l.Exposed[c] && rnd.Next(3) == 0)
            {
                bool leftSide = c < n - 1 - c;
                float fx = leftSide ? l.Left - 2f * s : l.Right + 2f * s;
                parts.Add(Clump(fx, l.Top - Rf(0f, cap + 1f), (leftSide ? -1f : 1f) * Rf(10f, 40f) * s, Rf(-10f, 10f) * s, Rf(1.5f, 2.8f) * s, s));
            }
        }

        // ---------- шаг симуляции ----------

        public void Step(float dt)
        {
            StepSnowmen(dt);
            time += dt;
            float gust;
            if (Intensity == 3)
            {
                // метель: сильный ветер в одну сторону с порывами, раз в несколько минут меняет направление
                gust = (float)(Math.Sin(time * 0.31) * 0.5 + Math.Sin(time * 0.13 + 1) * 0.35 + Math.Sin(time * 0.71) * 0.15);
                float dir = Math.Max(-1f, Math.Min(1f, (float)Math.Sin(time * 0.005 + 0.8) * 3f));
                wind = 140f * (0.6f + 0.6f * gust) * dir;
            }
            else
            {
                gust = (float)(Math.Sin(time * 0.07) * 0.7 + Math.Sin(time * 0.023) * 0.3);
                wind = (Intensity == 2 ? 25f : 10f) * gust;
            }
            float landK = LandK();
            float swirl = Intensity == 3 ? 70f : (Intensity == 2 ? 12f : 0f);

            foreach (var sc in Screens)
            {
                float fallTime = sc.Bounds.Height / (45f * sc.Scale);
                sc.SpawnAcc += TargetCount(sc) / fallTime * dt;
                float side = Math.Min(0.6f, Math.Abs(wind) / (Math.Abs(wind) + 60f));
                while (sc.SpawnAcc >= 1f)
                {
                    sc.SpawnAcc -= 1f;
                    if (rnd.NextDouble() < side)
                        Spawn(sc, wind > 0 ? sc.Bounds.Left : sc.Bounds.Right - 1, Rf(sc.Bounds.Top, sc.Bounds.Bottom - sc.Bounds.Height * 0.2f));
                    else
                        Spawn(sc, sc.Bounds.Top);
                }
            }

            // сугроб внизу экрана: снежинка садится на его поверхность, а не пролетает перед ним
            var grounds = new Dictionary<ScreenInfo, Ledge>();
            foreach (var gsc in Screens) grounds[gsc] = GroundFor(gsc);

            for (int i = flakes.Count - 1; i >= 0; i--)
            {
                var f = flakes[i];
                float py = f.Y;
                float vx = wind * f.S * (0.5f + f.Z) + f.Amp * f.Freq * (float)Math.Cos(time * f.Freq + f.Phase);
                float vy = f.Vy;
                if (swirl > 0)
                {
                    vx += swirl * f.S * (float)Math.Sin(f.Y * 0.008f / f.S + time * 1.6f + f.Phase);
                    vy += swirl * 0.6f * f.S * (float)Math.Cos(f.X * 0.007f / f.S + time * 1.1f + f.Phase * 0.7f);
                }
                f.X += vx * dt;
                f.Y += vy * dt;

                var sc = ScreenAt(f.X, py);
                if (sc == null) { Kill(i); continue; }

                Ledge gl;
                if (grounds.TryGetValue(sc, out gl) && f.Y < gl.Top && f.Y >= gl.Top - gl.MaxH * 1.3f)
                {
                    int gc = (int)((f.X - gl.Left) / Ledge.Col);
                    if (gc >= 0 && gc < gl.N && gl.Exposed[gc] && f.Y >= gl.Top - gl.H[gc])
                    {
                        AddSnow(gl, f.X, f.R * 0.45f * landK, f.S);
                        Kill(i);
                        continue;
                    }
                }

                if (f.Y >= sc.Bounds.Bottom)
                {
                    AddSnow(sc.Floor, f.X, f.R * 0.45f * landK, f.S);
                    Kill(i);
                    continue;
                }

                var w = TopAt(f.X, f.Y);
                if (w != null)
                {
                    bool canLand = w.IsTaskbar || SettleOnWindows;
                    if (canLand && py < w.Rect.Top && f.Y >= w.Rect.Top)
                    {
                        Ledge l;
                        if (ledges.TryGetValue(w.Hwnd, out l)) AddSnow(l, f.X, f.R * 0.45f * landK, f.S);
                        Kill(i);
                        continue;
                    }
                    f.Hidden = true; // летит «за» окном
                }
                else f.Hidden = false;
            }

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.Life -= dt;
                if (p.Life <= 0) { parts.RemoveAt(i); continue; }
                float ppy = p.Y;
                p.Vy += p.Gravity * dt;
                p.Vx *= (1f - 0.6f * dt); // сопротивление воздуха
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                if (!p.Settle) continue;
                var psc = ScreenAt(p.X, ppy);
                if (psc == null) { parts.RemoveAt(i); continue; }
                var ground = GroundFor(psc);
                if (ppy < ground.Top && p.Y >= ground.Top && p.X >= ground.Left && p.X < ground.Right)
                {
                    AddSnow(ground, p.X, p.R * 0.9f, psc.Scale);
                    parts.RemoveAt(i);
                }
            }

            StepTractors(dt);
        }

        // ---------- трактор ----------

        Ledge GroundFor(ScreenInfo sc)
        {
            foreach (var l in ledges.Values)
            {
                if (!l.IsTaskbar) continue;
                if (l.Right - l.Left < sc.Bounds.Width / 2) continue;
                if (!sc.Bounds.Contains(l.Left + 5, l.Top + 2)) continue;
                if (l.Top < sc.Bounds.Top + sc.Bounds.Height / 2) continue; // панель не снизу
                return l;
            }
            return sc.Floor;
        }

        bool HasTractor(Ledge l)
        {
            foreach (var t in tractors) if (t.L == l) return true;
            return false;
        }

        public void CallTractor()
        {
            foreach (var sc in Screens)
            {
                var l = GroundFor(sc);
                if (!HasTractor(l)) StartTractor(l);
            }
        }

        void StartTractor(Ledge l)
        {
            var t = new Tractor();
            t.L = l;
            t.Scale = ScaleAt(l.Left + 5, l.Top - 1);
            t.Dir = rnd.Next(2) == 0 ? -1 : 1;
            int w = Sprites.Cols * Sprites.PixelSize(t.Scale);
            t.X = t.Dir < 0 ? l.Right + w : l.Left - w;
            t.Heavy = l.Fill() > 0.45f;
            tractors.Add(t);
        }

        // Вал перед отвалом: высота растёт как корень из объёма.
        static float MoundH(float carry) { return 1.6f * (float)Math.Sqrt(carry); }
        static float MoundCarry(float h) { float k = h / 1.6f; return k * k; }

        void DrawMound(Graphics g, Tractor t)
        {
            if (t.Carry < 2f) return;
            int p = Sprites.PixelSize(t.Scale);
            float mh = Math.Min(9f * p, MoundH(t.Carry));
            float mw = mh * 1.9f + 2f * p;
            float x0 = t.X + t.Dir * p;            // вал прижат к отвалу
            float top = t.L.Top;
            var pts = new List<PointF>();
            const int steps = 14;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;      // 0 — у отвала, 1 — передний край
                float y = mh * (float)Math.Sqrt(Math.Max(0, 1 - u * u)) * (1f + 0.06f * (float)Math.Sin(u * 17f + t.T * 3f));
                pts.Add(new PointF(x0 + t.Dir * u * mw, top - y));
            }
            pts.Add(new PointF(x0 + t.Dir * mw, top));
            pts.Add(new PointF(x0, top));
            var arr = pts.ToArray();
            using (var br = new LinearGradientBrush(new PointF(0, top - mh - 1), new PointF(0, top + 1),
                Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 214, 226, 246)))
                g.FillPolygon(br, arr);
            g.DrawLines(pileEdge, pts.GetRange(0, steps + 1).ToArray());
        }

        // Трактор упёрся: вал остаётся кучей перед отвалом, потом он его снесёт с разгона.
        void DumpMound(Tractor t, int p)
        {
            var l = t.L;
            if (t.Carry < 2f) { t.Carry = 0; return; }
            float mh = Math.Min(l.MaxH * 1.3f, Math.Max(MoundH(t.Carry), 3f * p));
            float mw = mh * 1.9f + 2f * p;
            float x0 = t.X + t.Dir * p;
            for (float d = 0; d <= mw; d += Ledge.Col)
            {
                int c = (int)((x0 + t.Dir * d - l.Left) / Ledge.Col);
                if (c < 0 || c >= l.N) continue;
                float u = d / mw;
                float h = mh * (float)Math.Sqrt(Math.Max(0, 1 - u * u));
                l.H[c] = Math.Max(l.H[c], Math.Min(l.MaxH * 1.3f, h));
            }
            t.Carry = 0;
        }

        static float ExposedShare(Ledge l)
        {
            int k = 0;
            for (int c = 0; c < l.N; c++) if (l.Exposed[c]) k++;
            return k / (float)Math.Max(1, l.N);
        }

        void StepTractors(float dt)
        {
            tractorCheck -= dt;
            if (TractorOn && tractorCheck <= 0)
            {
                tractorCheck = 5f;
                foreach (var sc in Screens)
                {
                    var l = GroundFor(sc);
                    if (HasTractor(l)) continue;
                    float fill = l.Fill();
                    if (ExposedShare(l) < 0.3f) continue; // панель закрыта окном — трактор не едет поверх
                    bool go = TractorEvery > 0
                        ? (time - l.LastTractor > TractorEvery * 60f && fill > 0.02f)
                        : fill > 0.8f; // «когда много снега» — сугроб дорастает почти до подписей значков
                    if (go) StartTractor(l);
                }
            }

            for (int i = tractors.Count - 1; i >= 0; i--)
            {
                var t = tractors[i];
                var l = t.L;
                int p = Sprites.PixelSize(t.Scale);
                int w = Sprites.Cols * p;
                t.T += dt;
                float maxCarry = MoundCarry(9f * p);
                float v;
                if (t.Stage == 1)
                {
                    // отъезжает назад, чтобы взять разгон
                    v = -70f * t.Scale;
                    t.StageT += dt;
                    if (t.StageT > 1.8f) { t.Stage = 2; t.StageT = 0; }
                }
                else if (t.Stage == 2)
                {
                    v = 0;
                    t.StageT += dt;
                    if (t.StageT > 0.6f) { t.Stage = 3; t.Speed = 0; }
                }
                else if (t.Stage == 3)
                {
                    t.Speed = Math.Min(280f * t.Scale, t.Speed + 320f * t.Scale * dt);
                    v = t.Speed;
                }
                else
                {
                    // чем больше вал, тем тяжелее толкать
                    float load = Math.Min(1f, t.Carry / Math.Max(1f, maxCarry));
                    v = 110f * t.Scale * (1f - (t.Heavy ? 0.55f : 0.3f) * load);
                }
                t.X += t.Dir * v * dt;
                t.Anim += dt * Math.Abs(v) / (110f * t.Scale);

                // много снега: на середине упирается, оставляет кучу и отъезжает
                float mid = (l.Left + l.Right) * 0.5f;
                if (t.Heavy && t.Stage == 0 && (t.Dir > 0 ? t.X >= mid : t.X <= mid))
                {
                    DumpMound(t, p);
                    t.Stage = 1;
                    t.StageT = 0;
                }

                // отвал сгребает снег и толкает его перед собой валом
                if (v > 0) {
                float a = t.Dir < 0 ? t.X : t.X - 6 * p;
                float b = t.Dir < 0 ? t.X + 6 * p : t.X;
                int c0 = Math.Max(0, (int)((a - l.Left) / Ledge.Col));
                int c1 = Math.Min(l.N - 1, (int)((b - l.Left) / Ledge.Col));
                for (int c = c0; c <= c1; c++)
                {
                    float h = l.H[c];
                    if (h < 0.3f) continue;
                    l.H[c] = 0;
                    if (l.Exposed[c]) t.Carry += h * Ledge.Col;
                }
                }
                // вал не выше отвала: лишнее пересыпается вперёд и вверх
                while (t.Carry > maxCarry && parts.Count < 3000)
                {
                    float mh = MoundH(t.Carry);
                    float r = Rf(1.2f, 2.6f) * t.Scale;
                    float kick = t.Stage == 3 ? 2.2f : 1f; // с разгона снег летит дальше
                    var q = Clump(t.X + t.Dir * Rf(0f, mh * 1.5f), l.Top - mh, t.Dir * Rf(40f, 140f) * kick * t.Scale, -Rf(120f, 260f) * kick * t.Scale, r, t.Scale);
                    q.Settle = false;
                    q.Gravity = 600f * t.Scale;
                    q.Life = q.MaxLife = Rf(0.7f, 1.2f);
                    parts.Add(q);
                    t.Carry -= r * r * 2f;
                }

                // дымок из трубы
                t.PuffAcc += dt;
                if (t.PuffAcc > (t.Stage == 3 ? 0.08f : 0.25f))
                {
                    t.PuffAcc = 0;
                    float left = t.Dir < 0 ? t.X : t.X - w;
                    float pipe = t.Dir < 0 ? Sprites.PipeCol : Sprites.Cols - Sprites.PipeCol;
                    var q = new Particle();
                    q.X = left + pipe * p;
                    q.Y = l.Top - (Sprites.Rows - Sprites.PipeRow) * p;
                    q.Vx = -t.Dir * 12f * t.Scale;
                    q.Vy = -25f * t.Scale;
                    q.Gravity = -8f * t.Scale;
                    q.Life = q.MaxLife = 1.2f;
                    q.R = 2.5f * t.Scale;
                    q.Gray = 170;
                    parts.Add(q);
                }

                bool done = t.Dir < 0 ? t.X < l.Left - w - 20 : t.X > l.Right + w + 20;
                if (done)
                {
                    l.LastTractor = time;
                    t.Carry = 0;
                    tractors.RemoveAt(i);
                }
            }
        }

        // ---------- снеговик ----------

        readonly Dictionary<ScreenInfo, Snowman> snowmen = new Dictionary<ScreenInfo, Snowman>();

        Snowman SnowmanFor(ScreenInfo sc)
        {
            Snowman m;
            if (!snowmen.TryGetValue(sc, out m))
            {
                m = new Snowman();
                m.X = Rf(0.62f, 0.85f);
                snowmen[sc] = m;
            }
            return m;
        }

        float SnowmanHalfW(float s) { return 34f * s; }

        void Radii(Snowman m, float s, out float r1, out float r2, out float r3)
        {
            float gr = m.Growth;
            r1 = 30f * s * Math.Min(1f, gr / 0.4f);
            r2 = gr > 0.4f ? 22.5f * s * Math.Min(1f, (gr - 0.4f) / 0.3f) : 0f;
            r3 = gr > 0.7f ? 16.9f * s * Math.Min(1f, (gr - 0.7f) / 0.2f) : 0f;
        }

        // Где стоит снеговик: x и уровень подошвы. ledge — на чём стоит (null, если в руках или падает).
        void SnowmanPos(ScreenInfo sc, Snowman m, out float x, out float baseY, out Ledge on)
        {
            on = null;
            if (m.Dragging || m.Falling) { x = m.FX; baseY = m.FY; return; }
            if (m.Anchor != IntPtr.Zero)
            {
                Ledge l;
                if (ledges.TryGetValue(m.Anchor, out l) && l == m.AnchorLedge && m.Dx >= 0 && m.Dx <= l.Right - l.Left)
                {
                    on = l;
                    x = l.Left + m.Dx;
                    baseY = l.Top;
                    return;
                }
                // окно сдвинули или закрыли — снеговик падает
                m.Anchor = IntPtr.Zero;
                m.AnchorLedge = null;
                m.Falling = true;
                m.FX = m.LastX;
                m.FY = m.LastBase;
                m.FVy = 0;
                x = m.FX;
                baseY = m.FY;
                return;
            }
            var g = sc.Paused ? sc.Floor : GroundFor(sc);
            on = g;
            x = g.Left + (g.Right - g.Left) * m.X;
            baseY = g.Top;
        }

        void StepSnowmen(float dt)
        {
            if (!SnowmanOn) return;
            foreach (var sc in Screens)
            {
                var m = SnowmanFor(sc);
                m.T += dt;
                float grow = 1f / (600f / (Intensity == 3 ? 4f : (Intensity == 2 ? 1.5f : 1f)));
                m.Growth = Math.Min(1f, m.Growth + grow * dt);
                float s = sc.Scale;

                if (m.Falling && !m.Dragging) StepFall(sc, m, dt);

                float x, baseY;
                Ledge on;
                SnowmanPos(sc, m, out x, out baseY, out on);
                m.LastX = x;
                m.LastBase = baseY;

                if (m.PendingBreak)
                {
                    m.PendingBreak = false;
                    StartBreak(m, x, baseY, s);
                }
                if (m.Broken)
                {
                    StepBroken(m, x, baseY, on, sc, dt);
                    continue;
                }

                if (m.JumpT >= 0)
                {
                    m.JumpT += dt;
                    if (m.JumpT >= m.JumpDur) m.JumpT = -1f;
                    continue;
                }
                if (m.Growth < 0.4f || on == null || m.Anchor != IntPtr.Zero) continue;
                foreach (var t in tractors)
                {
                    if (t.L != on) continue;
                    int p = Sprites.PixelSize(t.Scale);
                    float tw = Sprites.Cols * p, th = Sprites.Rows * p;
                    float v = 110f * t.Scale;
                    float half = SnowmanHalfW(s);
                    float d = t.Dir < 0 ? t.X - (x + half) : (x - half) - t.X;
                    float lead = v * 0.35f;
                    if (d > 0 && d < lead)
                    {
                        m.JumpT = 0f;
                        m.JumpDur = (tw + 2 * half + lead) / v + 0.15f;
                        m.JumpH = th * 1.25f;
                    }
                }
            }
        }

        void StepFall(ScreenInfo home, Snowman m, float dt)
        {
            var sc = ScreenAt(m.FX, Math.Max(m.FY - 1, home.Bounds.Top)) ?? home;
            float s = sc.Scale;
            m.FVy += 1400f * s * dt;
            float ny = m.FY + m.FVy * dt;
            // бросок: летит по инерции, воздух тормозит, от краёв экрана отскакивает
            if (m.FVx != 0)
            {
                float nx = m.FX + m.FVx * dt;
                m.FVx *= (1f - 0.35f * dt);
                if (ScreenAt(nx, Math.Max(sc.Bounds.Top, Math.Min(sc.Bounds.Bottom - 1, ny))) == null)
                {
                    // сильно швырнули в край экрана — разбивается о стенку, слабо — отскакивает
                    if (Math.Abs(m.FVx) > 500f * s && m.Growth >= 0.4f) { WallBreak(sc, m, m.FVx > 0 ? 1 : -1); return; }
                    m.FVx = -m.FVx * 0.5f;
                }
                else m.FX = nx;
            }
            bool hard = m.FVy > 650f * s && m.Growth >= 0.4f; // упал с высоты — разлетится
            // ищем верхнюю кромку окна, через которую пролетели (окна идут сверху вниз по z-order)
            foreach (var w in wins)
            {
                if (m.FX < w.Rect.Left + 4 || m.FX >= w.Rect.Right - 4) continue;
                if (m.FY <= w.Rect.Top && ny >= w.Rect.Top)
                {
                    Ledge l;
                    if (!ledges.TryGetValue(w.Hwnd, out l)) continue;
                    m.Falling = false;
                    m.JumpT = -1f;
                    m.FVx = 0;
                    m.PendingBreak = hard;
                    if (w.IsTaskbar && l == GroundFor(sc))
                    {
                        m.Anchor = IntPtr.Zero;
                        m.X = (m.FX - l.Left) / Math.Max(1f, l.Right - l.Left);
                        MoveSnowmanHome(m, sc);
                    }
                    else
                    {
                        m.Anchor = w.Hwnd;
                        m.AnchorLedge = l;
                        m.Dx = m.FX - l.Left;
                    }
                    return;
                }
            }
            if (ny >= sc.Bounds.Bottom)
            {
                var g = GroundFor(sc);
                m.Falling = false;
                m.FVx = 0;
                m.PendingBreak = hard;
                m.Anchor = IntPtr.Zero;
                m.X = Math.Max(0.02f, Math.Min(0.98f, (m.FX - g.Left) / Math.Max(1f, g.Right - g.Left)));
                MoveSnowmanHome(m, sc);
                return;
            }
            m.FY = ny;
        }

        // ---- снеговик разлетается и собирается обратно ----

        void HomeOf(int kind, float x, float baseY, float r1, float r2, float r3, out float hx, out float hy, out float ha)
        {
            float y1 = baseY - r1;
            float y2 = y1 - r1 * 0.82f - r2 * 0.78f;
            float y3 = y2 - r2 * 0.82f - r3 * 0.8f;
            hx = x; ha = 0;
            switch (kind)
            {
                case 0: hy = y1; break;
                case 1: hy = y2; break;
                case 2: hy = y3; break;
                case 3: hy = y3 - r3 * 0.62f - r3 * 0.5f; ha = -12f; break;
                case 4: hx = x + r3 * 0.45f; hy = y3 + r3 * 0.07f; break;
                case 5: hx = x - r2 * 0.85f - r2 * 0.6f; hy = y2 - r2 * 0.25f - r2 * 0.3f; ha = 205f; break;
                default: hx = x + r2 * 0.85f + r2 * 0.65f; hy = y2 - r2 * 0.25f - r2 * 0.3f; ha = -25f; break;
            }
        }

        // Удар о боковую стенку: куски летят от стенки и падают на панель задач под ней.
        void WallBreak(ScreenInfo sc, Snowman m, int wall)
        {
            float s = sc.Scale;
            float r1, r2, r3;
            Radii(m, s, out r1, out r2, out r3);
            var g = GroundFor(sc);
            float hx = wall > 0 ? Math.Min(m.FX, g.Right - r1 * 1.6f) : Math.Max(m.FX, g.Left + r1 * 1.6f);
            m.Falling = false;
            m.FVx = 0;
            m.FVy = 0;
            m.Anchor = IntPtr.Zero;
            m.AnchorLedge = null;
            m.X = Math.Max(0.02f, Math.Min(0.98f, (hx - g.Left) / Math.Max(1f, g.Right - g.Left)));
            MoveSnowmanHome(m, sc);
            StartBreak(m, hx, g.Top, s, m.FX - hx, Math.Min(0f, m.FY - g.Top), wall);
        }

        void StartBreak(Snowman m, float x, float baseY, float s, float ox = 0, float oy = 0, int wall = 0)
        {
            float r1, r2, r3;
            Radii(m, s, out r1, out r2, out r3);
            m.Broken = true;
            m.BrokenT = 0;
            CrashCount++;
            m.JumpT = -1f;
            m.Pieces.Clear();
            int count = m.Growth >= 0.9f ? 7 : (r3 > 0.5f ? 3 : (r2 > 0.5f ? 2 : 1));
            int dir = rnd.Next(2) == 0 ? -1 : 1;
            for (int k = 0; k < count; k++)
            {
                var pc = new Piece();
                pc.Kind = k;
                float hx, hy, ha;
                HomeOf(k, x + ox, baseY + oy, r1, r2, r3, out hx, out hy, out ha);
                pc.X = hx; pc.Y = hy; pc.Ang = ha;
                int sg = (k % 2 == 0) ? dir : -dir;
                switch (k)
                {
                    case 0: pc.R = r1; pc.RestOff = r1; pc.Vx = sg * Rf(40f, 90f) * s; break;
                    case 1: pc.R = r2; pc.RestOff = r2; pc.Vx = sg * Rf(110f, 200f) * s; pc.Vy = -Rf(50f, 150f) * s; break;
                    case 2: pc.R = r3; pc.RestOff = r3; pc.Vx = sg * Rf(160f, 280f) * s; pc.Vy = -Rf(120f, 260f) * s; break;
                    case 3: pc.R = r3 * 0.7f; pc.RestOff = r3 * 0.5f; pc.Vx = sg * Rf(90f, 220f) * s; pc.Vy = -Rf(320f, 480f) * s; pc.Va = sg * Rf(300f, 600f); break;
                    case 4: pc.R = r3 * 0.5f; pc.RestOff = r3 * 0.12f; pc.Vx = sg * Rf(120f, 260f) * s; pc.Vy = -Rf(280f, 420f) * s; pc.Va = sg * Rf(500f, 900f); break;
                    default: pc.R = r2 * 0.75f; pc.RestOff = r2 * 0.06f; pc.Vx = sg * Rf(80f, 220f) * s; pc.Vy = -Rf(220f, 380f) * s; pc.Va = sg * Rf(300f, 700f); break;
                }
                if (wall != 0)
                {
                    // от стенки: все куски отлетают в одну сторону
                    pc.Vx = -wall * Rf(120f, 380f) * s;
                    pc.Vy = Rf(-260f, 60f) * s;
                    pc.Va = -wall * Rf(200f, 700f);
                }
                m.Pieces.Add(pc);
            }
            // облачко снега от удара
            float cx = wall != 0 ? x + ox + wall * r1 * 0.8f : x;
            float cy = baseY + oy;
            for (int i = 0; i < (wall != 0 ? 70 : 40) && parts.Count < 3000; i++)
            {
                float vx = wall != 0 ? -wall * Rf(40f, 320f) * s : Rf(-220f, 220f) * s;
                var q = Clump(cx + Rf(-r1, r1) * (wall != 0 ? 0.3f : 1f), cy - Rf(0, r1 * (wall != 0 ? 2.2f : 0.5f)), vx, -Rf(80f, 320f) * s, Rf(1.2f, 2.6f) * s, s);
                q.Settle = false;
                q.Life = q.MaxLife = Rf(0.5f, 1.0f);
                parts.Add(q);
            }
        }

        void StepBroken(Snowman m, float x, float baseY, Ledge on, ScreenInfo sc, float dt)
        {
            float s = sc.Scale;
            float r1, r2, r3;
            Radii(m, s, out r1, out r2, out r3);
            float left = on != null ? on.Left : sc.Bounds.Left, right = on != null ? on.Right : sc.Bounds.Right;
            float prevT = m.BrokenT;
            m.BrokenT += dt;
            const float scatter = 5.5f, gather = 0.8f;
            if (m.BrokenT < scatter)
            {
                foreach (var pc in m.Pieces)
                {
                    pc.Vy += 1400f * s * dt;
                    pc.X += pc.Vx * dt;
                    pc.Y += pc.Vy * dt;
                    float rest = baseY - pc.RestOff;
                    bool onGround = false;
                    if (pc.Y >= rest)
                    {
                        pc.Y = rest;
                        if (pc.Vy > 120f * s) { pc.Vy = -pc.Vy * 0.35f; pc.Va *= 0.6f; }
                        else { pc.Vy = 0; onGround = true; }
                    }
                    bool ball = pc.Kind <= 2;
                    if (onGround)
                    {
                        pc.Vx *= Math.Max(0f, 1f - (ball ? 0.9f : 3f) * dt);
                        if (ball) pc.Ang += pc.Vx / Math.Max(1f, pc.R) * dt * 57.3f;
                        else pc.Va *= Math.Max(0f, 1f - 5f * dt);
                    }
                    if (!ball || !onGround) pc.Ang += pc.Va * dt;
                    if (pc.X - pc.R < left) { pc.X = left + pc.R; pc.Vx = Math.Abs(pc.Vx) * 0.5f; }
                    if (pc.X + pc.R > right) { pc.X = right - pc.R; pc.Vx = -Math.Abs(pc.Vx) * 0.5f; }
                }
                return;
            }
            if (prevT < scatter)
                foreach (var pc in m.Pieces) { pc.SX = pc.X; pc.SY = pc.Y; pc.SAng = pc.Ang; }
            float u = Math.Min(1f, (m.BrokenT - scatter) / gather);
            float e = u * u * (3f - 2f * u);
            foreach (var pc in m.Pieces)
            {
                float hx, hy, ha;
                HomeOf(pc.Kind, x, baseY, r1, r2, r3, out hx, out hy, out ha);
                float sa = pc.SAng % 360f;
                while (sa - ha > 180f) sa -= 360f;
                while (ha - sa > 180f) sa += 360f;
                pc.X = pc.SX + (hx - pc.SX) * e;
                pc.Y = pc.SY + (hy - pc.SY) * e - (float)Math.Sin(Math.PI * u) * r1 * 0.6f;
                pc.Ang = sa + (ha - sa) * e;
            }
            if (u >= 1f) m.Broken = false;
        }

        void DrawBroken(Graphics g, Snowman m, float s)
        {
            float r1, r2, r3;
            Radii(m, s, out r1, out r2, out r3);
            using (var arm = new Pen(Color.FromArgb(255, 105, 68, 38), Math.Max(2f, 3.5f * s)))
            {
                arm.StartCap = LineCap.Round;
                arm.EndCap = LineCap.Round;
                foreach (var pc in m.Pieces)
                {
                    if (pc.Kind < 5) continue;
                    double a = pc.Ang * Math.PI / 180.0;
                    float len = r2 * 1.5f;
                    DrawArm(g, arm, pc.X - len * 0.5f * (float)Math.Cos(a), pc.Y - len * 0.5f * (float)Math.Sin(a), pc.Ang, len);
                }
            }
            foreach (var pc in m.Pieces)
            {
                if (pc.Kind > 2) continue;
                DrawBall(g, pc.X, pc.Y, pc.R);
                if (pc.Kind == 2 && m.Growth >= 0.9f)
                {
                    // глаза крутятся вместе с катящейся головой
                    double a = pc.Ang * Math.PI / 180.0;
                    float ca = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    using (var coal = new SolidBrush(Color.FromArgb(255, 32, 32, 40)))
                    {
                        float er = r3 * 0.12f;
                        float[] ex = { -0.38f, 0.30f };
                        foreach (var k in ex)
                        {
                            float dx = k * r3, dy = -0.28f * r3;
                            float rx = pc.X + dx * ca - dy * sn, ry = pc.Y + dx * sn + dy * ca;
                            g.FillEllipse(coal, rx - er, ry - er, er * 2, er * 2);
                        }
                    }
                }
            }
            foreach (var pc in m.Pieces)
            {
                if (pc.Kind == 3) DrawBucket(g, pc.X, pc.Y + r3 * 0.5f, r3, pc.Ang);
                else if (pc.Kind == 4)
                {
                    var st = g.Save();
                    g.TranslateTransform(pc.X, pc.Y);
                    g.RotateTransform(pc.Ang);
                    using (var carrot = new SolidBrush(Color.FromArgb(255, 245, 128, 25)))
                        g.FillPolygon(carrot, new[] { new PointF(-r3 * 0.47f, -r3 * 0.13f), new PointF(-r3 * 0.47f, r3 * 0.13f), new PointF(r3 * 0.5f, r3 * 0.03f) });
                    g.Restore(st);
                }
            }
        }

        public bool NeedFastSnapshot
        {
            get
            {
                foreach (var m in snowmen.Values)
                    if (m.Anchor != IntPtr.Zero || m.Falling || m.Dragging) return true;
                return false;
            }
        }

        // снеговик переехал на другой монитор — теперь он «живёт» там
        void MoveSnowmanHome(Snowman m, ScreenInfo sc)
        {
            ScreenInfo old = null;
            foreach (var kv in snowmen) if (kv.Value == m) old = kv.Key;
            if (old == null || old == sc) return;
            Snowman other;
            if (snowmen.TryGetValue(sc, out other)) snowmen[old] = other;
            else snowmen.Remove(old);
            snowmen[sc] = m;
        }

        RectangleF SnowmanBox(ScreenInfo sc, Snowman m)
        {
            float x, baseY;
            Ledge on;
            SnowmanPos(sc, m, out x, out baseY, out on);
            float r1, r2, r3;
            Radii(m, sc.Scale, out r1, out r2, out r3);
            float lift = 0f;
            if (m.JumpT >= 0) { float u = m.JumpT / m.JumpDur; lift = m.JumpH * 4f * u * (1f - u); }
            float h = 2f * (r1 + r2 + r3) + r3 * 1.2f;
            float w = Math.Max(r1, r2 * 1.8f);
            return new RectangleF(x - w, baseY - lift - h, w * 2, h);
        }

        // ---- перетаскивание мышью (координаты экрана) ----

        public bool TryGrabSnowman(int px, int py)
        {
            if (!SnowmanOn) return false;
            foreach (var sc in Screens)
            {
                if (sc.Paused) continue;
                var m = SnowmanFor(sc);
                if (m.Growth < 0.1f || m.Broken) continue;
                if (!SnowmanBox(sc, m).Contains(px, py)) continue;
                float x, baseY;
                Ledge on;
                SnowmanPos(sc, m, out x, out baseY, out on);
                m.Dragging = true;
                m.Falling = false;
                m.JumpT = -1f;
                m.Anchor = IntPtr.Zero;
                m.AnchorLedge = null;
                m.DragOffX = px - x;
                m.DragOffY = py - baseY;
                m.FX = x;
                m.FY = baseY;
                m.FVx = 0;
                m.DVx = m.DVy = 0;
                m.LastDragMs = Environment.TickCount;
                return true;
            }
            return false;
        }

        public void DragSnowman(int px, int py)
        {
            foreach (var m in snowmen.Values)
                if (m.Dragging)
                {
                    float nx = px - m.DragOffX, ny = py - m.DragOffY;
                    int now = Environment.TickCount;
                    float dtm = (now - m.LastDragMs) / 1000f;
                    if (dtm > 0.004f)
                    {
                        m.DVx = m.DVx * 0.4f + (nx - m.FX) / dtm * 0.6f;
                        m.DVy = m.DVy * 0.4f + (ny - m.FY) / dtm * 0.6f;
                        m.LastDragMs = now;
                        m.FX = nx;
                        m.FY = ny;
                    }
                }
        }

        public void DropSnowman(int px, int py)
        {
            foreach (var m in snowmen.Values)
                if (m.Dragging)
                {
                    m.FX = px - m.DragOffX;
                    m.FY = py - m.DragOffY;
                    m.Dragging = false;
                    m.Falling = true;
                    bool still = Environment.TickCount - m.LastDragMs > 90; // держал на месте — просто падает
                    m.FVx = still ? 0 : Math.Max(-3000f, Math.Min(3000f, m.DVx * 0.8f));
                    m.FVy = still ? 0 : Math.Max(-3000f, Math.Min(3000f, m.DVy * 0.8f));
                }
        }

        // ---- рисование ----

        void DrawSnowman(Graphics g, ScreenInfo sc)
        {
            var m = SnowmanFor(sc);
            if (m.Growth < 0.03f) return;
            float s = sc.Scale;
            float x, baseY;
            Ledge on;
            SnowmanPos(sc, m, out x, out baseY, out on);
            if (!sc.Paused && on != null && (on.Hwnd == IntPtr.Zero || on.IsTaskbar))
            {
                int col = Math.Max(0, Math.Min(on.N - 1, (int)((x - on.Left) / Ledge.Col)));
                if (!on.Exposed[col]) return;
            }
            // стоит на окне — окна, которые лежат выше этого, его перекрывают
            bool clipped = false;
            if (on != null && on.Hwnd != IntPtr.Zero && !on.IsTaskbar)
            {
                foreach (var w in wins)
                {
                    if (w.Z >= on.Z) break;
                    g.ExcludeClip(w.Rect);
                    clipped = true;
                }
            }

            float lift = 0f;
            if (m.JumpT >= 0)
            {
                float u = m.JumpT / m.JumpDur;
                lift = m.JumpH * 4f * u * (1f - u);
            }
            float r1, r2, r3;
            Radii(m, s, out r1, out r2, out r3);
            if (m.Broken)
            {
                DrawBroken(g, m, s);
                if (clipped) g.ResetClip();
                return;
            }
            bool full = m.Growth >= 0.9f;

            float y1 = baseY - r1 - lift;              // нижний ком касается поверхности
            float y2 = y1 - r1 * 0.82f - r2 * 0.78f;
            float y3 = y2 - r2 * 0.82f - r3 * 0.8f;

            // руки-веточки: машут; в прыжке и в руках — быстрее
            if (full)
            {
                float speed = (m.JumpT >= 0 || m.Dragging || m.Falling) ? 14f : 4f;
                float wave = (float)Math.Sin(m.T * speed) * 20f;
                using (var arm = new Pen(Color.FromArgb(255, 105, 68, 38), Math.Max(2f, 3.5f * s)))
                {
                    arm.StartCap = LineCap.Round;
                    arm.EndCap = LineCap.Round;
                    DrawArm(g, arm, x - r2 * 0.85f, y2 - r2 * 0.25f, 205f + wave, r2 * 1.5f);
                    DrawArm(g, arm, x + r2 * 0.85f, y2 - r2 * 0.25f, -25f - wave * 0.7f, r2 * 1.5f);
                }
            }

            DrawBall(g, x, y1, r1);
            if (r2 > 0.5f) DrawBall(g, x, y2, r2);
            if (r3 > 0.5f) DrawBall(g, x, y3, r3);

            if (full)
            {
                using (var coal = new SolidBrush(Color.FromArgb(255, 32, 32, 40)))
                {
                    // пуговицы
                    for (int i = 0; i < 3; i++)
                    {
                        float by = y2 - r2 * 0.45f + i * r2 * 0.42f;
                        float br = r2 * 0.09f;
                        g.FillEllipse(coal, x - br, by - br, br * 2, br * 2);
                    }
                    // глаза
                    float er = r3 * 0.12f;
                    g.FillEllipse(coal, x - r3 * 0.38f - er, y3 - r3 * 0.28f - er, er * 2, er * 2);
                    g.FillEllipse(coal, x + r3 * 0.30f - er, y3 - r3 * 0.28f - er, er * 2, er * 2);
                    // улыбка из угольков
                    for (int i = 0; i < 5; i++)
                    {
                        double a = (35 + i * 27.5) * Math.PI / 180.0;
                        float sx = x - r3 * 0.04f + r3 * 0.48f * (float)Math.Cos(a);
                        float sy = y3 + r3 * 0.12f + r3 * 0.32f * (float)Math.Sin(a);
                        float sr = r3 * 0.065f;
                        g.FillEllipse(coal, sx - sr, sy - sr, sr * 2, sr * 2);
                    }
                }
                // нос-морковка
                using (var carrot = new SolidBrush(Color.FromArgb(255, 245, 128, 25)))
                    g.FillPolygon(carrot, new[] {
                        new PointF(x - r3 * 0.02f, y3 - r3 * 0.06f), new PointF(x - r3 * 0.02f, y3 + r3 * 0.2f), new PointF(x + r3 * 0.95f, y3 + r3 * 0.12f) });

                DrawScarf(g, x, y3 + r3 * 0.82f, r3, r2, m.T);
                DrawBucket(g, x, y3 - r3 * 0.62f, r3, -12f);
            }

            // снеговик стоит в сугробе: у подошвы намело снег
            if (on != null && lift < 1f)
            {
                int col = Math.Max(0, Math.Min(on.N - 1, (int)((x - on.Left) / Ledge.Col)));
                float drift = Math.Max(on.H[col], 4f * s) + 4f * s;
                float dw = r1 * 1.35f;
                var pts = new List<PointF>();
                for (int i = 0; i <= 16; i++)
                {
                    float u = i / 16f * 2f - 1f;
                    float hh = drift * (1f - u * u) * (1f + 0.08f * (float)Math.Sin(i * 1.7f));
                    pts.Add(new PointF(x + u * dw, baseY - hh));
                }
                using (var br = new LinearGradientBrush(new PointF(0, baseY - drift - 1), new PointF(0, baseY + 1),
                    Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 222, 232, 248)))
                    g.FillPolygon(br, pts.ToArray());
                g.DrawLines(pileEdge, pts.ToArray());
            }
            if (clipped) g.ResetClip();
        }

        static void DrawScarf(Graphics g, float x, float yNeck, float r3, float r2, float t)
        {
            using (var red = new SolidBrush(Color.FromArgb(255, 210, 35, 45)))
            using (var dark = new SolidBrush(Color.FromArgb(255, 160, 20, 30)))
            using (var stripe = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1f, r3 * 0.07f)))
            {
                // кончик шарфа свисает сбоку на средний ком и чуть качается
                float sway = (float)Math.Sin(t * 2.2f) * r3 * 0.06f;
                var tail = new[] {
                    new PointF(x + r3 * 0.25f, yNeck), new PointF(x + r3 * 0.65f, yNeck),
                    new PointF(x + r3 * 0.78f + sway, yNeck + r2 * 0.95f), new PointF(x + r3 * 0.38f + sway, yNeck + r2 * 0.9f) };
                g.FillPolygon(dark, tail);
                g.DrawLine(stripe, x + r3 * 0.45f, yNeck + r2 * 0.35f, x + r3 * 0.72f + sway * 0.5f, yNeck + r2 * 0.37f);
                // бахрома
                for (int i = 0; i < 4; i++)
                {
                    float fx = x + r3 * (0.42f + i * 0.11f) + sway;
                    g.DrawLine(stripe, fx, yNeck + r2 * 0.92f, fx, yNeck + r2 * 1.05f);
                }
                // петля вокруг шеи
                float w = r3 * 2.05f, h = r3 * 0.5f;
                using (var path = new GraphicsPath())
                {
                    float rr = h * 0.5f;
                    path.AddArc(x - w / 2, yNeck - h / 2, h, h, 90, 180);
                    path.AddArc(x + w / 2 - h, yNeck - h / 2, h, h, 270, 180);
                    path.CloseFigure();
                    g.FillPath(red, path);
                }
                g.DrawLine(stripe, x - w * 0.3f, yNeck - h * 0.45f, x - w * 0.22f, yNeck + h * 0.45f);
                g.DrawLine(stripe, x + w * 0.05f, yNeck - h * 0.45f, x + w * 0.13f, yNeck + h * 0.45f);
            }
        }

        // ведро вместо шапки, чуть набекрень
        static void DrawBucket(Graphics g, float x, float yBottom, float r3, float angle)
        {
            var state = g.Save();
            g.TranslateTransform(x, yBottom);
            g.RotateTransform(angle);
            float wb = r3 * 1.5f, wt = r3 * 1.1f, h = r3 * 1.0f;
            var body = new[] { new PointF(-wb / 2, 0), new PointF(wb / 2, 0), new PointF(wt / 2, -h), new PointF(-wt / 2, -h) };
            using (var br = new LinearGradientBrush(new PointF(-wb / 2, 0), new PointF(wb / 2, 0),
                Color.FromArgb(255, 150, 168, 190), Color.FromArgb(255, 95, 112, 135)))
                g.FillPolygon(br, body);
            using (var rim = new SolidBrush(Color.FromArgb(255, 80, 95, 118)))
            {
                g.FillRectangle(rim, -wb / 2 - r3 * 0.06f, -r3 * 0.16f, wb + r3 * 0.12f, r3 * 0.16f);
                g.FillRectangle(rim, -wt / 2 - r3 * 0.04f, -h - r3 * 0.08f, wt + r3 * 0.08f, r3 * 0.12f);
            }
            using (var handle = new Pen(Color.FromArgb(255, 70, 80, 95), Math.Max(1f, r3 * 0.06f)))
                g.DrawArc(handle, -wb * 0.45f, -h * 0.75f, wb * 0.9f, h * 0.9f, 10, 160);
            using (var snowCap = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                g.FillEllipse(snowCap, -wt / 2 + r3 * 0.05f, -h - r3 * 0.2f, wt - r3 * 0.1f, r3 * 0.22f);
            g.Restore(state);
        }

        static void DrawArm(Graphics g, Pen pen, float x, float y, float angle, float len)
        {
            double a = angle * Math.PI / 180.0;
            float ex = x + len * (float)Math.Cos(a), ey = y + len * (float)Math.Sin(a);
            g.DrawLine(pen, x, y, ex, ey);
            double a1 = a + 0.6, a2 = a - 0.55;
            float fl = len * 0.28f;
            g.DrawLine(pen, ex, ey, ex + fl * (float)Math.Cos(a1), ey + fl * (float)Math.Sin(a1));
            g.DrawLine(pen, ex, ey, ex + fl * (float)Math.Cos(a2), ey + fl * (float)Math.Sin(a2));
            float mx = x + len * 0.55f * (float)Math.Cos(a), my = y + len * 0.55f * (float)Math.Sin(a);
            g.DrawLine(pen, mx, my, mx + fl * 0.8f * (float)Math.Cos(a2 - 0.3), my + fl * 0.8f * (float)Math.Sin(a2 - 0.3));
        }

        static void DrawBall(Graphics g, float x, float y, float r)
        {
            if (r < 0.5f) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x - r, y - r, r * 2, r * 2);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterPoint = new PointF(x - r * 0.35f, y - r * 0.4f);
                    br.CenterColor = Color.FromArgb(255, 255, 255, 255);
                    br.SurroundColors = new[] { Color.FromArgb(255, 200, 214, 236) };
                    g.FillPath(br, path);
                }
            }
            using (var pen = new Pen(Color.FromArgb(150, 160, 178, 205), 1f))
                g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        }

        // ---------- отрисовка ----------

        SolidBrush BrushFor(int gray, float alpha)
        {
            int a = Math.Max(0, Math.Min(15, (int)(alpha * 15f + 0.5f)));
            int key = gray * 16 + a;
            SolidBrush b;
            if (!brushes.TryGetValue(key, out b))
            {
                b = new SolidBrush(Color.FromArgb(a * 17, gray, gray, Math.Min(255, gray + 4)));
                brushes[key] = b;
            }
            return b;
        }

        public void RenderBack(ScreenInfo sc, Graphics g)
        {
            var area = sc.Bounds;
            foreach (var l in ledges.Values) DrawPileIfVisible(g, l, area);
            foreach (var s in Screens) DrawPileIfVisible(g, s.Floor, area);
        }

        public void RenderFront(ScreenInfo sc, Graphics g)
        {
            var area = sc.Bounds;
            foreach (var p in parts)
            {
                if (p.X < area.Left - 6 || p.X > area.Right + 6 || p.Y < area.Top - 6 || p.Y > area.Bottom + 6) continue;
                float alpha = p.Settle ? Math.Min(1f, p.Life / 0.4f) : p.Life / p.MaxLife * (p.Gray == 255 ? 0.95f : 0.5f);
                g.FillEllipse(BrushFor(p.Gray, alpha), p.X - p.R, p.Y - p.R, p.R * 2, p.R * 2);
            }

            // трактор и вал прячутся за окнами, которые закрывают кромку панели задач
            bool tclip = false;
            foreach (var t in tractors)
                foreach (var w in wins)
                    if (!w.IsTaskbar && w.Rect.Top < t.L.Top && w.Rect.Bottom >= t.L.Top - 1 && w.Rect.Right > t.L.Left && w.Rect.Left < t.L.Right)
                    { g.ExcludeClip(w.Rect); tclip = true; }

            foreach (var t in tractors) DrawMound(g, t);

            foreach (var t in tractors)
            {
                int p = Sprites.PixelSize(t.Scale);
                int frame = (((int)(t.T * 3f)) % 2) | ((((int)(t.Anim * 12f)) % 2) << 1);
                var bmp = Sprites.Get(p, t.Dir > 0, frame);
                int left = (int)(t.Dir < 0 ? t.X : t.X - bmp.Width);
                int bounce = ((int)(t.Anim * 6f) % 2 == 0) ? 0 : -1;
                int top = t.L.Top - bmp.Height + bounce; // гусеницы стоят на кромке панели
                var r = new Rectangle(left, top, bmp.Width, bmp.Height);
                if (!r.IntersectsWith(area)) continue;
                g.DrawImage(bmp, r);
            }
            if (tclip) g.ResetClip();

            if (SnowmanOn) foreach (var s2 in Screens) DrawSnowman(g, s2);
        }

        // ---------- снежинки: рисуем прямо в память кадра, так в десятки раз быстрее GDI+ ----------

        readonly Dictionary<int, byte[]> masks = new Dictionary<int, byte[]>();

        static int MaskSize(int r4)
        {
            return (int)Math.Ceiling(r4 / 4f * 2f) + 2;
        }

        byte[] Mask(int r4)
        {
            byte[] m;
            if (masks.TryGetValue(r4, out m)) return m;
            float r = r4 / 4f;
            int size = MaskSize(r4);
            m = new byte[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / r;
                    if (d >= 1f) continue;
                    float k = 1f - d * d;
                    k = k * (float)Math.Sqrt(k);
                    m[y * size + x] = (byte)(255f * Math.Min(1f, k * 1.6f));
                }
            masks[r4] = m;
            return m;
        }

        public unsafe void RasterFlakes(ScreenInfo sc, IntPtr bits)
        {
            int W = sc.Bounds.Width, H = sc.Bounds.Height, ox = sc.Bounds.Left, oy = sc.Bounds.Top;
            uint* p = (uint*)bits.ToPointer();
            for (int i = 0; i < flakes.Count; i++)
            {
                var f = flakes[i];
                if (f.Hidden) continue;
                int r4 = Math.Max(2, Math.Min(40, (int)(f.R * 4f)));
                int size = MaskSize(r4);
                int x0 = (int)(f.X - size * 0.5f) - ox, y0 = (int)(f.Y - size * 0.5f) - oy;
                if (x0 >= W || y0 >= H || x0 + size <= 0 || y0 + size <= 0) continue;
                byte[] m = Mask(r4);
                int a = (int)(f.Alpha * 255f);
                for (int yy = 0; yy < size; yy++)
                {
                    int py = y0 + yy;
                    if (py < 0 || py >= H) continue;
                    uint* row = p + py * W;
                    for (int xx = 0; xx < size; xx++)
                    {
                        int px = x0 + xx;
                        if (px < 0 || px >= W) continue;
                        int aa = m[yy * size + xx] * a / 255;
                        if (aa == 0) continue;
                        uint d = row[px];
                        int inv = 255 - aa;
                        uint cb = (uint)(aa + (d & 0xFF) * inv / 255);
                        uint cg = (uint)(aa + ((d >> 8) & 0xFF) * inv / 255);
                        uint cr = (uint)(aa + ((d >> 16) & 0xFF) * inv / 255);
                        uint ca = (uint)(aa + (d >> 24) * inv / 255);
                        row[px] = cb | (cg << 8) | (cr << 16) | (ca << 24);
                    }
                }
            }
        }

        void DrawPileIfVisible(Graphics g, Ledge l, Rectangle area)
        {
            var box = new Rectangle(l.Left, l.Top - (int)l.MaxH - 2, l.Right - l.Left, (int)l.MaxH + 4);
            if (!box.IntersectsWith(area)) return;
            DrawPile(g, l);
        }

        float Smooth(Ledge l, int k)
        {
            int n = l.N;
            float a = l.H[Math.Max(0, k - 2)], b = l.H[Math.Max(0, k - 1)], c = l.H[k];
            float d = l.H[Math.Min(n - 1, k + 1)], e = l.H[Math.Min(n - 1, k + 2)];
            return (a + 2 * b + 3 * c + 2 * d + e) / 9f;
        }

        static readonly bool Win11 = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        float EndHeight(Ledge l, int a, int b)
        {
            a = Math.Max(0, a);
            b = Math.Min(l.N, Math.Max(a + 1, b));
            float s = 0;
            for (int i = a; i < b; i++) s += l.H[i];
            return s / (b - a);
        }

        // Снег на углу переваливается через скругление окна: дуга снаружи, толщина к низу убывает.
        static void AddArc(List<PointF> pts, float cx, float cy, float R, float h, float a0, float a1, bool outer)
        {
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                float ang = a0 + (a1 - a0) * i / steps;
                float far = Math.Abs(ang + 90f) / 80f;
                float th = outer ? h * (1f - 0.65f * far * far) : 0f;
                double rad = ang * Math.PI / 180.0;
                pts.Add(new PointF(cx + (R + th) * (float)Math.Cos(rad), cy + (R + th) * (float)Math.Sin(rad)));
            }
        }

        void DrawPile(Graphics g, Ledge l)
        {
            int n = l.N, c = 0;
            float R = (l.Hwnd == IntPtr.Zero || l.IsTaskbar) ? 0f : (Win11 ? 8f : 3f) * ScaleAt(l.Left + 2, l.Top - 1);
            int rc = Math.Max(1, (int)Math.Ceiling(R / Ledge.Col));
            var outer = new List<PointF>();
            var poly = new List<PointF>();
            while (c < n)
            {
                if (!l.Exposed[c] || l.H[c] < 0.4f) { c++; continue; }
                int start = c;
                while (c < n && l.Exposed[c] && l.H[c] >= 0.4f) c++;
                int end = c;
                bool dl = R > 0 && start == 0, dr = R > 0 && end == n;
                float hl = dl ? EndHeight(l, 0, rc + 2) : 0f;
                float hr = dr ? EndHeight(l, n - rc - 2, n) : 0f;
                if (hl < 0.6f) dl = false;
                if (hr < 0.6f) dr = false;

                outer.Clear();
                if (dl) AddArc(outer, l.Left + R, l.Top + R, R, hl, -170f, -90f, true);
                else outer.Add(new PointF(l.ColX(start) - Ledge.Col * 0.5f, l.Top));
                for (int k = start; k < end; k++)
                {
                    float x = l.ColX(k);
                    if (dl && x <= l.Left + R) continue;
                    if (dr && x >= l.Right - R) continue;
                    outer.Add(new PointF(x, l.Top - Smooth(l, k)));
                }
                if (dr) AddArc(outer, l.Right - R, l.Top + R, R, hr, -90f, -10f, true);
                else outer.Add(new PointF(l.ColX(end - 1) + Ledge.Col * 0.5f, l.Top));
                if (outer.Count < 3) continue;

                poly.Clear();
                poly.AddRange(outer);
                if (dr) AddArc(poly, l.Right - R, l.Top + R, R, 0f, -10f, -90f, false);
                if (dl) AddArc(poly, l.Left + R, l.Top + R, R, 0f, -90f, -170f, false);
                var arr = poly.ToArray();
                using (var br = new LinearGradientBrush(new PointF(0, l.Top - l.MaxH - 1), new PointF(0, l.Top + R + 1),
                    Color.FromArgb(252, 255, 255, 255), Color.FromArgb(246, 220, 231, 248)))
                    g.FillPolygon(br, arr);

                int from = dl ? 0 : 1, to = dr ? outer.Count : outer.Count - 1;
                if (to - from >= 2)
                {
                    var edge = outer.GetRange(from, to - from).ToArray();
                    g.DrawLines(pileEdge, edge);
                }
            }
        }
    }
}
