// MemBall: floating memory / network widget with one-click memory trim.
// Rendered as a per-pixel-alpha layered window for smooth edges and a soft shadow.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("MemBall")]
[assembly: System.Reflection.AssemblyProduct("MemBall")]
[assembly: System.Reflection.AssemblyDescription("Floating memory ball")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0")]

class MemBall : Form
{
    [StructLayout(LayoutKind.Sequential)]
    class MEMSTATUS
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMSTATUS));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte op, flags, alpha, fmt; }

    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT dst, ref SZ size, IntPtr hdcSrc, ref PT src, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);
    [DllImport("ntdll.dll")] static extern int RtlAdjustPrivilege(int priv, bool enable, bool thread, out bool was);
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

    const string TaskName = "MemBall";
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemBall");
    static readonly string PosFile = Path.Combine(DataDir, "pos.txt");
    static readonly string LogFile = Path.Combine(DataDir, "error.log");
    static readonly string SettingsFile = Path.Combine(DataDir, "settings.txt");
    internal Settings settings;
    SettingsForm settingsForm;
    Release pendingRelease;      // newer release found by the last check
    UpdateForm updateForm;
    Timer updateTimer;
    volatile bool checkingUpdates;
    bool balloonIsUpdate;        // the last tray balloon announced an update

    // Logical layout (96 dpi units)
    const float Margin_ = 5, PanelW = 41.7f, PanelH = 160.2f, Radius = 20.85f, RadiusTop = 7.5f;

    float s;
    int load;
    float shownLoad = -1;
    double upBps, downBps;
    Dictionary<string, long[]> lastNic; // adapter id -> { bytes sent, bytes received } at lastTick
    DateTime lastTick;
    // Overall size relative to the logical layout below.
    const float Zoom = 1f;

    // Clean animation state
    volatile bool cleanWorkDone;
    bool cleaning;
    DateTime cleanStart, resultStart = DateTime.MinValue;
    int cleanResult;  // 0 freed (freedText holds the amount), 1 already optimal, 2 failed
    string freedText;
    const double MinCleanMs = 750, ResultMs = 2600, PulseMs = 700;

    // Visibility: hidden by the user (menu / hotkey) or automatically while a full-screen app is in front.
    // Showing and hiding fade the whole window; it is only really hidden (SW_HIDE) once fully transparent.
    bool userHidden, onScreen = true;
    float fade = 1, fadeTarget = 1;
    IntPtr fsOverride; // full-screen window the user asked to show the ball over (via the hotkey)
    const float FadeMs = 160;

    // Edge docking: -1 left, 1 right, 0 free. A docked ball tucks into the edge, leaving a thin strip.
    int dock;
    float slide, slideTarget; // 0 = fully out, 1 = tucked in
    DateTime lastNear = DateTime.Now;
    const float SlideMs = 220;
    const float SnapDist = 36f; // dropping the panel this close to a side edge docks it

    const int HotkeyId = 1;
    bool hotkeyOk, trayTipShown, exitRequested;
    string hotkeyName; // the show/hide shortcut actually registered
    NotifyIcon tray;
    IntPtr trayHicon;
    int trayLoad = -1;
    IntPtr fgHook;
    WinEventProc fgProc; // kept in a field so the GC does not collect the callback

    Point downAt;
    bool dragging, hover, hoverBadge, pressed;
    DateTime hoverStart;
    Timer sampleTimer, animTimer, dockTimer;
    ToolStripMenuItem cleanItem, hideItem, settingsItem, exitItem, trayExitItem;

    MemBall()
    {
        using (Graphics g = CreateGraphics()) s = g.DpiX / 96f * Zoom;
        FormBorderStyle = FormBorderStyle.None;
        // ShowInTaskbar = false would make WinForms give the form a hidden owner window, and an owned window
        // cannot be topmost on its own (it ended up buried under maximized windows). WS_EX_TOOLWINDOW in
        // CreateParams already keeps it off the taskbar and Alt+Tab.
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(S((double)(PanelW + Margin_ * 2)), S((double)(PanelH + Margin_ * 2)));
        Location = LoadPos();

        settings = Settings.Load(SettingsFile);
        Lang.Code = Lang.Resolve(settings.Language);

        ContextMenuStrip menu = new ContextMenuStrip();
        cleanItem = new ToolStripMenuItem("", null, delegate { Clean(); });
        hideItem = new ToolStripMenuItem("", null, delegate { SetUserHidden(!userHidden); });
        settingsItem = new ToolStripMenuItem("", null, delegate { OpenSettings(); });
        exitItem = new ToolStripMenuItem("", null, delegate { Exit(); });
        menu.Items.AddRange(new ToolStripItem[] { cleanItem, hideItem, settingsItem, new ToolStripSeparator(), exitItem });
        menu.Opening += delegate { ApplyLanguage(); FillStats(menu); };
        ContextMenuStrip = menu;
        ApplyLanguage();

        sampleTimer = new Timer();
        sampleTimer.Interval = 1000;
        sampleTimer.Tick += delegate { Sample(); UpdateVisibility(); KeepOnTop(); Render(); UpdateTray(); };
        animTimer = new Timer();
        animTimer.Interval = 16;
        animTimer.Tick += delegate { Animate(); };
        dockTimer = new Timer();
        dockTimer.Interval = 120;
        dockTimer.Tick += delegate { CheckTuck(); };
        // First look one minute after start, then whenever a day has passed since the last check.
        updateTimer = new Timer();
        updateTimer.Interval = 60 * 1000;
        updateTimer.Tick += delegate
        {
            updateTimer.Interval = 60 * 60 * 1000;
            if (settings.AutoUpdate && (DateTime.UtcNow - settings.LastUpdateCheck).TotalHours >= 24) CheckForUpdates(false, null, null);
        };
        Sample();
    }

    // ---------- memory details in the context menu ----------

    readonly List<ToolStripItem> statItems = new List<ToolStripItem>();

    // Rebuilt every time the menu opens: memory in GB, the five programs using the most memory, Task Manager.
    void FillStats(ContextMenuStrip menu)
    {
        foreach (ToolStripItem it in statItems) { menu.Items.Remove(it); it.Dispose(); }
        statItems.Clear();

        if (pendingRelease != null)
        {
            ToolStripMenuItem upd = new ToolStripMenuItem(Lang.T("menu.update", Updater.Short(pendingRelease.Version)), null, delegate { ShowUpdate(); });
            upd.Font = new Font(upd.Font, FontStyle.Bold);
            statItems.Add(upd);
            statItems.Add(new ToolStripSeparator());
        }

        MEMSTATUS m = new MEMSTATUS();
        if (GlobalMemoryStatusEx(m))
        {
            ToolStripMenuItem total = new ToolStripMenuItem(Lang.T("menu.memory", m.dwMemoryLoad));
            total.ShortcutKeyDisplayString = ((m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0).ToString("0.0") + " / " + (m.ullTotalPhys / 1073741824.0).ToString("0.0") + " GB";
            total.Enabled = false;
            statItems.Add(total);
            statItems.Add(new ToolStripSeparator());
        }

        ToolStripMenuItem head = new ToolStripMenuItem(Lang.T("menu.top"));
        head.Enabled = false;
        statItems.Add(head);
        foreach (AppUsage app in TopProcesses(5))
        {
            // Clicking a program offers to end it; system processes are greyed out.
            AppUsage a = app;
            ToolStripMenuItem it = new ToolStripMenuItem("   " + a.Label, null, delegate { KillApp(a); });
            it.ShortcutKeyDisplayString = FormatBytes(a.Bytes);
            if (IsProtected(a.Name)) { it.Enabled = false; it.ToolTipText = Lang.T("proc.protected"); }
            else it.ToolTipText = a.Pids.Count > 1 ? Lang.T("proc.killAll", a.Pids.Count) : Lang.T("proc.killOne");
            statItems.Add(it);
        }
        statItems.Add(new ToolStripMenuItem(Lang.T("menu.taskmgr"), null, delegate { OpenTaskManager(); }));
        statItems.Add(new ToolStripSeparator());

        for (int i = 0; i < statItems.Count; i++) menu.Items.Insert(i, statItems[i]);
    }

    static string FormatBytes(long b)
    {
        double mb = b / 1048576.0;
        return mb >= 1024 ? (mb / 1024).ToString("0.0") + " GB" : mb.ToString("0") + " MB";
    }

    class AppUsage
    {
        public string Name, Label; // process name, and the friendly name shown in the menu
        public long Bytes;
        public List<int> Pids;
    }

    // Processes that keep Windows (or this app) running; ending them would crash or log off the session.
    static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso", "svchost",
        "dwm", "fontdrvhost", "sihost", "ctfmon", "audiodg", "spoolsv", "SecurityHealthService",
        "MsMpEng", "NisSrv", "MpDefenderCoreService", "MemBall" };

    static bool IsProtected(string name) { return ProtectedNames.Contains(name); }

    // Ends every process of the program after a confirmation (unsaved work in it is lost).
    void KillApp(AppUsage a)
    {
        string msg = Lang.T("kill.confirm", a.Label, a.Pids.Count > 1 ? Lang.T("kill.count", a.Pids.Count) : "", FormatBytes(a.Bytes));
        if (MessageBox.Show(this, msg, Lang.T("kill.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        int failed = 0;
        foreach (int pid in a.Pids)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    // The pid may have been reused since the menu was built: only end the same program.
                    if (!string.Equals(p.ProcessName, a.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                }
            }
            catch (ArgumentException) { }          // already exited before we looked
            catch (InvalidOperationException) { }  // exited while we were ending it
            catch (Exception ex) { failed++; Log("ending " + a.Name + " (pid " + pid + ") failed: " + ex.Message); }
        }
        if (failed > 0)
            MessageBox.Show(this, Lang.T("kill.failed", failed), Lang.T("kill.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        Sample();
        Render();
    }

    // Working sets summed per executable name (a browser's many processes count as one program),
    // labelled with the file description ("Google Chrome") when it can be read.
    static List<AppUsage> TopProcesses(int n)
    {
        Dictionary<string, long> sum = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<int>> pidsOf = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName;
                if (p.Id == 0 || name == "Idle" || name == "MemCompression" || name == "Memory Compression") continue; // Task Manager hides these too
                long ws = p.WorkingSet64;
                long cur;
                sum[name] = (sum.TryGetValue(name, out cur) ? cur : 0) + ws;
                List<int> pids;
                if (!pidsOf.TryGetValue(name, out pids)) pidsOf[name] = pids = new List<int>();
                pids.Add(p.Id);
            }
            catch { }
            finally { p.Dispose(); }
        }
        List<KeyValuePair<string, long>> sorted = new List<KeyValuePair<string, long>>(sum);
        sorted.Sort(delegate (KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
        if (sorted.Count > n) sorted.RemoveRange(n, sorted.Count - n);

        List<AppUsage> list = new List<AppUsage>();
        foreach (KeyValuePair<string, long> kv in sorted)
        {
            AppUsage a = new AppUsage();
            a.Name = kv.Key; a.Label = kv.Key; a.Bytes = kv.Value; a.Pids = pidsOf[kv.Key];
            try
            {
                // Some instances (e.g. SYSTEM services) may refuse access; any one of them gives the path.
                string exe = null;
                for (int k = 0; k < a.Pids.Count && k < 5 && exe == null; k++) exe = ImagePath(a.Pids[k]);
                string desc = exe == null ? null : FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (!string.IsNullOrEmpty(desc) && desc.Trim().Length > 0) a.Label = desc.Trim();
            }
            catch { }
            if (a.Label.Length > 24) a.Label = a.Label.Substring(0, 23) + "…";
            list.Add(a);
        }
        return list;
    }

    // Executable path via PROCESS_QUERY_LIMITED_INFORMATION, which (unlike Process.MainModule) also works
    // for services and protected processes such as the antivirus engine.
    static string ImagePath(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            StringBuilder sb = new StringBuilder(1024);
            int len = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref len) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    static void OpenTaskManager()
    {
        try { Process.Start("taskmgr.exe"); } catch (Exception ex) { Log(ex); }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // WS_EX_TOPMOST must be set at creation: started in the background by the logon task, the process has
            // no foreground rights and Windows silently ignores SetWindowPos(HWND_TOPMOST), which is what the
            // TopMost property uses, so the ball would sit under maximized windows.
            cp.ExStyle |= 0x80000 | 0x80 | 0x8; // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            cp.ExStyle &= ~0x40000;       // no WS_EX_APPWINDOW (ShowInTaskbar adds it): keeps it off the taskbar
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MoveTo(Left, Top);
        Render();
        KeepOnTop(); // check right away rather than after the first 1 s tick
        UpdateTray();
        if (!settings.Welcomed)
        {
            // First run: say how to use it, once.
            balloonIsUpdate = false;
            tray.ShowBalloonTip(6000, Lang.T("welcome.title"), Lang.T("welcome.text"), ToolTipIcon.None);
            settings.Welcomed = true;
            try { settings.Save(SettingsFile); } catch (Exception ex) { LogOnce("savesettings", ex.ToString()); }
        }
        sampleTimer.Start();
        dockTimer.Start();
        updateTimer.Start();

        // Ctrl+Alt+M, or Ctrl+Alt+Shift+M when another program already owns it (MOD_NOREPEAT on both).
        if (RegisterHotKey(Handle, HotkeyId, 0x0002 | 0x0001 | 0x4000, 0x4D)) hotkeyName = "Ctrl+Alt+M";
        else if (RegisterHotKey(Handle, HotkeyId, 0x0002 | 0x0001 | 0x0004 | 0x4000, 0x4D)) hotkeyName = "Ctrl+Alt+Shift+M";
        hotkeyOk = hotkeyName != null;
        if (hotkeyOk) hideItem.ShortcutKeyDisplayString = hotkeyName;
        else Log("Ctrl+Alt+M and Ctrl+Alt+Shift+M are both taken by other programs; hiding is still possible from the menu and undone from the tray icon");

        // React to foreground changes immediately instead of waiting for the next 1 s sample.
        // Called from native code, so exceptions must not escape it.
        fgProc = delegate { try { UpdateVisibility(); } catch (Exception ex) { LogOnce("fg", ex.ToString()); } };
        fgHook = SetWinEventHook(3, 3, IntPtr.Zero, fgProc, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT
        if (fgHook == IntPtr.Zero) Log("SetWinEventHook failed; full-screen detection falls back to the 1 s poll");
        UpdateVisibility();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    void OnDisplayChanged(object o, EventArgs e) { Post(delegate { MoveTo(Left, Top); Render(); }); }
    void OnPowerChanged(object o, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Post(delegate { MoveTo(Left, Top); UpdateVisibility(); Render(); });
    }
    void OnSessionSwitch(object o, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
            Post(delegate { MoveTo(Left, Top); UpdateVisibility(); Render(); });
    }

    // SystemEvents fire on their own thread, possibly while the window is closing (logoff, exit);
    // an exception there would count as a crash and restart the app, so a closed window just ignores them.
    void Post(MethodInvoker action)
    {
        if (exitRequested || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { } // includes ObjectDisposedException: window gone meanwhile
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) { ToggleShown(); return; } // WM_HOTKEY
        if (m.Msg == 0x0021) // WM_MOUSEACTIVATE: left clicks must not steal focus from the app in front
        {
            int msg = (int)(((long)m.LParam >> 16) & 0xFFFF);
            if (msg != 0x0204) { m.Result = (IntPtr)3; return; } // anything but WM_RBUTTONDOWN -> MA_NOACTIVATE
        }
        base.WndProc(ref m);
    }

    float S(float v) { return v * s; }
    int S(double v) { return (int)Math.Round(v * s); }

    static GraphicsPath Rounded(RectangleF r, float top, float bottom)
    {
        GraphicsPath p = new GraphicsPath();
        float dt = Math.Min(top * 2, r.Width), db = Math.Min(bottom * 2, r.Width);
        p.AddArc(r.X, r.Y, dt, dt, 180, 90);
        p.AddArc(r.Right - dt, r.Y, dt, dt, 270, 90);
        p.AddArc(r.Right - db, r.Bottom - db, db, db, 0, 90);
        p.AddArc(r.X, r.Bottom - db, db, db, 90, 90);
        p.CloseFigure();
        return p;
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- data ----------

    static ulong AvailPhys()
    {
        MEMSTATUS m = new MEMSTATUS();
        return GlobalMemoryStatusEx(m) ? m.ullAvailPhys : 0;
    }

    void Sample()
    {
        MEMSTATUS m = new MEMSTATUS();
        if (GlobalMemoryStatusEx(m)) load = (int)m.dwMemoryLoad;
        if (shownLoad < 0) shownLoad = load;
        if (Math.Abs(shownLoad - TargetLoad()) > 0.05f) animTimer.Start();

        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (Exception ex) { LogOnce("nics", ex.ToString()); upBps = downBps = 0; lastNic = null; return; }

        // Counters are cumulative per adapter, so only adapters seen in both samples contribute;
        // an adapter appearing or vanishing (VPN, Wi-Fi reconnect) must not add its lifetime total.
        Dictionary<string, long[]> cur = new Dictionary<string, long[]>();
        foreach (NetworkInterface ni in nics)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceStatistics st = ni.GetIPStatistics();
                cur[ni.Id] = new long[] { st.BytesSent, st.BytesReceived };
            }
            catch { } // one adapter that cannot be read is skipped; the others still count
        }
        DateTime now = DateTime.UtcNow;
        if (lastNic != null)
        {
            long ds = 0, dr = 0;
            foreach (KeyValuePair<string, long[]> kv in cur)
            {
                long[] prev;
                if (!lastNic.TryGetValue(kv.Key, out prev)) continue;
                ds += Math.Max(0, kv.Value[0] - prev[0]); // a counter reset reads as 0, not negative
                dr += Math.Max(0, kv.Value[1] - prev[1]);
            }
            double sec = Math.Max(0.2, (now - lastTick).TotalSeconds);
            upBps = ds / sec;
            downBps = dr / sec;
        }
        lastNic = cur; lastTick = now;
    }

    // While cleaning the ring drains to zero, then refills to the new value.
    float TargetLoad() { return cleaning ? 0 : load; }

    double ResultAge() { return (DateTime.Now - resultStart).TotalMilliseconds; }

    void Animate()
    {
        if (cleaning && cleanWorkDone && (DateTime.Now - cleanStart).TotalMilliseconds >= MinCleanMs)
        {
            cleaning = false;
            resultStart = DateTime.Now;
            Sample();
        }

        float target = TargetLoad();
        float diff = target - shownLoad;
        bool ringIdle = Math.Abs(diff) < 0.05f;
        if (ringIdle) shownLoad = target;
        else shownLoad += diff * (cleaning ? 0.10f : 0.08f);

        fade = Step(fade, fadeTarget, animTimer.Interval / FadeMs);
        slide = Step(slide, slideTarget, animTimer.Interval / SlideMs);
        if (fade == 0 && fadeTarget == 0 && onScreen)
        {
            onScreen = false;
            hover = hoverBadge = pressed = false;
            ShowWindow(Handle, 0); // SW_HIDE
            Note("faded out, window hidden");
        }

        bool effectsIdle = !cleaning && ResultAge() > ResultMs;
        bool moving = fade != fadeTarget || slide != slideTarget;
        if (!onScreen || (ringIdle && effectsIdle && !moving && !hover)) animTimer.Stop();
        Render();
    }

    static float Step(float v, float target, float step)
    {
        return v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);
    }

    static void SplitSpeed(double b, out string num, out string unit)
    {
        double k = b / 1024;
        if (k < 1000) { num = k.ToString(k < 100 ? "0.0" : "0"); unit = "K/s"; return; }
        double mb = k / 1024;
        num = mb.ToString(mb < 100 ? "0.0" : "0"); unit = "M/s";
    }

    void Clean()
    {
        if (cleaning) return;
        cleaning = true;
        cleanWorkDone = false;
        cleanStart = DateTime.Now;
        resultStart = DateTime.MinValue;
        animTimer.Start();
        System.Threading.Thread worker = new System.Threading.Thread(delegate ()
        {
            try
            {
                long freed = DoClean();
                if (freed < 10) cleanResult = 1;
                else { freedText = freed >= 1024 ? (freed / 1024.0).ToString("0.0") + "G" : freed + "M"; cleanResult = 0; }
            }
            catch (Exception ex) { Log(ex); cleanResult = 2; }
            finally { cleanWorkDone = true; }
        });
        worker.IsBackground = true;
        worker.Start();
    }

    static long DoClean()
    {
        ulong before = AvailPhys();
        bool was;
        int p1 = RtlAdjustPrivilege(13, true, false, out was); // SeProfileSingleProcessPrivilege
        int p2 = RtlAdjustPrivilege(5, true, false, out was);  // SeIncreaseQuotaPrivilege
        int cmd = 2; // MemoryEmptyWorkingSets
        int status = NtSetSystemInformation(80, ref cmd, 4); // SystemMemoryListInformation
        if (status != 0)
        {
            // Expected when not elevated; when elevated it means the system-wide trim broke, so leave a trace.
            LogOnce("clean-fallback", string.Format("NtSetSystemInformation 0x{0:X8} (privileges 0x{1:X8}, 0x{2:X8}); trimming per process instead", status, p1, p2));
            // Not elevated: trim the processes we are allowed to touch.
            foreach (Process p in Process.GetProcesses())
            {
                try { EmptyWorkingSet(p.Handle); } catch { }
                p.Dispose();
            }
        }
        System.Threading.Thread.Sleep(300);
        ulong after = AvailPhys();
        if (before == 0 || after == 0) return 0; // memory status unreadable: do not report a bogus amount
        return after > before ? (long)((after - before) / (1024 * 1024)) : 0;
    }

    // ---------- rendering ----------

    void Render()
    {
        if (!IsHandleCreated || !onScreen) return;
        using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawFrame(g, dock, EdgeX(), Ease(slide));
            }
            // Released in finally: this runs up to 60 times a second, so a leak per failed frame adds up fast.
            IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) return;
                mem = CreateCompatibleDC(screen);
                if (mem == IntPtr.Zero) return;
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hb);
                SZ size; size.cx = bmp.Width; size.cy = bmp.Height;
                PT src; src.x = 0; src.y = 0;
                PT dst; dst.x = Left; dst.y = Top;
                BLEND bl; bl.op = 0; bl.flags = 0; bl.alpha = (byte)Math.Round(255 * Ease(fade) * settings.Opacity / 100f); bl.fmt = 1; // AC_SRC_ALPHA
                if (!UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bl, 2)) // ULW_ALPHA
                    LogOnce("ulw", "UpdateLayeredWindow failed, error " + Marshal.GetLastWin32Error());
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hb != IntPtr.Zero) DeleteObject(hb);
                if (mem != IntPtr.Zero) DeleteDC(mem);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    static float Ease(float t) { return t * t * (3 - 2 * t); }

    // Screen edge the ball is docked to, in window coordinates.
    float EdgeX()
    {
        Rectangle wa = WorkArea(Left, Top);
        return dock < 0 ? wa.Left - Left : wa.Right - Left;
    }

    // Draws the panel slid toward the docked edge by e (0 = out, 1 = tucked) and clipped at the edge,
    // plus the thin strip that stays visible once it is tucked in.
    void DrawFrame(Graphics g, int side, float edgeX, float e)
    {
        if (side == 0) { Draw(g); return; }
        RectangleF p = Panel();

        // Near-invisible (alpha 1) but hit-testable band along the edge, so a cursor pushed against the
        // screen edge counts as being on the ball: it pulls a tucked ball out and keeps an open one open.
        float zone = S(5f);
        using (SolidBrush hz = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
            g.FillRectangle(hz, side > 0 ? edgeX - zone : edgeX, p.Y, zone, p.Height);

        if (e < 1)
        {
            float travel = side > 0 ? edgeX - p.X : p.Right - edgeX;
            GraphicsState st = g.Save();
            g.SetClip(side > 0 ? new RectangleF(0, 0, edgeX, Height) : new RectangleF(edgeX, 0, Width - edgeX, Height));
            g.TranslateTransform(side * travel * e, 0);
            Draw(g);
            g.Restore(st);
        }

        float sa = Math.Max(0f, Math.Min(1f, (e - 0.55f) / 0.45f));
        if (sa > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sw = S(3.5f), sh = S(44f);
            RectangleF sr = new RectangleF(side > 0 ? edgeX - S(1f) - sw : edgeX + S(1f), p.Y + S(59.2f) - sh / 2, sw, sh);
            // A dark track filled from the bottom up to the memory load, like the ring.
            float v = Math.Max(0, Math.Min(100, shownLoad));
            Color c1, c2;
            RingColors(v, out c1, out c2);
            using (GraphicsPath sp = Rounded(sr, sw / 2))
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(235 * sa), 56, 59, 69)))
                    g.FillPath(tb, sp);
                float fh = sh * v / 100f;
                if (fh > 0.5f)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(sp, CombineMode.Intersect);
                    RectangleF fr = new RectangleF(sr.X, sr.Bottom - fh, sw, fh);
                    using (LinearGradientBrush fb = new LinearGradientBrush(sr, Color.FromArgb((int)(255 * sa), c1), Color.FromArgb((int)(255 * sa), c2), 90f))
                        g.FillRectangle(fb, fr);
                    g.Restore(st);
                }
                using (Pen so = new Pen(Color.FromArgb((int)(120 * sa), 30, 32, 40), S(0.8f)))
                    g.DrawPath(so, sp);
            }
        }
    }

    static void RingColors(float v, out Color c1, out Color c2)
    {
        if (v < 60) { c1 = Theme.Ring1; c2 = Theme.Ring2; }
        else if (v < 80) { c1 = Color.FromArgb(255, 204, 96); c2 = Color.FromArgb(246, 156, 56); }
        else { c1 = Color.FromArgb(255, 128, 110); c2 = Color.FromArgb(236, 78, 78); }
    }

    // Panel geometry (logical px, panel-relative).
    RectangleF Panel() { return new RectangleF(S(Margin_), S(Margin_), S(PanelW), S(PanelH)); }
    PointF BadgeCenter() { RectangleF p = Panel(); return new PointF(p.X + p.Width / 2, p.Y + S(21f)); }
    float BadgeR() { return S(10.2f); }

    void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        RectangleF p = Panel();
        float cx = p.X + p.Width / 2;

        // faint shadow
        for (int i = 0; i >= 1; i--)
        {
            RectangleF sr = p; sr.Inflate(S(i * 0.8f), S(i * 0.8f)); sr.Offset(0, S(1f));
            using (GraphicsPath sp = Rounded(sr, S(Radius) + S(i * 0.8f)))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(8 - i, 0, 0, 0)))
                g.FillPath(sb, sp);
        }

        // panel: translucent slate
        using (GraphicsPath pp = Rounded(p, S(RadiusTop), S(Radius)))
        {
            Color fill = pressed ? Color.FromArgb(240, 40, 43, 52) : hover ? Color.FromArgb(240, 64, 68, 80) : Color.FromArgb(252, 56, 59, 69);
            using (SolidBrush pb = new SolidBrush(fill))
                g.FillPath(pb, pp);
            using (Pen edge = new Pen(Color.FromArgb(22, 255, 255, 255), S(0.8f)))
                g.DrawPath(edge, pp);
        }

        // hover idle animation: fades in over 250 ms, paused while cleaning / showing a result
        double rAge = ResultAge();
        double hAge = (DateTime.Now - hoverStart).TotalMilliseconds;
        bool resultShowing = rAge >= 0 && rAge < ResultMs;
        float idle = (hover && !cleaning && !resultShowing && !pressed) ? (float)Math.Min(1.0, hAge / 250) : 0f;
        float breath = (float)Math.Sin(hAge / 1600.0 * 2 * Math.PI);

        // check badge
        PointF bc = BadgeCenter();
        float br = BadgeR() * (pressed ? 0.9f : hoverBadge ? 1.07f : 1f) * (1 + 0.04f * breath * idle);
        RectangleF brc = new RectangleF(bc.X - br, bc.Y - br, br * 2, br * 2);

        if (idle > 0)
        {
            // breathing halo
            float hr = br + S(4f) + S(1.5f) * breath;
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddEllipse(bc.X - hr, bc.Y - hr, hr * 2, hr * 2);
                using (PathGradientBrush hb2 = new PathGradientBrush(hp))
                {
                    hb2.CenterColor = Color.FromArgb((int)((110 + 50 * breath) * idle), Theme.Accent);
                    hb2.SurroundColors = new Color[] { Color.FromArgb(0, Theme.Accent) };
                    g.FillPath(hb2, hp);
                }
            }
        }

        if (rAge >= 0 && rAge < PulseMs)
        {
            float t = (float)(rAge / PulseMs);
            float pr = br + S(13f) * t;
            using (Pen pp2 = new Pen(Color.FromArgb((int)(220 * (1 - t)), Theme.Accent), S(2.2f) * (1 - t) + S(0.4f)))
                g.DrawEllipse(pp2, bc.X - pr, bc.Y - pr, pr * 2, pr * 2);
        }
        using (GraphicsPath bp = new GraphicsPath())
        {
            bp.AddEllipse(brc);
            using (PathGradientBrush bb = new PathGradientBrush(bp))
            {
                bb.CenterPoint = new PointF(bc.X, bc.Y + br * 0.12f);
                bb.CenterColor = Theme.BadgeCenter;
                bb.SurroundColors = new Color[] { Theme.BadgeEdge };
                bb.FocusScales = new PointF(0.55f, 0.5f);
                g.FillPath(bb, bp);
            }
        }
        // cool sheen on the left side of the glass badge
        using (LinearGradientBrush sheen = new LinearGradientBrush(brc, Color.FromArgb(130, Theme.Sheen), Color.FromArgb(0, Theme.Sheen), 0f))
            g.FillEllipse(sheen, brc);
        using (GraphicsPath hl = new GraphicsPath())
        {
            hl.AddEllipse(brc.X + br * 0.25f, brc.Y + br * 0.12f, br * 1.1f, br * 0.75f);
            using (PathGradientBrush hb = new PathGradientBrush(hl))
            {
                hb.CenterColor = Color.FromArgb(150, Theme.Highlight);
                hb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                g.FillPath(hb, hl);
            }
        }
        if (idle > 0)
        {
            float rot = (float)(hAge / 2000.0 * 360 % 360);
            RectangleF gl = brc; gl.Inflate(S(1.8f), S(1.8f));
            for (int i = 0; i < 8; i++)
                using (Pen gp = new Pen(Color.FromArgb((int)(220 * idle * (8 - i) / 8), Theme.BadgeGlint), S(1.4f)))
                    g.DrawArc(gp, gl, rot - i * 7, 7.5f);
        }
        

        if (cleaning)
        {
            float sr2 = br * 0.52f;
            float start = (float)((DateTime.Now - cleanStart).TotalMilliseconds * 0.55 % 360);
            using (Pen sp2 = new Pen(Color.White, S(1.8f)))
            {
                sp2.StartCap = LineCap.Round; sp2.EndCap = LineCap.Round;
                g.DrawArc(sp2, bc.X - sr2, bc.Y - sr2, sr2 * 2, sr2 * 2, start, 260);
            }
        }
        else
        {
            float k = 1f;
            if (rAge >= 0 && rAge < 320)
            {
                float t = (float)(rAge / 320) - 1;
                k = 1 + 2.2f * t * t * t + 1.2f * t * t; // ease-out-back 0 -> 1
            }
            DrawGlyph(g, bc, br * k);
        }

        // memory ring: dark disc, thin track, arc growing symmetrically from the bottom
        float ry = p.Y + S(59.2f), r = S(18.9f), th = S(1.7f);
        RectangleF disc = new RectangleF(cx - r, ry - r, r * 2, r * 2);
        using (LinearGradientBrush db = new LinearGradientBrush(disc, Color.FromArgb(48, 54, 66), Color.FromArgb(34, 39, 50), 90f))
            g.FillEllipse(db, disc);
        using (Pen track = new Pen(Color.FromArgb(78, 81, 90), th))
            g.DrawEllipse(track, disc);
        float v = Math.Max(0, Math.Min(100, shownLoad));
        Color c1, c2;
        RingColors(v, out c1, out c2);
        if (v > 0.5f)
        {
            float sweep = 360f * v / 100f;
            RectangleF gb = disc; gb.Inflate(th, th);
            using (LinearGradientBrush ab = new LinearGradientBrush(gb, c1, c2, 0f))
            using (Pen arc = new Pen(ab, th * 1.35f))
            {
                arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round;
                g.DrawArc(arc, disc, 90 - sweep / 2, sweep);
            }
        }
        if (idle > 0)
        {
            float head = (float)(-90 + hAge / 2200.0 * 360 % 360);
            const int Trail = 14;
            for (int i = 0; i < Trail; i++)
            {
                int a = (int)(220 * idle * (Trail - i) / Trail);
                using (Pen cp = new Pen(Color.FromArgb(a, Theme.RingGlint), th * (1.5f - 0.6f * i / Trail)))
                {
                    cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round;
                    g.DrawArc(cp, disc, head - i * 4f, 4.5f);
                }
            }
        }

        StringFormat cen = new StringFormat(StringFormat.GenericTypographic);
        cen.Alignment = StringAlignment.Center;
        cen.LineAlignment = StringAlignment.Center;
        cen.FormatFlags |= StringFormatFlags.NoWrap;
        using (SolidBrush white = new SolidBrush(Color.FromArgb(234, 234, 236)))
        {
            float fitW = disc.Width - S(6f); // widest text that stays inside the ring
            if (!cleaning && rAge >= 0 && rAge < ResultMs && (cleanResult != 0 || freedText != null))
            {
                int a = (int)(255 * Math.Min(1.0, rAge / 200));
                using (SolidBrush lb2 = new SolidBrush(Color.FromArgb(a * 200 / 255, 200, 205, 215)))
                using (SolidBrush gb2 = new SolidBrush(Color.FromArgb(a, Theme.Accent)))
                {
                    string label = Lang.T(cleanResult == 1 ? "ball.optimal1" : cleanResult == 2 ? "ball.failed1" : "ball.freed");
                    string value = cleanResult == 1 ? Lang.T("ball.optimal2") : cleanResult == 2 ? Lang.T("ball.failed2") : freedText;
                    DrawFit(g, label, S(7.5f), FontStyle.Regular, lb2, new RectangleF(disc.X - S(4f), ry - S(10.5f), disc.Width + S(8f), S(10f)), cen, fitW);
                    // 17 px tall (centred where the old 14 px box was): a bold CJK line is taller than 14 px, and with
                    // LineLimit GDI+ dropped the whole line, so "最佳" / "失败" never showed.
                    DrawFit(g, value, S(11.5f), FontStyle.Bold, gb2, new RectangleF(disc.X - S(6f), ry, disc.Width + S(12f), S(17f)), cen, fitW);
                }
            }
            else
            {
                using (Font big = new Font("Microsoft YaHei UI", S(17f), FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString(((int)Math.Round(shownLoad)).ToString(), big, white, new RectangleF(disc.X - S(4f), ry - S(11f) - (cleaning ? S(3f) : S(4.5f) * idle), disc.Width + S(8f), S(22f)), cen);
                if (idle > 0)
                {
                    using (SolidBrush hb3 = new SolidBrush(Color.FromArgb((int)(210 * idle), Theme.Hint)))
                        DrawFit(g, Lang.T("ball.hint"), S(6f), FontStyle.Regular, hb3, new RectangleF(disc.X - S(4f), ry + S(2f), disc.Width + S(8f), S(11f)), cen, fitW);
                }
                if (cleaning)
                {
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(220, Theme.Accent)))
                        DrawFit(g, Lang.T("ball.cleaning"), S(7f), FontStyle.Regular, cb, new RectangleF(disc.X - S(4f), ry + S(6f), disc.Width + S(8f), S(9f)), cen, fitW);
                }
            }
        }

        cen.Dispose();

        DrawSpeed(g, p, p.Y + S(94.5f), true, upBps);
        DrawSpeed(g, p, p.Y + S(125.5f), false, downBps);
    }

    // Draws text at the given pixel size, shrinking it in small steps until it is no wider than maxW
    // (long French / English words inside the ring); text that already fits is drawn unchanged.
    static void DrawFit(Graphics g, string text, float size, FontStyle style, Brush b, RectangleF r, StringFormat f, float maxW)
    {
        for (int i = 0; ; i++)
        {
            using (Font ft = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel))
            {
                if (i >= 6 || g.MeasureString(text, ft, PointF.Empty, f).Width <= maxW) { g.DrawString(text, ft, b, r, f); return; }
            }
            size *= 0.92f;
        }
    }

    // The white mark on the badge, scaled by cr (the badge radius, animated after a clean).
    void DrawGlyph(Graphics g, PointF bc, float cr)
    {
        using (Pen pen = new Pen(Color.White, S(1.7f)))
        using (SolidBrush fill = new SolidBrush(Color.White))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            switch (Theme.Glyph)
            {
                case 1: // ring, open at the top, like the memory ring and the logo
                {
                    float rr = cr * 0.44f;
                    g.DrawArc(pen, bc.X - rr, bc.Y - rr, rr * 2, rr * 2, -50, 280);
                    break;
                }
                case 2: // lightning bolt
                    g.FillPolygon(fill, new PointF[] {
                        new PointF(bc.X + cr * 0.10f, bc.Y - cr * 0.52f),
                        new PointF(bc.X - cr * 0.30f, bc.Y + cr * 0.06f),
                        new PointF(bc.X - cr * 0.02f, bc.Y + cr * 0.06f),
                        new PointF(bc.X - cr * 0.12f, bc.Y + cr * 0.52f),
                        new PointF(bc.X + cr * 0.30f, bc.Y - cr * 0.08f),
                        new PointF(bc.X + cr * 0.02f, bc.Y - cr * 0.08f) });
                    break;
                case 3: // four-point sparkle
                {
                    float a = cr * 0.50f, w = cr * 0.13f;
                    g.FillPolygon(fill, new PointF[] {
                        new PointF(bc.X, bc.Y - a), new PointF(bc.X + w, bc.Y - w), new PointF(bc.X + a, bc.Y), new PointF(bc.X + w, bc.Y + w),
                        new PointF(bc.X, bc.Y + a), new PointF(bc.X - w, bc.Y + w), new PointF(bc.X - a, bc.Y), new PointF(bc.X - w, bc.Y - w) });
                    break;
                }
                default: // check mark
                    g.DrawLines(pen, new PointF[] {
                        new PointF(bc.X - cr * 0.40f, bc.Y - cr * 0.06f),
                        new PointF(bc.X - cr * 0.10f, bc.Y + cr * 0.22f),
                        new PointF(bc.X + cr * 0.42f, bc.Y - cr * 0.36f) });
                    break;
            }
        }
    }

    // One speed row: small arrow at left, value, unit underneath.
    void DrawSpeed(Graphics g, RectangleF p, float y, bool up, double bps)
    {
        string num, unit;
        SplitSpeed(bps, out num, out unit);
        Color ac = up ? Theme.Up : Theme.Down;
        float ax = p.X + S(9.5f), ay = y + S(5f), hw = S(2f), hh = S(2f), stem = S(2.2f);

        using (SolidBrush ab = new SolidBrush(ac))
        using (Pen sp = new Pen(ac, S(1.1f)))
        {
            if (up)
            {
                g.FillPolygon(ab, new PointF[] { new PointF(ax, ay - hh - stem / 2), new PointF(ax - hw, ay - stem / 2), new PointF(ax + hw, ay - stem / 2) });
                g.DrawLine(sp, ax, ay - stem / 2, ax, ay + stem);
            }
            else
            {
                g.FillPolygon(ab, new PointF[] { new PointF(ax, ay + hh + stem / 2), new PointF(ax - hw, ay + stem / 2), new PointF(ax + hw, ay + stem / 2) });
                g.DrawLine(sp, ax, ay + stem / 2, ax, ay - stem);
            }
        }

        StringFormat lf = new StringFormat(StringFormat.GenericTypographic);
        lf.LineAlignment = StringAlignment.Center;
        lf.FormatFlags |= StringFormatFlags.NoWrap;
        float tx = p.X + S(16.5f);
        using (Font nf = new Font("Microsoft YaHei UI", S(10.5f), FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush wb = new SolidBrush(Color.FromArgb(234, 234, 236)))
            g.DrawString(num, nf, wb, new RectangleF(tx, y - S(6.5f), p.Right - tx, S(13f)), lf);
        using (Font uf = new Font("Microsoft YaHei UI", S(7.5f), FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush ub = new SolidBrush(Color.FromArgb(175, 176, 180)))
            g.DrawString(unit, uf, ub, new RectangleF(tx + S(1f), y + S(8f), p.Right - tx, S(9f)), lf);
        lf.Dispose();
    }

    // ---------- interaction ----------

    bool OnBadge(Point pt)
    {
        PointF bc = BadgeCenter();
        float dx = pt.X - bc.X, dy = pt.Y - bc.Y, rr = BadgeR() + S(3f);
        return dx * dx + dy * dy <= rr * rr;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true; hoverStart = DateTime.Now;
        if (slideTarget > 0) Untuck();
        animTimer.Start(); Render();
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hover = false; hoverBadge = false; Render(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { downAt = e.Location; dragging = false; pressed = true; Render(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hb = OnBadge(e.Location);
        if (hb != hoverBadge) { hoverBadge = hb; Cursor = hb ? Cursors.Hand : Cursors.Default; Render(); }
        if (e.Button == MouseButtons.Left && !dragging &&
            (Math.Abs(e.X - downAt.X) > S(4.0) || Math.Abs(e.Y - downAt.Y) > S(4.0)))
        {
            dragging = true;
            pressed = false;
            Untuck();
            slide = 0;
            Render();
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            // The system move loop swallows the button-up, so OnMouseUp never runs for a drag: finish it here.
            dragging = false;
            SnapToEdge();
            SavePos();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        // A click that lands while the ball is still mostly tucked in only pulls it out.
        bool click = e.Button == MouseButtons.Left && pressed && !dragging && slide < 0.3f;
        pressed = false;
        dragging = false;
        if (click) Clean();
        Render();
        base.OnMouseUp(e);
    }

    // ---------- window behaviour ----------

    Rectangle WorkArea(int x, int y)
    {
        // FromPoint picks the nearest screen, so a window left off-screen by a display change is pulled back.
        return Screen.FromPoint(new Point(x + Width / 2, y + Height / 2)).WorkingArea;
    }

    // After a drag: dock to a side edge when dropped close to one.
    void SnapToEdge()
    {
        Rectangle wa = WorkArea(Left, Top);
        int m = (int)S(Margin_), snap = (int)S(SnapDist);
        int panelL = Left + m, panelR = Left + Width - m;
        dock = panelL - wa.Left < snap ? -1 : wa.Right - panelR < snap ? 1 : 0;
        lastNear = DateTime.Now;
        MoveTo(Left, Top);
    }

    // Place the window at (x, y), kept inside the nearest work area and pinned to its docked edge.
    void MoveTo(int x, int y)
    {
        Rectangle wa = WorkArea(x, y);
        int m = (int)S(Margin_), gap = (int)S(4f);
        if (dock < 0) x = wa.Left + gap - m;
        else if (dock > 0) x = wa.Right - gap - Width + m;
        else x = Math.Max(wa.Left - m, Math.Min(x, wa.Right - Width + m));
        y = Math.Max(wa.Top + gap - m, Math.Min(y, wa.Bottom - gap - Height + m));
        if (x != Left || y != Top) { Location = new Point(x, y); Render(); }
    }

    // ---------- tucking into the docked edge ----------

    void Tuck() { slideTarget = 1; hover = hoverBadge = false; animTimer.Start(); }
    void Untuck() { slideTarget = 0; lastNear = DateTime.Now; animTimer.Start(); }

    void CheckTuck()
    {
        if (dock == 0 || !onScreen || !settings.Tuck) { if (slideTarget != 0) Untuck(); return; }
        Point c = Cursor.Position;
        int edge = Left + (int)EdgeX(), zone = (int)S(5f);
        RectangleF pf = Panel();
        Rectangle panel = new Rectangle(Left + (int)pf.X, Top + (int)pf.Y, (int)pf.Width, (int)pf.Height);
        if (slideTarget > 0)
        {
            // Tucked: pushing the cursor against the edge next to the strip pulls the ball out.
            Rectangle hot = dock > 0 ? new Rectangle(edge - zone, panel.Y, zone, panel.Height) : new Rectangle(edge, panel.Y, zone, panel.Height);
            if (hot.Contains(c)) Untuck();
            return;
        }
        Rectangle area = Rectangle.Union(panel, dock > 0 ? new Rectangle(panel.Right, panel.Y, edge - panel.Right, panel.Height)
                                                         : new Rectangle(edge, panel.Y, panel.X - edge, panel.Height));
        area.Inflate((int)S(3f), (int)S(3f));
        bool busy = pressed || dragging || cleaning || ResultAge() < ResultMs || ContextMenuStrip.Visible;
        if (busy || area.Contains(c)) lastNear = DateTime.Now;
        else if ((DateTime.Now - lastNear).TotalMilliseconds > settings.TuckDelaySec * 1000) Tuck();
    }

    // ---------- hiding ----------

    // Fade out while a full-screen app is in front of this monitor or the user hid the ball; fade back in otherwise.
    void UpdateVisibility()
    {
        if (!IsHandleCreated) return;
        IntPtr fg = GetForegroundWindow();
        if (fsOverride != IntPtr.Zero && fg != fsOverride) fsOverride = IntPtr.Zero;
        bool fs = settings.HideInFullscreen && fsOverride == IntPtr.Zero && FullscreenAppActive(fg);
        bool want = !userHidden && !fs;
        if (want != (fadeTarget > 0))
            Note(want ? "show" : "hide: " + (userHidden ? "user" : "full screen, foreground " + Describe(fg)));
        SetShown(want);
    }

    void SetShown(bool want)
    {
        if (want && !onScreen)
        {
            onScreen = true;
            fade = 0;
            Render();
            ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
            KeepOnTop();
        }
        float t = want ? 1 : 0;
        if (fadeTarget != t) { fadeTarget = t; animTimer.Start(); }
    }

    // Runs every second: re-asserts topmost and repairs the window if its real state drifted from ours
    // (once it was found visible but not topmost, buried under a maximized window for minutes).
    void KeepOnTop()
    {
        if (!IsHandleCreated) return;
        bool visible = IsWindowVisible(Handle);
        if (!onScreen)
        {
            if (visible) { Note("visible although hidden by us; hiding again"); ShowWindow(Handle, 0); }
            return;
        }
        if (!visible) { Note("hidden although it should show; showing again"); ShowWindow(Handle, 4); Render(); }
        bool wasTop = IsTopmost();
        if (!SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010)) // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
            Note("SetWindowPos(TOPMOST) failed, error " + Marshal.GetLastWin32Error() + "; foreground " + Describe(GetForegroundWindow()));
        if (wasTop) return;
        // Something took topmost away. Windows ignores the request above while the process has no foreground
        // rights, so it may only come back after the next user interaction; record which case this was.
        Note((IsTopmost() ? "had lost topmost, restored" : "had lost topmost, not restored (no foreground rights?)") +
             "; foreground " + Describe(GetForegroundWindow()));
    }

    bool IsTopmost() { return (GetWindowLong(Handle, -20) & 0x8) != 0; } // WS_EX_TOPMOST

    // ---------- visibility journal (included in the exported log) ----------

    readonly Queue<string> journal = new Queue<string>();

    void Note(string what)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + what +
                      string.Format("  [onScreen={0} fade={1:0.00}->{2:0} slide={3:0.00} dock={4} pos={5},{6}]", onScreen, fade, fadeTarget, slide, dock, Left, Top);
        journal.Enqueue(line);
        while (journal.Count > 50) journal.Dequeue();
        if (what.StartsWith("had lost topmost") || what.StartsWith("SetWindowPos") || what.Contains("although"))
            LogOnce("vis:" + what.Substring(0, Math.Min(20, what.Length)), line); // anomalies also go to error.log, once per kind
    }

    static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(none)";
        StringBuilder sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        string proc = "?";
        try { using (Process p = Process.GetProcessById((int)pid)) proc = p.ProcessName; } catch { }
        RECT r;
        GetWindowRect(h, out r);
        int st;
        SHQueryUserNotificationState(out st);
        return string.Format("{0} ({1}) rect={2},{3},{4},{5} zoomed={6} style=0x{7:X8} quns={8}",
                             proc, sb, r.L, r.T, r.R, r.B, IsZoomed(h), GetWindowLong(h, -16), st);
    }

    void SetUserHidden(bool hide)
    {
        userHidden = hide;
        if (!hide)
        {
            // Shown on request: come out of the edge and stay out for a moment.
            Untuck();
            IntPtr fg = GetForegroundWindow();
            if (FullscreenAppActive(fg)) fsOverride = fg;
        }
        else if (!trayTipShown && tray != null)
        {
            trayTipShown = true;
            balloonIsUpdate = false;
            tray.ShowBalloonTip(3000, Lang.T("tray.hidden"), hotkeyOk ? Lang.T("tray.howHotkey", hotkeyName) : Lang.T("tray.how"), ToolTipIcon.None);
        }
        UpdateVisibility();
    }

    // Hotkey and tray click: hide the ball if it is on screen, otherwise bring it back
    // (also over a full-screen app, until the foreground window changes).
    void ToggleShown()
    {
        bool visible = onScreen && fadeTarget > 0;
        SetUserHidden(visible);
    }

    // The tray icon is always there: it shows the live memory load and is the way back to a hidden ball.
    void UpdateTray()
    {
        if (tray == null)
        {
            tray = new NotifyIcon();
            ContextMenuStrip tm = new ContextMenuStrip(); // the tray only offers Exit; everything else is on the ball
            trayExitItem = new ToolStripMenuItem(Lang.T("menu.exit"), null, delegate { Exit(); });
            tray.BalloonTipClicked += delegate { if (balloonIsUpdate) ShowUpdate(); };
            tm.Items.Add(trayExitItem);
            tray.ContextMenuStrip = tm;
            // Double click, so a stray single click on the tray cannot hide the ball.
            tray.MouseDoubleClick += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleShown(); };
        }
        if (trayLoad != load || tray.Icon == null)
        {
            try
            {
                IntPtr old = trayHicon, fresh;
                tray.Icon = MakeTrayIcon(load, out fresh);
                trayHicon = fresh;
                trayLoad = load; // only once the icon exists, so a failure is retried on the next tick
                if (old != IntPtr.Zero) DestroyIcon(old);
            }
            catch (Exception ex) { LogOnce("trayicon", ex.ToString()); }
        }
        // A NotifyIcon without an icon shows nothing; fall back to a stock icon so the way back always exists.
        if (tray.Icon == null) tray.Icon = SystemIcons.Application;
        tray.Text = Lang.T("tray.tip", load);
        if (!tray.Visible) tray.Visible = true;
    }

    static Icon MakeTrayIcon(int pct, out IntPtr hicon)
    {
        using (Bitmap bmp = RingIcon(Math.Max(16, SystemInformation.SmallIconSize.Width), pct))
        {
            hicon = bmp.GetHicon();
            return Icon.FromHandle(hicon);
        }
    }

    // The MemBall logo: a dark disc with a ring filled symmetrically from the bottom to pct.
    // The tray icon draws it with the live load; the exe icon (MemBall.ico) with a fixed one.
    static Bitmap RingIcon(int n, int pct)
    {
        Bitmap bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float th = n * 0.14f;
            RectangleF r = new RectangleF(th / 2 + 0.5f, th / 2 + 0.5f, n - th - 1, n - th - 1);
            using (SolidBrush disc = new SolidBrush(Color.FromArgb(52, 56, 66))) g.FillEllipse(disc, r);
            using (Pen track = new Pen(Color.FromArgb(96, 100, 110), th)) g.DrawEllipse(track, r);
            Color c1, c2;
            RingColors(pct, out c1, out c2);
            float sweep = Math.Max(8f, 360f * pct / 100f);
            using (Pen arc = new Pen(c2, th)) g.DrawArc(arc, r, 90 - sweep / 2, sweep);
        }
        return bmp;
    }

    // Writes the logo as a multi-size .ico (PNG-compressed entries, supported since Windows Vista).
    static void SaveLogoIco(string path, int pct)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (Bitmap b = RingIcon(sizes[i], pct))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                pngs[i] = ms.ToArray();
            }
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); // ICONDIR
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) // ICONDIRENTRY; 0 means 256
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
    }

    static readonly uint SelfPid = (uint)Process.GetCurrentProcess().Id;

    bool FullscreenAppActive(IntPtr fg)
    {
        int st;
        if (SHQueryUserNotificationState(out st) == 0 && (st == 3 || st == 4)) return true; // D3D full screen, presentation mode
        if (fg == IntPtr.Zero || fg == Handle || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        uint pid;
        GetWindowThreadProcessId(fg, out pid);
        if (pid == SelfPid) return false; // our own menus
        StringBuilder sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        string cls = sb.ToString();
        if (cls == "WorkerW" || cls == "Progman" || cls == "Shell_TrayWnd") return false;
        // A maximized window with a title bar is not full screen, even when it covers an auto-hide taskbar.
        if (IsZoomed(fg) && (GetWindowLong(fg, -16) & 0x00C00000) == 0x00C00000) return false; // GWL_STYLE, WS_CAPTION
        RECT r;
        if (!GetWindowRect(fg, out r)) return false;
        Screen sc = Screen.FromHandle(fg);
        if (!sc.Equals(Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)))) return false;
        Rectangle b = sc.Bounds;
        return r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
    }

    // ---------- autostart / position ----------

    static bool AutostartExists()
    {
        string err;
        return RunSchtasks("/query /tn " + TaskName, out err) == 0;
    }

    void SetAutostart(bool on)
    {
        if (on == AutostartExists()) return;
        int rc;
        string err;
        if (on)
            rc = CreateAutostartTask(out err);
        else
            rc = RunSchtasks("/delete /tn " + TaskName + " /f", out err);
        if (rc != 0)
        {
            Log("autostart change failed (" + rc + "): " + err);
            MessageBox.Show(Lang.T("auto.failed", string.IsNullOrEmpty(err) ? Lang.T("auto.runAsAdmin") : err), "MemBall");
        }
    }

    // ---------- settings and language ----------

    void ApplyLanguage()
    {
        cleanItem.Text = Lang.T("menu.clean");
        hideItem.Text = Lang.T(userHidden ? "menu.show" : "menu.hide");
        settingsItem.Text = Lang.T("menu.settings");
        exitItem.Text = Lang.T("menu.exit");
        if (trayExitItem != null) trayExitItem.Text = Lang.T("menu.exit");
    }

    // ---------- updates ----------

    // Asks GitHub for the latest release on a worker thread. manual: from the settings window, so report
    // every outcome (and ignore "skip this version"); automatic checks stay silent unless there is news.
    internal void CheckForUpdates(bool manual, IWin32Window owner, MethodInvoker done)
    {
        if (checkingUpdates) { if (done != null) done(); return; }
        checkingUpdates = true;
        System.Threading.Thread th = new System.Threading.Thread(delegate ()
        {
            Release rel = null;
            Exception err = null;
            try { rel = Updater.FetchLatest(); }
            catch (Exception ex) { err = ex; }
            Post(delegate
            {
                checkingUpdates = false;
                try
                {
                    if (err != null)
                    {
                        LogOnce("updatecheck", "update check failed: " + err.Message);
                        if (manual) MessageBox.Show(owner, Lang.T("upd.checkFailed", err.Message), "MemBall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    settings.LastUpdateCheck = DateTime.UtcNow;
                    try { settings.Save(SettingsFile); } catch (Exception ex) { LogOnce("savesettings", ex.ToString()); }
                    if (rel.Version > Updater.Current)
                    {
                        pendingRelease = rel;
                        string v = Updater.Short(rel.Version);
                        if (manual) ShowUpdate();
                        else if (v != settings.SkipVersion && tray != null)
                        {
                            balloonIsUpdate = true;
                            tray.ShowBalloonTip(10000, Lang.T("upd.balloonTitle"), Lang.T("upd.balloonText", v), ToolTipIcon.None);
                        }
                    }
                    else
                    {
                        pendingRelease = null;
                        if (manual) MessageBox.Show(owner, Lang.T("upd.latest", Updater.Short(Updater.Current)), "MemBall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                finally { if (done != null) done(); }
            });
        });
        th.IsBackground = true;
        th.Start();
    }

    void ShowUpdate()
    {
        if (pendingRelease == null) return;
        if (updateForm != null && !updateForm.IsDisposed) { updateForm.Activate(); return; }
        updateForm = new UpdateForm(this, pendingRelease);
        updateForm.FormClosed += delegate { updateForm = null; };
        updateForm.Show();
    }

    internal void SkipVersion(string version)
    {
        settings.SkipVersion = version;
        try { settings.Save(SettingsFile); } catch (Exception ex) { LogOnce("savesettings", ex.ToString()); }
    }

    // The update has been handed to the installer / the replaced exe: leave without restarting.
    internal void ExitForUpdate()
    {
        Exit();
    }

    internal static void LogError(Exception ex) { Log(ex); }

    // Debug aids: show a given language without touching the saved settings.
    void ForceLanguage(string code)
    {
        if (code == null) return;
        Lang.Code = Lang.Resolve(code);
        ApplyLanguage();
    }

    // at: where to put the window (reopening after a language change); null centres it on screen.
    internal void OpenSettings(Point? at = null)
    {
        if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(this, settings, AutostartExists(), hotkeyName);
        settingsForm.FormClosed += delegate { settingsForm = null; };
        if (at.HasValue) { settingsForm.StartPosition = FormStartPosition.Manual; settingsForm.Location = at.Value; }
        settingsForm.Show();
    }

    // Called by the settings window when the user presses OK.
    internal void ApplySettings(Settings s, bool autostart)
    {
        settings = s;
        try { settings.Save(SettingsFile); }
        catch (Exception ex) { LogOnce("savesettings", ex.ToString()); }
        SetAutostart(autostart);
        Lang.Code = Lang.Resolve(settings.Language);
        ApplyLanguage();
        if (!settings.Tuck) Untuck();
        UpdateVisibility();
        UpdateTray();
        Render();
    }

    // Plain "schtasks /create" would not start the task on battery, stop it when unplugged and kill it
    // after 72 hours, so the task is defined from XML: run at logon, elevated, on battery, without time limit.
    static int CreateAutostartTask(out string err)
    {
        string user = System.Security.SecurityElement.Escape(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        string exe = System.Security.SecurityElement.Escape(Application.ExecutablePath);
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\r\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>7</Priority>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\"><Exec><Command>&quot;" + exe + "&quot;</Command></Exec></Actions>\r\n" +
            "</Task>\r\n";
        string tmp = Path.Combine(Path.GetTempPath(), "MemBall_task.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            return RunSchtasks("/create /tn " + TaskName + " /xml \"" + tmp + "\" /f", out err);
        }
        catch (Exception ex) { Log(ex); err = ex.Message; return 1; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // Returns schtasks' exit code (-1 if it could not be run) and its error output in err.
    static int RunSchtasks(string args, out string err)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                err = p.StandardError.ReadToEnd().Trim();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex) { err = ex.Message; Log(ex); return -1; }
    }

    // pos.txt holds "x,y,dock"; files from older versions have no dock value, so it is inferred from x.
    Point LoadPos()
    {
        try
        {
            string[] a = File.ReadAllText(PosFile).Split(',');
            Point p = new Point(int.Parse(a[0]), int.Parse(a[1]));
            if (a.Length > 2) dock = Math.Sign(int.Parse(a[2]));
            else
            {
                Rectangle wa = Screen.FromPoint(new Point(p.X + Width / 2, p.Y + Height / 2)).WorkingArea;
                int m = (int)S(Margin_), snap = (int)S(SnapDist);
                dock = p.X + m - wa.Left < snap ? -1 : wa.Right - (p.X + Width - m) < snap ? 1 : 0;
            }
            return p;
        }
        catch { }
        dock = 1; // first run: docked to the right edge, so it tucks in like a docked ball
        Rectangle w = Screen.PrimaryScreen.WorkingArea;
        return new Point(w.Right - Width - S(8.0), w.Top + w.Height / 3);
    }

    void SavePos()
    {
        try
        {
            // Write a temp file and swap it in, so a power cut mid-write cannot leave a truncated pos.txt.
            Directory.CreateDirectory(DataDir);
            string tmp = PosFile + ".tmp";
            File.WriteAllText(tmp, Left + "," + Top + "," + dock);
            if (File.Exists(PosFile)) File.Replace(tmp, PosFile, null);
            else File.Move(tmp, PosFile);
        }
        catch (Exception ex) { LogOnce("savepos", ex.ToString()); }
    }

    void Exit()
    {
        exitRequested = true;
        Close();
    }

    // Only the 退出 menu item (or Windows shutting down / Task Manager) may close the ball; Alt+F4 does not.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SavePos();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (fgHook != IntPtr.Zero) UnhookWinEvent(fgHook);
        if (hotkeyOk) UnregisterHotKey(Handle, HotkeyId);
        RemoveTray();
        base.OnFormClosed(e);
    }

    void RemoveTray()
    {
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
    }

    // ---------- diagnostics export ----------

    // Saves a report (environment + error logs) the user can attach to a bug report, then shows it in Explorer.
    internal void ExportLog(IWin32Window owner)
    {
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Title = Lang.T("export.title");
            d.Filter = Lang.T("export.filter");
            d.FileName = "MemBall-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
            d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(d.FileName, BuildReport(), new UTF8Encoding(true));
                Process.Start("explorer.exe", "/select,\"" + d.FileName + "\"");
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show(owner, Lang.T("export.failed", ex.Message), "MemBall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    string BuildReport()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("MemBall diagnostics report");
        sb.AppendLine("Exported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Version: " + Application.ProductVersion);
        sb.AppendLine("Executable: " + Application.ExecutablePath);
        sb.AppendLine("Windows: " + WindowsVersion() + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"));
        sb.AppendLine(".NET CLR: " + Environment.Version);
        bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                         .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        sb.AppendLine("Administrator: " + (admin ? "yes" : "no"));
        MEMSTATUS m = new MEMSTATUS();
        if (GlobalMemoryStatusEx(m))
            sb.AppendLine("Memory: " + (m.ullTotalPhys / 1073741824.0).ToString("0.0") + " GB, " + m.dwMemoryLoad + "% in use");
        sb.AppendLine("Scaling: " + Math.Round(s / Zoom * 100) + "%");
        foreach (Screen sc in Screen.AllScreens)
            sb.AppendLine("Display: " + sc.Bounds.Width + "x" + sc.Bounds.Height + " @ " + sc.Bounds.X + "," + sc.Bounds.Y +
                          (sc.Primary ? " (primary)" : "") + ", work area " + sc.WorkingArea);
        sb.AppendLine("Start with Windows: " + (AutostartExists() ? "on" : "off"));
        sb.AppendLine("Hotkey: " + (hotkeyName ?? "not registered (taken by another program)"));
        sb.AppendLine("Language: " + Lang.Code + " (setting: " + settings.Language + ", system: " + System.Globalization.CultureInfo.CurrentUICulture.Name + ")");
        sb.AppendLine("Settings: hideInFullscreen=" + settings.HideInFullscreen + " tuck=" + settings.Tuck +
                      " tuckDelay=" + settings.TuckDelaySec + "s opacity=" + settings.Opacity + "%");
        sb.AppendLine("Updates: auto=" + settings.AutoUpdate + " lastCheck=" + (settings.LastUpdateCheck == DateTime.MinValue ? "never" : settings.LastUpdateCheck.ToString("u")) +
                      " skip=" + (settings.SkipVersion.Length > 0 ? settings.SkipVersion : "-") + " pending=" + (pendingRelease != null ? Updater.Short(pendingRelease.Version) : "-") +
                      " installed=" + Updater.IsInstalled);
        sb.AppendLine("Position: " + Left + "," + Top + "  docked: " + (dock < 0 ? "left" : dock > 0 ? "right" : "no") +
                      "  hidden by user: " + (userHidden ? "yes" : "no"));
        TimeSpan up = DateTime.Now - startedAt;
        sb.AppendLine("Uptime: " + (int)up.TotalHours + " h " + up.Minutes + " min" + (restartCount > 0 ? " (restart #" + restartCount + " after a crash)" : ""));
        sb.AppendLine();
        sb.AppendLine("===== Visibility journal (last " + journal.Count + ") =====");
        foreach (string j in journal) sb.AppendLine(j);
        sb.AppendLine();

        string[] logs = { Path.Combine(DataDir, "error.old.log"), LogFile };
        bool any = false;
        foreach (string f in logs)
        {
            string text;
            lock (logLock) { try { text = File.Exists(f) ? File.ReadAllText(f) : null; } catch (Exception ex) { text = "(could not read: " + ex.Message + ")"; } }
            if (string.IsNullOrEmpty(text)) continue;
            any = true;
            sb.AppendLine("===== " + Path.GetFileName(f) + " =====");
            sb.AppendLine(text);
        }
        if (!any) sb.AppendLine("(no errors logged)");
        return Scrub(sb.ToString());
    }

    // Keep the report free of the user's name: profile paths and the computer name are replaced.
    static string Scrub(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(profile), "%USERPROFILE%",
                                                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string pc = Environment.MachineName;
        if (!string.IsNullOrEmpty(pc))
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\b" + System.Text.RegularExpressions.Regex.Escape(pc) + "\\b", "<computer>",
                                                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    static string WindowsVersion()
    {
        // Environment.OSVersion reports 6.2 on Windows 10/11 for apps without a manifest; the registry has the real build.
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                string build = k.GetValue("CurrentBuild") as string;
                object ubr = k.GetValue("UBR");
                string disp = k.GetValue("DisplayVersion") as string;
                int b;
                string name = int.TryParse(build, out b) && b >= 22000 ? "Windows 11" : (k.GetValue("ProductName") as string ?? "Windows");
                return name + (disp != null ? " " + disp : "") + " (build " + build + (ubr != null ? "." + ubr : "") + ")";
            }
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    // ---------- crash recovery ----------

    static MemBall current;
    static int restartCount;
    static readonly DateTime startedAt = DateTime.Now;
    static DateTime uiErrorWindow = DateTime.MinValue;
    static int uiErrors;

    static readonly object logLock = new object();
    static readonly HashSet<string> loggedOnce = new HashSet<string>();

    static void Log(Exception ex) { Log(ex.ToString()); }

    // Appends to error.log (UI and clean threads may log at once). Past 256 KB the log moves to
    // error.old.log, so the first, most useful entries survive one rotation.
    static void Log(string text)
    {
        lock (logLock)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                FileInfo fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 256 * 1024)
                {
                    string oldLog = Path.Combine(DataDir, "error.old.log");
                    if (File.Exists(oldLog)) File.Delete(oldLog);
                    fi.MoveTo(oldLog);
                }
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n\r\n");
            }
            catch { } // nowhere left to report a failure to write the log
        }
    }

    // For errors that can repeat every frame or every second: record only the first occurrence per run.
    static void LogOnce(string key, string text)
    {
        lock (logLock) { if (!loggedOnce.Add(key)) return; }
        Log(text);
    }

    // Start a fresh copy and leave. Gives up after 3 restarts in a row that each crashed within a minute.
    static void Restart()
    {
        try { if (current != null) current.RemoveTray(); } catch { }
        if (current != null && current.exitRequested) Environment.Exit(1); // the user already chose to quit
        int n = (DateTime.Now - startedAt).TotalSeconds < 60 ? restartCount + 1 : 1;
        if (n <= 3)
        {
            Log("restarting after a crash (attempt " + n + " of 3)");
            try { Process.Start(Application.ExecutablePath, "--restart " + n); }
            catch (Exception ex) { Log("restart failed: " + ex); }
        }
        else Log("crashed again within a minute of 3 restarts; giving up. Start MemBall again to retry.");
        Environment.Exit(1);
    }

    static void OnUiException(object o, System.Threading.ThreadExceptionEventArgs e)
    {
        // Errors in a timer or paint handler are logged and skipped; only a burst of them restarts the app.
        Log(e.Exception);
        if ((DateTime.Now - uiErrorWindow).TotalSeconds > 60) { uiErrorWindow = DateTime.Now; uiErrors = 0; }
        if (++uiErrors >= 20) Restart();
    }

    static void OnFatalException(object o, UnhandledExceptionEventArgs e)
    {
        Exception ex = e.ExceptionObject as Exception;
        if (ex != null) Log(ex);
        Restart();
    }

    // Debug aid: MemBall.exe --png <file> renders one frame to an image and exits.
    void SavePreview(string path)
    {
        // Frames side by side: idle, hover, cleaning, results (freed, optimal, failed),
        // half tucked into a right edge, tucked (current, 65%, 90%).
        using (Bitmap bmp = new Bitmap(Width * 10, Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0, 120, 215));
                Draw(g);
                g.TranslateTransform(Width, 0);
                hover = true; hoverStart = DateTime.Now.AddMilliseconds(-1000);
                Draw(g);
                hover = false;
                g.TranslateTransform(Width, 0);
                cleaning = true; cleanStart = DateTime.Now.AddMilliseconds(-300); shownLoad = load * 0.4f;
                Draw(g);
                g.TranslateTransform(Width, 0);
                cleaning = false; resultStart = DateTime.Now.AddMilliseconds(-250); freedText = "1.2G"; shownLoad = load;
                cleanResult = 0;
                Draw(g);
                // The other two results: already optimal, clean failed.
                for (int r = 1; r <= 2; r++)
                {
                    g.TranslateTransform(Width, 0);
                    cleanResult = r;
                    Draw(g);
                }
                cleanResult = 0;
                resultStart = DateTime.MinValue;
                // Right screen edge where a docked window would put it; the dark band stands for the next monitor / off-screen.
                float edgeX = Width - ((int)S(Margin_) - (int)S(4f));
                // Tucked frames: current load, then 65% and 90% to show the fill level and colour steps.
                float[] loads = { load, load, 65, 90 };
                for (int i = 0; i < loads.Length; i++)
                {
                    g.TranslateTransform(Width, 0);
                    using (SolidBrush off = new SolidBrush(Color.FromArgb(24, 24, 28)))
                        g.FillRectangle(off, edgeX, 0, Width - edgeX, Height);
                    shownLoad = loads[i];
                    DrawFrame(g, 1, edgeX, Ease(i == 0 ? 0.5f : 1f));
                }
                shownLoad = load;
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--autostart")
        {
            // Used by the installer: create (pointing at this exe) or remove the logon task. Exit code 0 = done.
            string err = null;
            int rc = args[1] == "on" ? CreateAutostartTask(out err)
                   : AutostartExists() ? RunSchtasks("/delete /tn " + TaskName + " /f", out err) : 0;
            if (rc != 0) Log("--autostart " + args[1] + " failed (" + rc + "): " + err);
            Environment.Exit(rc == 0 ? 0 : 1);
        }
        // Debug aids below take an optional language (zh | en | fr) after the file name.
        string debugLang = args.Length == 3 ? args[2] : null;
        if ((args.Length == 2 || args.Length == 3) && args[0] == "--png")
        {
            SetProcessDPIAware();
            using (MemBall mb = new MemBall()) { mb.ForceLanguage(debugLang); mb.SavePreview(args[1]); }
            return;
        }
        if ((args.Length == 2 || args.Length == 3) && args[0] == "--settings")
        {
            // Debug aid: render the settings window to an image.
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            using (MemBall mb = new MemBall())
            {
                mb.ForceLanguage(debugLang);
                using (SettingsForm sf = new SettingsForm(mb, mb.settings, AutostartExists(), "Ctrl+Alt+Shift+M"))
                {
                    sf.StartPosition = FormStartPosition.Manual;
                    sf.Location = new Point(-4000, -4000);
                    sf.Show();
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(sf.Width, sf.Height))
                    {
                        sf.DrawToBitmap(bmp, new Rectangle(0, 0, sf.Width, sf.Height));
                        bmp.Save(args[1], ImageFormat.Png);
                    }
                }
            }
            return;
        }
        if ((args.Length == 2 || args.Length == 3) && args[0] == "--ico")
        {
            // Dev aid: MemBall.exe --ico <file> [pct] writes the logo icon used by build.bat.
            int pct = 40;
            if (args.Length == 3) int.TryParse(args[2], out pct);
            SaveLogoIco(args[1], pct);
            return;
        }
        if (args.Length == 2 && args[0] == "--report")
        {
            // Debug aid: write the diagnostics report without the save dialog
            // (the hotkey line reads "not registered" here, since this mode never registers it).
            SetProcessDPIAware();
            using (MemBall mb = new MemBall()) File.WriteAllText(args[1], mb.BuildReport(), new UTF8Encoding(true));
            return;
        }
        if ((args.Length == 2 || args.Length == 3) && args[0] == "--menu")
        {
            // Debug aid: render the context menu (with live memory details) to an image.
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            using (MemBall mb = new MemBall())
            {
                mb.ForceLanguage(debugLang);
                ContextMenuStrip menu = mb.ContextMenuStrip;
                mb.FillStats(menu);
                menu.Show(new Point(-4000, -4000));
                using (Bitmap bmp = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bmp, new Rectangle(0, 0, menu.Width, menu.Height));
                    bmp.Save(args[1], ImageFormat.Png);
                }
                menu.Close();
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--restart") int.TryParse(args[1], out restartCount);
        bool afterUpdate = args.Length == 1 && args[0] == "--after-update";

        bool created;
        using (System.Threading.Mutex mx = new System.Threading.Mutex(true, "MemBall_single", out created))
        {
            if (!created)
            {
                // A restart after a crash waits for the old instance to finish exiting.
                if (restartCount == 0 && !afterUpdate) return;
                try { if (!mx.WaitOne(10000)) { Log("restart aborted: previous instance still running after 10 s"); return; } }
                catch (System.Threading.AbandonedMutexException) { }
            }
            Updater.CleanupOldExe(); // the previous exe after a portable self-update; its process is gone by now
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += OnFatalException;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            current = new MemBall();
            Application.Run(current);
        }
    }
}
