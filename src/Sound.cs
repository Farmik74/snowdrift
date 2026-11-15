using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Snowdrift
{
    // Все звуки синтезируются в коде при запуске: никаких чужих файлов и записей.
    internal static class Synth
    {
        public const int Rate = 22050;
        const double TwoPi = Math.PI * 2;

        static short[] Normalize(float[] buf, int count, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < count; i++) max = Math.Max(max, Math.Abs(buf[i]));
            var res = new short[count];
            float k = peak * 32767f / max;
            for (int i = 0; i < count; i++) res[i] = (short)Math.Max(-32767, Math.Min(32767, buf[i] * k));
            return res;
        }

        // Мотор трактора на всю поездку: 40 с без повтора, поэтому стыка и щелчков нет.
        // Мягкое низкое бурчание, обороты чуть «гуляют», в начале плавно нарастает.
        public static short[] Tractor()
        {
            int n = Rate * 40;
            var raw = new float[n];
            var rnd = new Random(3);
            float lp = 0, lp2 = 0, body = 0;
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double wob = 0.5 * Math.Sin(TwoPi * 0.37 * t) + 0.3 * Math.Sin(TwoPi * 0.61 * t + 1) + 0.2 * Math.Sin(TwoPi * 1.13 * t + 2);
                double f = 31 * (1 + 0.04 * wob);
                ph += TwoPi * f / Rate;
                float engine = (float)(0.55 * Math.Sin(ph) + 0.28 * Math.Sin(2 * ph + 0.7) + 0.12 * Math.Sin(3 * ph + 1.1) + 0.05 * Math.Sin(4 * ph + 0.3));
                float rumble = (float)(0.72 + 0.2 * Math.Sin(TwoPi * 6.5 * t * (1 + 0.03 * wob)) + 0.08 * wob);
                float white = (float)(rnd.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.03f;
                lp2 += (lp - lp2) * 0.03f;
                float raw0 = engine * rumble + lp2 * 6f * rumble;
                body += (raw0 - body) * 0.25f;
                float fadeIn = (float)Math.Min(1.0, t / 0.5);
                raw[i] = body * fadeIn;
            }
            return Normalize(raw, n, 0.8f);
        }

        // Снеговик разбился: глухой удар, хруст снега и звон ведра.
        public static short[] Crash()
        {
            int n = (int)(Rate * 1.4);
            var raw = new float[n];
            var rnd = new Random(11);
            float lp = 0;
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double f = 45 + 95 * Math.Exp(-t * 12);
                ph += TwoPi * f / Rate;
                float thump = (float)(Math.Sin(ph) * Math.Exp(-t * 7));
                float white = (float)(rnd.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.3f;
                float grain = rnd.NextDouble() < 0.03 ? 1f : 0.5f;
                float crunch = (white - lp) * (float)Math.Exp(-t * 5) * grain;
                float clang = 0;
                double tc = t - 0.28;
                if (tc > 0)
                    clang = (float)((Math.Sin(TwoPi * 820 * tc) + 0.6 * Math.Sin(TwoPi * 1310 * tc) + 0.3 * Math.Sin(TwoPi * 2150 * tc)) * Math.Exp(-tc * 6) * 0.22);
                raw[i] = thump * 0.9f + crunch * 0.5f + clang;
            }
            return Normalize(raw, n, 0.9f);
        }

        static double Midi(int m) { return 440.0 * Math.Pow(2, (m - 69) / 12.0); }

        static void Bell(float[] buf, double start, int midi, float amp)
        {
            double f = Midi(midi);
            int i0 = (int)(start * Rate), len = (int)(1.8 * Rate);
            for (int i = 0; i < len && i0 + i < buf.Length; i++)
            {
                double t = i / (double)Rate;
                double env = (1 - Math.Exp(-t * 200)) * Math.Exp(-t * 3.2);
                double w = Math.Sin(TwoPi * f * t) + 0.35 * Math.Sin(TwoPi * 2 * f * t) * Math.Exp(-t * 2)
                         + 0.12 * Math.Sin(TwoPi * 3 * f * t) + 0.08 * Math.Sin(TwoPi * 4.2 * f * t) * Math.Exp(-t * 8);
                buf[i0 + i] += (float)(w * env * amp);
            }
        }

        static void Bass(float[] buf, double start, int midi, float amp)
        {
            double f = Midi(midi);
            int i0 = (int)(start * Rate), len = (int)(0.9 * Rate);
            for (int i = 0; i < len && i0 + i < buf.Length; i++)
            {
                double t = i / (double)Rate;
                double env = (1 - Math.Exp(-t * 80)) * Math.Exp(-t * 4);
                buf[i0 + i] += (float)((Math.Sin(TwoPi * f * t) + 0.3 * Math.Sin(TwoPi * 2 * f * t)) * env * amp);
            }
        }

        // «В лесу родилась ёлочка» целиком: пять куплетов с разной обработкой и концовкой.
        // Играется один раз от начала до конца, без петли (на стыке петли MCI щёлкает),
        // потом пауза и снова сначала — см. SoundEngine.Song.
        static readonly int[] Mel = {
            72, 81, 81, 79, 81, 77, 72, 72,
            72, 81, 81, 82, 79, 84,
            72, 74, 74, 82, 82, 81, 79, 77,
            77, 81, 81, 79, 81, 77 };
        static readonly int[] Dur = {
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 3,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 3 };
        static readonly int[] BassBars = { 53, 53, 48, 53, 46, 48, 53, 53 };
        const double Eighth = 0.27;
        const int Verses = 5, VerseEighths = 32 + 4;   // куплет и такт паузы
        public static double MusicSeconds { get { return (Verses * VerseEighths + 6) * Eighth + 2.0; } }

        public static short[] Music()
        {
            var raw = new float[(int)((MusicSeconds + 0.5) * Rate)];
            for (int v = 0; v < Verses; v++)
            {
                double t0 = v * VerseEighths * Eighth;
                bool last = v == Verses - 1;
                double t = t0;
                for (int i = 0; i < Mel.Length; i++)
                {
                    float amp = v == 0 ? 0.45f : 0.5f;
                    Bell(raw, t, Mel[i], amp);
                    if (v == 2) Bell(raw, t, Mel[i] + 12, 0.18f);           // третий куплет — звонче, октавой выше
                    if (v == 3 && Dur[i] == 1 && i % 2 == 1) Bell(raw, t, Mel[i] - 12, 0.12f); // четвёртый — с подголоском
                    int d = Dur[i];
                    if (last && i == Mel.Length - 1) d = 6;                   // последняя нота тянется
                    t += d * Eighth;
                }
                if (v == 0) continue;                                         // первый куплет — без баса
                for (int b = 0; b < BassBars.Length; b++)
                {
                    double tb = t0 + b * 4 * Eighth;
                    Bass(raw, tb, BassBars[b], 0.25f);
                    Bass(raw, tb + 2 * Eighth, BassBars[b] + 7, 0.18f);
                }
                if (last)
                {
                    // финальный аккорд фа мажор
                    double tf = t0 + 32 * Eighth;
                    Bass(raw, tf, 41, 0.3f);
                    Bell(raw, tf + 0.03, 65, 0.25f);
                    Bell(raw, tf + 0.06, 69, 0.25f);
                }
            }
            return Normalize(raw, raw.Length, 0.75f);
        }

        public static void WriteWav(string path, short[] data)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = data.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (var s in data) w.Write(s);
            }
        }
    }

    // Проигрывание через MCI (есть в любой Windows): несколько звуков одновременно, у каждого своя громкость.
    // Все обращения к MCI идут в отдельном фоновом потоке: команда «play» иногда задерживается
    // на доли секунды, и без потока это подвешивало бы снег и мышь.
    internal sealed class SoundEngine : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);

        static readonly string[] Names = { "tractor", "crash", "music" };
        readonly Dictionary<string, bool> playing = new Dictionary<string, bool>();
        readonly Dictionary<string, int> volume = new Dictionary<string, int>();
        readonly System.Collections.Concurrent.BlockingCollection<string> queue = new System.Collections.Concurrent.BlockingCollection<string>();
        readonly System.Threading.Thread worker;

        public SoundEngine()
        {
            foreach (var n in Names) { playing[n] = false; volume[n] = -1; }
            worker = new System.Threading.Thread(Work);
            worker.IsBackground = true;
            worker.SetApartmentState(System.Threading.ApartmentState.STA); // проигрыватель MCI работает только в STA
            worker.Start();
        }

        void Work()
        {
            bool ok = true;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Snowdrift");
                Directory.CreateDirectory(dir);
                Make(dir, "tractor", Synth.Tractor());
                Make(dir, "crash", Synth.Crash());
                Make(dir, "music", Synth.Music());
            }
            catch (Exception) { ok = false; }
            foreach (var cmd in queue.GetConsumingEnumerable())
                if (ok) Send(cmd);
            if (ok) foreach (var n in Names) mciSendString("close sd_" + n, null, 0, IntPtr.Zero);
        }

        static void Make(string dir, string name, short[] data)
        {
            string path = Path.Combine(dir, name + ".wav");
            Synth.WriteWav(path, data);
            Send("open \"" + path + "\" type mpegvideo alias sd_" + name);
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int err, StringBuilder text, int len);

        // Отправить команду; ошибки пишем в %APPDATA%\Snowdrift\sound.log
        static void Send(string cmd)
        {
            int err = mciSendString(cmd, null, 0, IntPtr.Zero);
            if (err == 0) return;
            try
            {
                var sb = new StringBuilder(256);
                mciGetErrorString(err, sb, sb.Capacity);
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "sound.log"), DateTime.Now + "  " + cmd + "  -> " + err + " " + sb + "\r\n");
            }
            catch (Exception) { }
        }

        void Mci(string cmd)
        {
            if (!queue.IsAddingCompleted) queue.Add(cmd);
        }

        void SetVolume(string name, int v)
        {
            v = Math.Max(0, Math.Min(1000, v));
            if (volume[name] == v) return;
            volume[name] = v;
            Mci("setaudio sd_" + name + " volume to " + v);
        }

        // Зацикленный звук: включить/выключить и задать громкость 0..1000.
        public void Loop(string name, bool on, int vol)
        {
            if (on) SetVolume(name, vol);
            if (on && !playing[name]) { Mci("play sd_" + name + " repeat"); playing[name] = true; }
            else if (!on && playing[name]) { Mci("pause sd_" + name); playing[name] = false; }
        }

        // Звук на одну поездку: запускается с начала, без повтора.
        public void Run(string name, bool on, int vol)
        {
            if (on) SetVolume(name, vol);
            if (on && !playing[name]) { Mci("play sd_" + name + " from 0"); playing[name] = true; }
            else if (!on && playing[name]) { Mci("stop sd_" + name); playing[name] = false; }
        }

        // Песня: играет до конца, после паузы начинается сначала. Петли MCI нет, поэтому нет и щелчка.
        readonly System.Diagnostics.Stopwatch songClock = new System.Diagnostics.Stopwatch();
        public void Song(string name, bool on, int vol, double period)
        {
            if (on)
            {
                SetVolume(name, vol);
                if (!playing[name] || songClock.Elapsed.TotalSeconds > period)
                {
                    Mci("play sd_" + name + " from 0");
                    playing[name] = true;
                    songClock.Reset();
                    songClock.Start();
                }
            }
            else if (playing[name]) { Mci("stop sd_" + name); playing[name] = false; }
        }

        public void Shot(string name, int vol)
        {
            SetVolume(name, vol);
            Mci("play sd_" + name + " from 0");
        }

        public void StopAll()
        {
            foreach (var n in Names)
                if (playing[n]) { Mci("stop sd_" + n); playing[n] = false; }
        }

        public void Dispose()
        {
            StopAll();
            queue.CompleteAdding();
            worker.Join(1000);
        }
    }
}
