using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Snowdrift
{
    internal static class L
    {
        static readonly bool ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        public static string T(string rus, string eng) { return ru ? rus : eng; }
    }

    internal sealed class Settings
    {
        public bool Enabled = true;
        public int Intensity = 2;
        public bool OnWindows = true;
        public bool Tractor = true;
        public int TractorEvery = 0;
        public bool Snowman = true;
        public bool Sound = true;
        public int Volume = 50;
        public bool SndMusic = false;
        public bool SndTractor = true;
        public bool SndSnowman = true;
        public bool FirstRun = true;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snowdrift"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "enabled") s.Enabled = v == "1";
                    else if (k == "intensity") { int i; if (int.TryParse(v, out i) && i >= 1 && i <= 3) s.Intensity = i; }
                    else if (k == "windows") s.OnWindows = v == "1";
                    else if (k == "tractor") s.Tractor = v == "1";
                    else if (k == "tractorevery") { int m; if (int.TryParse(v, out m) && m >= 0) s.TractorEvery = m; }
                    else if (k == "snowman") s.Snowman = v == "1";
                    else if (k == "sound") s.Sound = v == "1";
                    else if (k == "volume") { int vv; if (int.TryParse(v, out vv) && vv >= 0 && vv <= 100) s.Volume = vv; }
                    else if (k == "sndmusic") s.SndMusic = v == "1";
                    else if (k == "sndtractor") s.SndTractor = v == "1";
                    else if (k == "sndsnowman") s.SndSnowman = v == "1";
                    else if (k == "firstrun") s.FirstRun = v == "1";
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath,
                    "enabled=" + (Enabled ? 1 : 0) + "\r\n" +
                    "intensity=" + Intensity + "\r\n" +
                    "windows=" + (OnWindows ? 1 : 0) + "\r\n" +
                    "tractor=" + (Tractor ? 1 : 0) + "\r\n" +
                    "tractorevery=" + TractorEvery + "\r\n" +
                    "snowman=" + (Snowman ? 1 : 0) + "\r\n" +
                    "sound=" + (Sound ? 1 : 0) + "\r\n" +
                    "volume=" + Volume + "\r\n" +
                    "sndmusic=" + (SndMusic ? 1 : 0) + "\r\n" +
                    "sndtractor=" + (SndTractor ? 1 : 0) + "\r\n" +
                    "sndsnowman=" + (SndSnowman ? 1 : 0) + "\r\n" +
                    "firstrun=" + (FirstRun ? 1 : 0) + "\r\n");
            }
            catch (Exception) { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Autostart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue("Snowdrift") != null;
                }
                catch (Exception) { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    {
                        if (k == null) return;
                        if (value) k.SetValue("Snowdrift", "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue("Snowdrift", false);
                    }
                }
                catch (Exception) { }
            }
        }
    }

    internal static class Res
    {
        public static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        public static string Text(string name)
        {
            using (var s = Open(name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd().Trim();
            }
        }

        public static Icon AppIcon(Size size)
        {
            try
            {
                using (var s = Open("snowdrift.ico"))
                    if (s != null) return new Icon(s, size);
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }
    }

    internal sealed class App : ApplicationContext
    {
        public const string Version = "0.10.7";
        public const string RepoUrl = ""; // появится после публикации на GitHub

        readonly World world = new World();
        readonly Settings st;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly HashSet<IntPtr> overlayHandles = new HashSet<IntPtr>();
        SynchronizationContext ui;
        double last, lastSnap, lastTop, lastWork;
        bool locked;
        ToolStripMenuItem miEnabled, miWindows, miTractor, miAutostart;
        ToolStripMenuItem[] miIntensity;
        DonateForm donate;
        Native.LowLevelMouseProc mouseProc;
        IntPtr mouseHook;
        bool dragging;
        SoundEngine sound;
        int lastCrash;
        float tractorGain;

        public App()
        {
            st = Settings.Load();
            world.Intensity = st.Intensity;
            world.SettleOnWindows = st.OnWindows;
            world.TractorOn = st.Tractor;
            world.TractorEvery = st.TractorEvery;
            world.SnowmanOn = st.Snowman;

            BuildScreens();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            tray = new NotifyIcon();
            tray.Icon = Res.AppIcon(SystemInformation.SmallIconSize);
            tray.Text = L.T("Сугроб — снег на рабочем столе", "Snowdrift — snow on your desktop");
            tray.ContextMenuStrip = BuildMenu();
            tray.DoubleClick += delegate { SetEnabled(!st.Enabled); };
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 30;
            timer.Tick += OnTick;
            if (st.Enabled) timer.Start();

            mouseProc = OnMouse;
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            if (st.FirstRun)
            {
                tray.ShowBalloonTip(5000, L.T("Сугроб запущен", "Snowdrift is running"),
                    L.T("Снежинка у часов — меню: сила снегопада, трактор, автозапуск.",
                        "The snowflake icon near the clock opens the menu."), ToolTipIcon.Info);
                st.FirstRun = false;
                st.Save();
            }
        }

        // Мышь: нажатие на снеговика перехватываем и тащим его, остальные клики проходят как обычно.
        IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && st.Enabled && !locked)
                {
                    int msg = wParam.ToInt32();
                    var info = (MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (msg == 0x201 && !dragging && world.TryGrabSnowman(info.pt.X, info.pt.Y))
                    {
                        dragging = true;
                        return new IntPtr(1);
                    }
                    if (msg == 0x200 && dragging) world.DragSnowman(info.pt.X, info.pt.Y);
                    if (msg == 0x202 && dragging)
                    {
                        dragging = false;
                        world.DropSnowman(info.pt.X, info.pt.Y);
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception) { }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            ui.Post(delegate { BuildScreens(); }, null);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) locked = false;
        }

        void BuildScreens()
        {
            foreach (var sc in world.Screens)
                if (sc.Overlay != null) { sc.Overlay.Close(); sc.Overlay.Dispose(); }
            overlayHandles.Clear();

            var list = new List<ScreenInfo>();
            foreach (var s in Screen.AllScreens)
            {
                var sc = new ScreenInfo();
                sc.Bounds = s.Bounds;
                sc.Scale = Native.DpiScaleAt(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2);
                sc.Overlay = new Overlay(s.Bounds);
                sc.Overlay.Show();
                sc.Overlay.InitSurface();
                overlayHandles.Add(sc.Overlay.Handle);
                list.Add(sc);
            }
            world.SetScreens(list);
            lastSnap = 0;
            lastWork = -10;
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();

            miEnabled = new ToolStripMenuItem(L.T("Снег идёт", "Snow is falling"));
            miEnabled.Checked = st.Enabled;
            miEnabled.Click += delegate { SetEnabled(!st.Enabled); };
            m.Items.Add(miEnabled);

            var miInt = new ToolStripMenuItem(L.T("Снегопад", "Snowfall"));
            string[] names = { L.T("Лёгкий", "Light"), L.T("Обычный", "Normal"), L.T("Метель", "Blizzard") };
            miIntensity = new ToolStripMenuItem[3];
            for (int i = 0; i < 3; i++)
            {
                int level = i + 1;
                var it = new ToolStripMenuItem(names[i]);
                it.Checked = st.Intensity == level;
                it.Click += delegate
                {
                    st.Intensity = level;
                    world.Intensity = level;
                    for (int j = 0; j < 3; j++) miIntensity[j].Checked = j + 1 == level;
                    st.Save();
                };
                miIntensity[i] = it;
                miInt.DropDownItems.Add(it);
            }
            m.Items.Add(miInt);

            miWindows = new ToolStripMenuItem(L.T("Снег копится на окнах", "Snow piles up on windows"));
            miWindows.Checked = st.OnWindows;
            miWindows.Click += delegate
            {
                st.OnWindows = !st.OnWindows;
                world.SettleOnWindows = st.OnWindows;
                miWindows.Checked = st.OnWindows;
                st.Save();
            };
            m.Items.Add(miWindows);

            miTractor = new ToolStripMenuItem(L.T("Трактор чистит панель задач", "Tractor clears the taskbar"));
            miTractor.Checked = st.Tractor;
            miTractor.Click += delegate
            {
                st.Tractor = !st.Tractor;
                world.TractorOn = st.Tractor;
                miTractor.Checked = st.Tractor;
                st.Save();
            };
            m.Items.Add(miTractor);

            var miEvery = new ToolStripMenuItem(L.T("Трактор приезжает", "Tractor comes"));
            int[] mins = { 0, 5, 15, 30, 60 };
            string[] minNames = { L.T("Когда много снега", "When there is a lot of snow"), L.T("Раз в 5 минут", "Every 5 minutes"),
                L.T("Раз в 15 минут", "Every 15 minutes"), L.T("Раз в 30 минут", "Every 30 minutes"), L.T("Раз в час", "Every hour") };
            var everyItems = new ToolStripMenuItem[mins.Length];
            for (int i = 0; i < mins.Length; i++)
            {
                int val = mins[i];
                var it = new ToolStripMenuItem(minNames[i]);
                it.Checked = st.TractorEvery == val;
                it.Click += delegate
                {
                    st.TractorEvery = val;
                    world.TractorEvery = val;
                    foreach (var x in everyItems) x.Checked = false;
                    it.Checked = true;
                    st.Save();
                };
                everyItems[i] = it;
                miEvery.DropDownItems.Add(it);
            }
            m.Items.Add(miEvery);

            var miSnowman = new ToolStripMenuItem(L.T("Снеговик", "Snowman"));
            miSnowman.Checked = st.Snowman;
            miSnowman.Click += delegate
            {
                st.Snowman = !st.Snowman;
                world.SnowmanOn = st.Snowman;
                miSnowman.Checked = st.Snowman;
                st.Save();
            };
            m.Items.Add(miSnowman);

            var miCall = new ToolStripMenuItem(L.T("Вызвать трактор", "Call the tractor"));
            miCall.Click += delegate { if (st.Enabled) world.CallTractor(); };
            m.Items.Add(miCall);

            m.Items.Add(new ToolStripSeparator());

            miAutostart = new ToolStripMenuItem(L.T("Запускать вместе с Windows", "Start with Windows"));
            miAutostart.Checked = Settings.Autostart;
            miAutostart.Click += delegate
            {
                Settings.Autostart = !Settings.Autostart;
                miAutostart.Checked = Settings.Autostart;
            };
            m.Items.Add(miAutostart);

            m.Items.Add(BuildSoundMenu());

            var miDonate = new ToolStripMenuItem(L.T("Поддержать автора…", "Support the author…"));
            miDonate.Click += delegate { ShowDonate(); };
            m.Items.Add(miDonate);

            m.Items.Add(new ToolStripSeparator());

            var miExit = new ToolStripMenuItem(L.T("Выход", "Exit"));
            miExit.Click += delegate { ExitThread(); };
            m.Items.Add(miExit);
            return m;
        }

        ToolStripMenuItem Toggle(string text, bool value, Action<bool> set)
        {
            var it = new ToolStripMenuItem(text);
            it.Checked = value;
            it.Click += delegate
            {
                it.Checked = !it.Checked;
                set(it.Checked);
                st.Save();
                UpdateSound();
            };
            return it;
        }

        ToolStripMenuItem BuildSoundMenu()
        {
            var root = new ToolStripMenuItem(L.T("Звук", "Sound"));
            root.DropDownItems.Add(Toggle(L.T("Звук включён", "Sound on"), st.Sound, delegate(bool v) { st.Sound = v; }));
            var vol = new ToolStripMenuItem(L.T("Громкость", "Volume"));
            int[] levels = { 25, 50, 75, 100 };
            var items = new ToolStripMenuItem[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                int lv = levels[i];
                var it = new ToolStripMenuItem(lv + " %");
                it.Checked = st.Volume == lv;
                it.Click += delegate
                {
                    st.Volume = lv;
                    foreach (var x in items) x.Checked = false;
                    it.Checked = true;
                    st.Save();
                    UpdateSound();
                };
                items[i] = it;
                vol.DropDownItems.Add(it);
            }
            root.DropDownItems.Add(vol);
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(Toggle(L.T("Музыка «В лесу родилась ёлочка»", "Music: \"A Little Fir Tree\""), st.SndMusic, delegate(bool v) { st.SndMusic = v; }));
            root.DropDownItems.Add(Toggle(L.T("Трактор", "Tractor"), st.SndTractor, delegate(bool v) { st.SndTractor = v; }));
            root.DropDownItems.Add(Toggle(L.T("Снеговик разбивается", "Snowman crash"), st.SndSnowman, delegate(bool v) { st.SndSnowman = v; }));
            return root;
        }

        int Vol(int baseVol) { return baseVol * st.Volume / 100; }

        void UpdateSound()
        {
            bool active = st.Sound && st.Enabled && !locked;
            if (active && sound == null) sound = new SoundEngine();
            if (sound == null) return;
            if (!active) { sound.StopAll(); tractorGain = 0; return; }
            bool visible = false;
            foreach (var sc in world.Screens) if (!sc.Paused) visible = true;
            // трактор: плавно нарастает при выезде и плавно стихает, когда уехал
            float target = (visible && st.SndTractor && world.AnyTractor) ? 1f : 0f;
            tractorGain += Math.Sign(target - tractorGain) * 0.06f;
            tractorGain = Math.Max(0f, Math.Min(1f, tractorGain));
            if (Math.Abs(target - tractorGain) < 0.06f) tractorGain = target;
            sound.Run("tractor", tractorGain > 0.001f, (int)(Vol(650) * tractorGain));
            sound.Song("music", visible && st.SndMusic, Vol(450), Synth.MusicSeconds + 30); // вся песня, потом 30 с тишины
            if (world.CrashCount != lastCrash)
            {
                lastCrash = world.CrashCount;
                if (visible && st.SndSnowman) sound.Shot("crash", Vol(1000));
            }
        }

        void ShowDonate()
        {
            if (donate == null || donate.IsDisposed) donate = new DonateForm();
            donate.Show();
            donate.Activate();
        }

        void SetEnabled(bool on)
        {
            st.Enabled = on;
            miEnabled.Checked = on;
            st.Save();
            if (on)
            {
                foreach (var sc in world.Screens) sc.Overlay.ShownEmpty = false;
                last = sw.Elapsed.TotalSeconds;
                timer.Start();
            }
            else
            {
                timer.Stop();
                foreach (var sc in world.Screens) sc.Overlay.PresentEmpty();
                UpdateSound();
            }
        }

        void OnTick(object sender, EventArgs e)
        {
            double now = sw.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, Math.Max(0, now - last));
            last = now;
            UpdateSound();
            if (locked) return;

            if (now - lastSnap > (world.NeedFastSnapshot ? 0.03 : 0.12))
            {
                lastSnap = now;
                if (now - lastWork > 1)
                {
                    lastWork = now;
                    foreach (var sc in world.Screens) world.SetWorkArea(sc, Screen.FromRectangle(sc.Bounds).WorkingArea);
                }
                world.UpdateWindows(WindowTracker.Snapshot(overlayHandles, false));
                int quns;
                bool globalPause = Native.SHQueryUserNotificationState(out quns) == 0 && (quns == 3 || quns == 4);
                foreach (var sc in world.Screens)
                {
                    // любая полноэкранная пауза (окно на весь экран, «3D»-режим, презентация) — снег не идёт,
                    // а снеговик остаётся, если включена настройка
                    sc.Fullscreen = globalPause || WindowTracker.IsFullscreenOn(sc.Bounds, overlayHandles);
                    sc.Paused = sc.Fullscreen;
                }
            }
            if (now - lastTop > 2)
            {
                lastTop = now;
                foreach (var sc in world.Screens) sc.Overlay.KeepOnTop();
            }

            world.Step(dt);

            foreach (var sc in world.Screens)
            {
                if (sc.Paused)
                {
                    sc.Overlay.PresentEmpty();
                    continue;
                }
                sc.Overlay.ShownEmpty = false;
                var g = sc.Overlay.Begin();
                world.RenderBack(sc, g);
                g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
                world.RasterFlakes(sc, sc.Overlay.Bits);
                world.RenderFront(sc, g);
                sc.Overlay.Present();
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (sound != null) sound.Dispose();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            tray.Visible = false;
            tray.Dispose();
            foreach (var sc in world.Screens)
                if (sc.Overlay != null) { sc.Overlay.Close(); sc.Overlay.Dispose(); }
            base.ExitThreadCore();
        }
    }
}
