// MacKeys — Mac-style brightness keys for Windows.
//
// F1 = darker, F2 = brighter, applied to the screen under the mouse cursor.
// External monitors via DDC/CI (dxva2), built-in laptop panel via WMI.
// The overlay mimics the Windows 11 volume flyout: slides up from behind the taskbar,
// updates in place, and slides back down 2 s after the last press.
//
// Runs in the background with a tray icon (Start with Windows / Exit).
// The rest of the Mac-style shortcuts live in the PowerToys Keyboard Manager config (see /config).
//
// MIT License — see LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacKeys
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dx, out uint dy);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);

        // SendInput
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

        // Raw input
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
        [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        // Low-level keyboard hook
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr OpenEvent(uint access, bool inherit, string name);
        [DllImport("kernel32.dll")] public static extern bool SetEvent(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint n);
        [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] public static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetMonitorBrightness(IntPtr h, uint value);
    }

    // ---------------- Brightness backend ----------------
    static class Backend
    {
        // Returns 0..100, or -1 when the monitor supports neither DDC/CI nor WMI.
        public static int Get(IntPtr hMon)
        {
            int v = -1;
            WithDdc(hMon, (h, min, cur, max) => { v = (int)Math.Round((cur - min) * 100.0 / (max - min)); });
            if (v >= 0) return v;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public static void Set(IntPtr hMon, int pct)
        {
            bool done = false;
            WithDdc(hMon, (h, min, cur, max) => { done |= Native.SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * pct / 100.0)); });
            if (done) return;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct }); break; }
            }
            catch { }
        }

        delegate void DdcAction(IntPtr h, uint min, uint cur, uint max);
        static void WithDdc(IntPtr hMon, DdcAction act)
        {
            uint n; if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) return;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return;
            try
            {
                foreach (var pm in arr)
                {
                    uint min, cur, max;
                    if (Native.GetMonitorBrightness(pm.h, out min, out cur, out max) && max > min) act(pm.h, min, cur, max);
                }
            }
            finally { Native.DestroyPhysicalMonitors(n, arr); }
        }
    }

    // All monitor I/O runs on one background thread (DDC/CI is slow and must not be used concurrently),
    // so the animation never stutters. Only the latest value per monitor is sent.
    // It also keeps a cache of each monitor's brightness so the overlay can appear instantly.
    class Applier
    {
        readonly Dictionary<IntPtr, int> pending = new Dictionary<IntPtr, int>();
        readonly HashSet<IntPtr> refresh = new HashSet<IntPtr>();
        readonly Dictionary<IntPtr, int> known = new Dictionary<IntPtr, int>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);

        public Applier()
        {
            var t = new Thread(Loop); t.IsBackground = true; t.Start();
        }

        public void Request(IntPtr hMon, int pct)
        {
            lock (pending) { pending[hMon] = pct; refresh.Remove(hMon); }
            lock (known) known[hMon] = pct;
            signal.Set();
        }

        public void Refresh(IntPtr hMon) { lock (pending) { if (!pending.ContainsKey(hMon)) refresh.Add(hMon); } signal.Set(); }

        public bool TryGetKnown(IntPtr hMon, out int pct) { lock (known) return known.TryGetValue(hMon, out pct); }

        void Loop()
        {
            while (true)
            {
                signal.WaitOne();
                while (true)
                {
                    IntPtr mon; int pct = -1; bool isSet;
                    lock (pending)
                    {
                        if (pending.Count > 0) { var e = pending.GetEnumerator(); e.MoveNext(); mon = e.Current.Key; pct = e.Current.Value; pending.Remove(mon); isSet = true; }
                        else if (refresh.Count > 0) { var e = refresh.GetEnumerator(); e.MoveNext(); mon = e.Current; refresh.Remove(mon); isSet = false; }
                        else break;
                    }
                    try
                    {
                        if (isSet) Backend.Set(mon, pct);
                        else
                        {
                            int v = Backend.Get(mon);
                            bool changedMeanwhile; lock (pending) changedMeanwhile = pending.ContainsKey(mon);
                            if (v >= 0 && !changedMeanwhile) lock (known) known[mon] = v;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    // ---------------- Overlay ----------------
    class Osd : Form
    {
        // Geometry measured from the Windows 11 volume flyout (at 100% scale).
        const float W = 192, H = 46, GAP = 16, RADIUS = 8, PAD = 12; // PAD = room for the shadow
        const double SHOW_MS = 280, HIDE_MS = 130, STAY_MS = 2000, FILL_MS = 120;
        const int STEP = 10;
        const uint VK_F1 = 0x70, VK_F2 = 0x71;

        readonly Applier applier = new Applier();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();

        IntPtr curMon = IntPtr.Zero;
        Native.RECT work; float scale = 1f;
        int value = -1;                       // target percent for curMon
        double fillFrom, fillTo, fillStart;   // bar fill animation
        double pFrom, pStart; int mode;       // slide: 0 hidden, 1 showing, 2 shown, 3 hiding
        double lastPress;
        bool visible;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            timer.Interval = 10; timer.Tick += (o, e) => Frame();
            // Keep the brightness cache fresh (e.g. after using the monitor's own buttons) while the overlay is hidden.
            RefreshAll();
            refreshTimer.Interval = 20000; refreshTimer.Tick += (o, e) => { if (mode == 0) RefreshAll(); }; refreshTimer.Start();
            SystemEvents.DisplaySettingsChanged += (o, e) => RefreshAll();
        }

        void RefreshAll()
        {
            foreach (var scr in Screen.AllScreens)
            {
                var b = scr.Bounds;
                applier.Refresh(Native.MonitorFromPoint(new Native.POINT(b.Left + b.Width / 2, b.Top + b.Height / 2), 2));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 /*LAYERED*/ | 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/ | 0x00000008 /*TOPMOST*/ | 0x00000020 /*TRANSPARENT*/;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void SetVisibleCore(bool v) { base.SetVisibleCore(false); } // shown via SetWindowPos only

        bool hotkeys;
        public bool HotkeysEnabled
        {
            get { return hotkeys; }
            set
            {
                if (value == hotkeys) return;
                hotkeys = value;
                if (value) { Native.RegisterHotKey(Handle, 1, 0, VK_F1); Native.RegisterHotKey(Handle, 2, 0, VK_F2); }
                else { Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2); }
            }
        }

        public event EventHandler DevicesChanged;
        public KeyWatch Keys; // set by Program; receives WM_INPUT

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 /*WM_HOTKEY*/) { OnKey(m.WParam.ToInt32() == 1 ? -STEP : STEP); return; }
            if (m.Msg == 0x0219 /*WM_DEVICECHANGE*/ && DevicesChanged != null) DevicesChanged(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        void OnKey(int step)
        {
            Native.POINT p; Native.GetCursorPos(out p);
            IntPtr hMon = Native.MonitorFromPoint(p, 2);
            bool fresh = hMon != curMon || mode == 0 || mode == 3;

            if (hMon != curMon || mode == 0 || value < 0)
            {
                // Use the cached value (kept fresh in the background); read synchronously only the very first time.
                int v;
                if (!applier.TryGetKnown(hMon, out v)) v = Backend.Get(hMon);
                if (v < 0) return;
                if (hMon != curMon && mode != 0) mode = 0; // jumped to another screen: restart the slide there
                curMon = hMon; value = v;
                fillFrom = fillTo = v; fillStart = Now;
                var mi = new Native.MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                Native.GetMonitorInfo(hMon, ref mi); work = mi.rcWork;
                uint dx = 96, dy; try { Native.GetDpiForMonitor(hMon, 0, out dx, out dy); } catch { }
                scale = dx / 96f;
            }

            int target = (int)Math.Max(0, Math.Min(100, Math.Round(value / (double)STEP) * STEP + step));
            fillFrom = CurrentFill(); fillTo = target; fillStart = Now;
            value = target;
            applier.Request(curMon, target);

            lastPress = Now;
            if (fresh) { pFrom = SlidePos(); pStart = Now; mode = 1; }
            if (!visible)
            {
                visible = true;
                Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // TOPMOST; NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW
            }
            Frame();
            timer.Start();
        }

        static double EaseOut(double t) { t = Math.Max(0, Math.Min(1, t)); return 1 - Math.Pow(1 - t, 3); }
        static double EaseIn(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * t; }

        double SlidePos()
        {
            double t = Now;
            switch (mode)
            {
                case 1: return pFrom + (1 - pFrom) * EaseOut((t - pStart) / SHOW_MS);
                case 2: return 1;
                case 3: return pFrom * (1 - EaseIn((t - pStart) / HIDE_MS));
                default: return 0;
            }
        }

        double CurrentFill() { return fillFrom + (fillTo - fillFrom) * EaseOut((Now - fillStart) / FILL_MS); }

        void Frame()
        {
            double t = Now;
            if (mode == 1 && t - pStart >= SHOW_MS) mode = 2;
            if ((mode == 1 || mode == 2) && t - lastPress >= STAY_MS) { pFrom = SlidePos(); pStart = t; mode = 3; }
            if (mode == 3 && t - pStart >= HIDE_MS)
            {
                mode = 0; timer.Stop(); visible = false;
                Native.ShowWindow(Handle, 0);
                applier.Refresh(curMon);
                return;
            }
            Render(SlidePos(), CurrentFill());
        }

        // ---------- drawing ----------
        void Render(double slide, double fill)
        {
            float s = scale;
            int bw = (int)Math.Ceiling((W + 2 * PAD) * s);
            int bh = (int)Math.Ceiling((PAD + H + GAP) * s);   // the window ends exactly at the taskbar top
            int x = work.L + (work.R - work.L - bw) / 2;
            int y = work.B - bh;
            float travel = (H + GAP) * s;
            float top = PAD * s + (float)((1 - slide) * travel);

            bool light = Theme.IsLight();
            Color bg = light ? Color.FromArgb(236, 243, 243, 243) : Color.FromArgb(240, 40, 40, 40);
            Color border = light ? Color.FromArgb(28, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255);
            Color fg = light ? Color.FromArgb(228, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
            Color track = light ? Color.FromArgb(114, 0, 0, 0) : Color.FromArgb(139, 255, 255, 255);
            Color accent = Theme.Accent(light);

            using (var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var card = new RectangleF(PAD * s, top, W * s, H * s);

                    // soft shadow
                    for (int i = 6; i >= 1; i--)
                    {
                        var r = card; r.Inflate(i * s, i * s); r.Offset(0, 2 * s);
                        using (var path = Rounded(r, (RADIUS + i) * s))
                        using (var b = new SolidBrush(Color.FromArgb(light ? 6 : 12, 0, 0, 0))) g.FillPath(b, path);
                    }
                    using (var path = Rounded(card, RADIUS * s))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                    }

                    float cy = card.Top + card.Height / 2f;
                    // sun icon
                    using (var f = new Font(Theme.IconFont, 16f * s, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(fg))
                    {
                        var sz = g.MeasureString("", f, PointF.Empty, StringFormat.GenericTypographic);
                        g.DrawString("", f, b, card.Left + 13 * s, cy - sz.Height / 2f, StringFormat.GenericTypographic);
                    }
                    // bar
                    float x0 = card.Left + 44 * s, x1 = card.Left + 150 * s, th = 4 * s;
                    using (var p = new Pen(track, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x0, cy, x1, cy);
                    if (fill > 0.5)
                        using (var p = new Pen(accent, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, x0, cy, x0 + (x1 - x0) * (float)(fill / 100.0), cy);
                    // number
                    using (var f = new Font("Segoe UI Variable Text", 14f * s, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(fg))
                    {
                        string txt = value.ToString();
                        var sz = g.MeasureString(txt, f, PointF.Empty, StringFormat.GenericTypographic);
                        g.DrawString(txt, f, b, card.Right - 12 * s - sz.Width, cy - sz.Height / 2f, StringFormat.GenericTypographic);
                    }
                }
                Push(bmp, x, y);
            }
        }

        void Push(Bitmap bmp, int x, int y)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hb);
            try
            {
                var size = new Native.SIZE(bmp.Width, bmp.Height); var src = new Native.POINT(0, 0); var dst = new Native.POINT(x, y);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.SelectObject(mem, old); Native.DeleteObject(hb); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // ---------------- Theme helpers ----------------
    static class Theme
    {
        const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsLight() { return ReadFlag("SystemUsesLightTheme"); }

        static bool ReadFlag(string name)
        {
            try { var v = Registry.GetValue(Personalize, name, 1); return v == null || (int)v != 0; }
            catch { return true; }
        }

        // Windows uses a darker accent shade on light backgrounds and a lighter one on dark backgrounds.
        public static Color Accent(bool light)
        {
            try
            {
                var pal = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) as byte[];
                if (pal != null && pal.Length >= 32) { int i = light ? 4 * 4 : 4 * 1; return Color.FromArgb(255, pal[i], pal[i + 1], pal[i + 2]); }
            }
            catch { }
            return light ? Color.FromArgb(0, 95, 184) : Color.FromArgb(96, 205, 255);
        }

        static string iconFont;
        public static string IconFont
        {
            get
            {
                if (iconFont == null)
                {
                    iconFont = "Segoe MDL2 Assets";
                    using (var fc = new InstalledFontCollection())
                        foreach (var ff in fc.Families) if (ff.Name == "Segoe Fluent Icons") iconFont = ff.Name;
                }
                return iconFont;
            }
        }
    }

    // ---------------- Settings (HKCU\Software\MacKeys) ----------------
    enum Mode { Auto, On, Off }

    static class Settings
    {
        const string Key = @"Software\MacKeys";

        public static Mode Mode
        {
            get { var s = Read("Mode", "Auto"); return s == "On" ? Mode.On : s == "Off" ? Mode.Off : Mode.Auto; }
            set { Write("Mode", value.ToString()); }
        }

        static string Read(string name, string def)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { var v = k == null ? null : k.GetValue(name) as string; return v ?? def; } }
            catch { return def; }
        }
        static void Write(string name, string value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value); } catch { }
        }
    }

    // ---------------- Apple keyboard detection ----------------
    static class AppleKeyboard
    {
        // Apple's USB vendor id is 05AC; over Bluetooth Apple devices report the Bluetooth SIG company id 004C.
        static readonly string[] Ids = { "VID_05AC", "VID&000205AC", "VID&0001004C", "VID&0002004C" };

        public static bool IsConnected()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceID, Status FROM Win32_PnPEntity WHERE PNPClass = 'Keyboard'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var id = (Convert.ToString(o["DeviceID"]) ?? "").ToUpperInvariant();
                        if (Convert.ToString(o["Status"]) != "OK") continue;
                        foreach (var v in Ids) if (id.Contains(v)) return true;
                    }
            }
            catch { }
            return false;
        }
    }

    // ---------------- PowerToys Keyboard Manager on/off ----------------
    // "Off" = an empty remap config. The real config is stashed next to MacKeys.exe and restored for "on".
    // PowerToys reloads its config when this named event is signalled (the Keyboard Manager editor does the same).
    static class PowerToysKm
    {
        static readonly string Config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\PowerToys\Keyboard Manager\default.json");
        static string Stash { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "keyboard-manager.json"); } }
        const string Empty = "{\"remapKeys\":{\"inProcess\":[]},\"remapKeysToText\":{\"inProcess\":[]},\"remapShortcuts\":{\"global\":[],\"appSpecific\":[]},\"remapShortcutsToText\":{\"global\":[],\"appSpecific\":[]}}";

        public static void Apply(bool on)
        {
            try
            {
                if (!File.Exists(Config)) return; // PowerToys not set up
                string cur = File.ReadAllText(Config);
                bool curEmpty = !cur.Contains("originalKeys");
                if (on)
                {
                    if (!curEmpty || !File.Exists(Stash)) return;
                    File.WriteAllText(Config, File.ReadAllText(Stash));
                }
                else
                {
                    if (curEmpty) return;
                    File.WriteAllText(Stash, cur); // keep whatever the user has configured
                    File.WriteAllText(Config, Empty);
                }
                Reload();
            }
            catch { }
        }

        static void Reload()
        {
            IntPtr h = Native.OpenEvent(0x0002 /*EVENT_MODIFY_STATE*/, false, "PowerToys_KeyboardManager_Event_Settings");
            if (h == IntPtr.Zero) return;
            Native.SetEvent(h); Native.CloseHandle(h);
            // Any modifier left "down" by the switch is released by KeyWatch's watchdog.
        }
    }

    // ---------------- Lock ----------------
    // ⌃⌘Q runs "MacKeys.exe --lock". (An earlier version could disable Win+L so ⌘L reached apps; Windows still
    // swallows L while the physical Win key is down, and every workaround left modifiers stuck, so it was removed.
    // CleanUpOldCmdL undoes what that version may have left behind.)
    static class LockPolicy
    {
        public static void LockNow() { Native.LockWorkStation(); }

        public static void CleanUpOldCmdL()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System",
                           RegistryKeyPermissionCheck.ReadWriteSubTree,
                           System.Security.AccessControl.RegistryRights.ReadKey | System.Security.AccessControl.RegistryRights.SetValue))
                    if (k != null) k.DeleteValue("DisableLockWorkstation", false);
            }
            catch { }
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\MacKeys", true)) if (k != null) k.DeleteValue("AllowCmdL", false); } catch { }
        }
    }

    // ---------------- Log (next to MacKeys.exe) ----------------
    static class Log
    {
        static readonly object gate = new object();
        static string PathName { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mackeys.log"); } }

        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    var p = PathName;
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ---------------- Physical keyboard state, stuck-key watchdog ----------------
    // A low-level keyboard hook that sits FIRST in the hook chain (before PowerToys) sees every physical key,
    // including ⌘/⌃, which PowerToys swallows and replaces. (Raw Input does not report keys a hook swallowed,
    // so an earlier version could not see ⌘ and released modifiers the user was holding.)
    // Windows calls the most recently installed hook first, so MacKeys re-installs its hook whenever the
    // PowerToys Keyboard Manager engine (re)starts. If it ever sees a modifier pressed with no physical key
    // behind it, it assumes it is missing input and stays hands-off for a while.
    class KeyWatch
    {
        const long OurTag = 1; // dwExtraInfo PowerToys Keyboard Manager treats as "already processed" and ignores
        static readonly byte[] Modifiers = { 0x5B, 0x5C, 0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1 };
        static readonly string[] ModNames = { "LWin", "RWin", "LCtrl", "RCtrl", "LAlt", "RAlt", "LShift", "RShift" };
        const double StuckMs = 1000; // how long Windows must report a modifier down with nothing physically held

        readonly HashSet<uint> held = new HashSet<uint>();   // physically held modifiers (vk)
        readonly HashSet<uint> seen = new HashSet<uint>();   // for the log
        readonly System.Windows.Forms.Timer watchdog = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer ptWatch = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer rehook = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly double[] downSince = new double[Modifiers.Length];
        readonly Native.HookProc proc;                         // keep the delegate alive
        IntPtr hook;
        int ptPid = -1;
        double lastPhys = -1e9, lastModUp = -1e9, distrustUntil;

        public KeyWatch(Osd osd)
        {
            osd.Keys = this;
            proc = Callback;
            Install("start");
            watchdog.Interval = 150; watchdog.Tick += (o, e) => Check(); watchdog.Start();
            rehook.Interval = 1500; rehook.Tick += (o, e) => { rehook.Stop(); Install("PowerToys (re)started"); };
            ptWatch.Interval = 2000; ptWatch.Tick += (o, e) => WatchPowerToys(); ptWatch.Start();
            ptPid = PowerToysPid();
            SystemEvents.SessionSwitch += (o, e) => held.Clear(); // key-ups are not seen on the lock screen
        }

        double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        /// <summary>True while any modifier is physically held or a key was touched in the last <paramref name="quietMs"/>.</summary>
        public bool Busy(double quietMs) { return held.Count > 0 || Now - lastPhys < quietMs; }

        void Install(string why)
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = Native.SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, proc, Native.GetModuleHandle(null), 0);
            distrustUntil = Now + 1000;
            Log.Write("Keyboard hook installed (" + why + ")" + (hook == IntPtr.Zero ? " FAILED" : ""));
        }

        static int PowerToysPid()
        {
            var ps = Process.GetProcessesByName("PowerToys.KeyboardManagerEngine");
            int pid = ps.Length > 0 ? ps[0].Id : -1;
            foreach (var p in ps) p.Dispose();
            return pid;
        }

        void WatchPowerToys()
        {
            int pid = PowerToysPid();
            if (pid != ptPid) { ptPid = pid; if (pid != -1) { rehook.Stop(); rehook.Start(); } }
        }

        static bool IsModifier(uint vk) { return vk == 0x5B || vk == 0x5C || (vk >= 0xA0 && vk <= 0xA5) || (vk >= 0x10 && vk <= 0x12); }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool injected = (k.flags & 0x10) != 0, up = (k.flags & 0x80) != 0;
                if (!injected)
                {
                    lastPhys = Now;
                    if (IsModifier(k.vkCode))
                    {
                        if (seen.Add(k.vkCode)) Log.Write(string.Format("Physical modifier seen: vk=0x{0:X2}", k.vkCode));
                        if (up) { held.Remove(k.vkCode); lastModUp = Now; } else held.Add(k.vkCode);
                    }
                }
                else if (!up && IsModifier(k.vkCode) && k.dwExtraInfo.ToInt64() != OurTag && held.Count == 0 && Now - lastPhys > 100)
                {
                    // Something pressed a modifier with no physical key behind it: either another app is sending
                    // shortcuts, or a hook installed after ours is swallowing keys. Stay out of the way.
                    if (Now > distrustUntil) Log.Write(string.Format("Injected modifier vk=0x{0:X2} without a physical key; watchdog paused, re-hooking", k.vkCode));
                    distrustUntil = Now + 10000;
                    rehook.Stop(); rehook.Start();
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void Check()
        {
            double now = Now;
            if (held.Count > 0 && now - lastPhys > 10000) { Log.Write("No keyboard activity for 10 s while a modifier looked held; resetting."); held.Clear(); }
            bool quiet = hook != IntPtr.Zero && now > distrustUntil && held.Count == 0 && now - lastModUp > 300 && now - lastPhys > 300;
            for (int i = 0; i < Modifiers.Length; i++)
            {
                bool down = (Native.GetAsyncKeyState(Modifiers[i]) & 0x8000) != 0;
                if (!down || !quiet) { downSince[i] = 0; continue; }
                if (downSince[i] == 0) { downSince[i] = now; continue; }
                if (now - downSince[i] < StuckMs) continue;
                // Windows thinks it is down, nothing is physically held: release it.
                if (Modifiers[i] == 0x5B || Modifiers[i] == 0x5C) { Send(0xE8, false); Send(0xE8, true); } // keep Start from opening
                Send(Modifiers[i], true);
                downSince[i] = 0;
                Log.Write("Released stuck " + ModNames[i] + " (foreground: " + ForegroundName() + ")");
            }
        }

        static void Send(ushort vk, bool up)
        {
            var i = new Native.INPUT { type = 1 };
            i.u.ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u, dwExtraInfo = new IntPtr(OurTag) };
            Native.SendInput(1, new[] { i }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static IntPtr lastFg; static string lastFgName = "";
        static string ForegroundName()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == lastFg) return lastFgName;
            uint pid; Native.GetWindowThreadProcessId(fg, out pid);
            string name = "";
            try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { }
            lastFg = fg; lastFgName = name;
            return name;
        }

        public void Dispose() { watchdog.Stop(); ptWatch.Stop(); if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); }
    }

    // ---------------- Mac mode controller ----------------
    class Controller
    {
        const int OffDelayMs = 3000; // ignore momentary Bluetooth blips

        readonly Osd osd;
        readonly KeyWatch keys;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer offDelay = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer retry = new System.Windows.Forms.Timer();
        bool checking, connectedForMode, ptActive, ptKnown;
        public bool KeyboardConnected { get; private set; }
        public bool Active { get; private set; }
        public event EventHandler Changed;

        public Controller(Osd osd, KeyWatch keys)
        {
            this.osd = osd; this.keys = keys;
            debounce.Interval = 1500; debounce.Tick += (o, e) => { debounce.Stop(); Check(); };
            osd.DevicesChanged += (o, e) => { debounce.Stop(); debounce.Start(); };
            poll.Interval = 15000; poll.Tick += (o, e) => Check(); poll.Start();
            offDelay.Interval = OffDelayMs; offDelay.Tick += (o, e) => { offDelay.Stop(); connectedForMode = false; Log.Write("Apple keyboard gone: Mac keys off"); Apply(false); };
            retry.Interval = 250; retry.Tick += (o, e) => { retry.Stop(); SyncPowerToys(); };
            KeyboardConnected = connectedForMode = AppleKeyboard.IsConnected();
            Log.Write("Started. Apple keyboard " + (KeyboardConnected ? "connected" : "not connected") + ", mode " + Settings.Mode);
            Apply(true);
        }

        void Check()
        {
            if (checking) return;
            checking = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool c = AppleKeyboard.IsConnected();
                osd.BeginInvoke((Action)(() => { checking = false; OnConnection(c); }));
            });
        }

        void OnConnection(bool c)
        {
            if (c == KeyboardConnected) return;
            KeyboardConnected = c;
            Log.Write("Apple keyboard " + (c ? "connected" : "disconnected"));
            if (c) { offDelay.Stop(); if (!connectedForMode) { connectedForMode = true; Apply(false); } }
            else offDelay.Start(); // only switch off if it stays away
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Apply(bool force)
        {
            var m = Settings.Mode;
            bool active = m == Mode.On || (m == Mode.Auto && connectedForMode);
            Active = active;
            if (force) ptKnown = false;
            osd.HotkeysEnabled = active;
            SyncPowerToys();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        // Swap the PowerToys config only while no key is held: changing remaps mid-press loses key-ups
        // and leaves modifiers stuck.
        void SyncPowerToys()
        {
            if (ptKnown && ptActive == Active) return;
            if (keys.Busy(400)) { retry.Start(); return; }
            PowerToysKm.Apply(Active);
            ptActive = Active; ptKnown = true;
            Log.Write("PowerToys remaps " + (Active ? "on" : "off"));
        }

        public void Shutdown() { }
    }

    // ---------------- Logo ----------------
    // Same design as assets/MacKeys.ico (tools/make-icon.ps1): rounded square with the banner gradient and a
    // white ⌘. Paused = the same shape in grey.
    static class Logo
    {
        public static Bitmap Draw(int s, bool active)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float m = (float)Math.Max(0, Math.Round(s * 0.04)), w = s - 2 * m, d = w * 0.48f;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(m, m, d, d, 180, 90); path.AddArc(m + w - d, m, d, d, 270, 90);
                    path.AddArc(m + w - d, m + w - d, d, d, 0, 90); path.AddArc(m, m + w - d, d, d, 90, 90);
                    path.CloseFigure();
                    using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Color.White, Color.White))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = active
                            ? new[] { Color.FromArgb(56, 189, 248), Color.FromArgb(99, 102, 241), Color.FromArgb(217, 70, 239) }
                            : new[] { Color.FromArgb(150, 150, 150), Color.FromArgb(120, 120, 120), Color.FromArgb(95, 95, 95) };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, path);
                    }
                }
                using (var ff = new FontFamily("Segoe UI Symbol"))
                using (var gp = new GraphicsPath())
                {
                    gp.AddString("⌘", ff, (int)FontStyle.Regular, s * 0.62f, new PointF(0, 0), StringFormat.GenericTypographic);
                    var b = gp.GetBounds();
                    float k = s * 0.56f / Math.Max(b.Width, b.Height);
                    using (var mx = new Matrix())
                    {
                        mx.Translate((s - b.Width * k) / 2, (s - b.Height * k) / 2);
                        mx.Scale(k, k); mx.Translate(-b.X, -b.Y);
                        gp.Transform(mx);
                    }
                    g.FillPath(Brushes.White, gp);
                }
            }
            return bmp;
        }
    }

    // ---------------- Tray icon ----------------
    class Tray : IDisposable
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MacKeys";

        readonly NotifyIcon icon = new NotifyIcon();
        readonly Controller ctl;
        readonly ToolStripMenuItem status, auto, on, off, autostart;
        Icon current;
        bool lastActive;

        public Tray(Controller ctl)
        {
            this.ctl = ctl;
            var menu = new ContextMenuStrip();
            status = new ToolStripMenuItem("") { Enabled = false };
            auto = new ToolStripMenuItem("Automatic (when an Apple keyboard is connected)", null, (o, e) => SetMode(Mode.Auto));
            on = new ToolStripMenuItem("Always on", null, (o, e) => SetMode(Mode.On));
            off = new ToolStripMenuItem("Off", null, (o, e) => SetMode(Mode.Off));
            autostart = new ToolStripMenuItem("Start with Windows", null, (o, e) => SetAutostart(!IsAutostart()));
            menu.Items.Add(new ToolStripMenuItem("MacKeys") { Enabled = false });
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(auto); menu.Items.Add(on); menu.Items.Add(off);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (o, e) => { icon.Visible = false; Application.Exit(); }));
            menu.Opening += (o, e) => RefreshMenu();

            icon.ContextMenuStrip = menu;
            lastActive = ctl.Active;
            UpdateIcon();
            icon.Visible = true;
            SystemEvents.UserPreferenceChanged += (o, e) => UpdateIcon(); // follow light/dark taskbar
            ctl.Changed += (o, e) =>
            {
                UpdateIcon();
                if (ctl.Active != lastActive && Settings.Mode == Mode.Auto)
                    icon.ShowBalloonTip(2000, "MacKeys", ctl.Active ? "Mac keys on — Apple keyboard connected" : "Mac keys off — Apple keyboard disconnected", ToolTipIcon.None);
                lastActive = ctl.Active;
            };
        }

        void RefreshMenu()
        {
            var m = Settings.Mode;
            status.Text = ctl.Active ? "● Active" + (ctl.KeyboardConnected ? " — Apple keyboard connected" : "")
                                     : "○ Paused" + (m == Mode.Auto ? " — no Apple keyboard" : "");
            auto.Checked = m == Mode.Auto; on.Checked = m == Mode.On; off.Checked = m == Mode.Off;
            autostart.Checked = IsAutostart();
        }

        void SetMode(Mode m) { Settings.Mode = m; ctl.Apply(false); }

        static bool IsAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunName, false);
            }
        }

        // Draws the sun glyph in the taskbar's text color; dimmed while paused.
        void UpdateIcon()
        {
            icon.Text = ctl.Active ? "MacKeys — active" : "MacKeys — paused";
            int size = SystemInformation.SmallIconSize.Width;
            using (var bmp = Logo.Draw(size, ctl.Active))
            {
                IntPtr h = bmp.GetHicon();
                var next = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                icon.Icon = next;
                if (current != null) current.Dispose();
                current = next;
            }
        }

        public void Dispose() { icon.Visible = false; icon.Dispose(); if (current != null) current.Dispose(); }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--lock") { LockPolicy.LockNow(); return; }

            bool created;
            using (new Mutex(true, "MacKeys.Resident", out created))
            {
                if (!created) return; // already running
                Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor DPI aware v2
                Native.timeBeginPeriod(1);                            // smoother animation timer
                Application.EnableVisualStyles();
                var osd = new Osd();
                LockPolicy.CleanUpOldCmdL();
                var h = osd.Handle; // create the window without showing it
                var keys = new KeyWatch(osd);
                var ctl = new Controller(osd, keys);
                using (new Tray(ctl)) Application.Run();
                ctl.Shutdown();
                keys.Dispose();
            }
        }
    }
}
