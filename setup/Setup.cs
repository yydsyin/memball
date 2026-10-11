// MemBall-Setup.exe: installs MemBall for all users and, run as uninstall.exe /uninstall, removes it again.
// MemBall.exe and LICENSE are embedded as resources by build.bat; the UI text comes from Lang.cs.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MemBall Setup")]
[assembly: AssemblyProduct("MemBall")]
[assembly: AssemblyDescription("MemBall Setup")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

static class Setup
{
    public static readonly string Version = "1.0.0";
    public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MemBall");
    static readonly string AppExe = Path.Combine(InstallDir, "MemBall.exe");
    static readonly string UninstallExe = Path.Combine(InstallDir, "uninstall.exe");
    static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "MemBall.lnk");
    static readonly string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "MemBall.lnk");
    static readonly string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemBall");
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MemBall";

    public static bool AlreadyInstalled() { return File.Exists(AppExe); }

    // ---------- install ----------

    public static void Install(bool autostart, bool desktopShortcut)
    {
        StopMemBall();
        Directory.CreateDirectory(InstallDir);
        Extract("MemBall.exe", AppExe);
        Extract("LICENSE", Path.Combine(InstallDir, "LICENSE.txt"));
        // The uninstaller is this program itself, kept next to MemBall.exe.
        if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), UninstallExe, StringComparison.OrdinalIgnoreCase))
            Retry(delegate { File.Copy(Application.ExecutablePath, UninstallExe, true); });

        CreateShortcut(StartMenuLink, AppExe);
        if (desktopShortcut) CreateShortcut(DesktopLink, AppExe);
        else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

        using (RegistryKey k = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "MemBall");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "yydsyin");
            k.SetValue("DisplayIcon", AppExe);
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", "\"" + UninstallExe + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + UninstallExe + "\" /uninstall /quiet");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)((new FileInfo(AppExe).Length + new FileInfo(UninstallExe).Length) / 1024), RegistryValueKind.DWord);
        }

        // MemBall.exe owns the logon-task definition; let it create or remove the task.
        if (RunWait(AppExe, "--autostart " + (autostart ? "on" : "off")) != 0)
            throw new Exception("autostart (" + (autostart ? "on" : "off") + ")");
    }

    // Through the logon task when there is one, so it runs exactly as it will at every logon.
    public static void Launch()
    {
        if (RunWait("schtasks.exe", "/run /tn MemBall") != 0) Process.Start(AppExe);
    }

    // ---------- uninstall ----------

    public static void Uninstall(bool deleteUserData)
    {
        StopMemBall();
        if (File.Exists(AppExe)) RunWait(AppExe, "--autostart off");
        else RunWait("schtasks.exe", "/delete /tn MemBall /f");
        foreach (string link in new[] { StartMenuLink, DesktopLink })
            if (File.Exists(link)) File.Delete(link);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        if (deleteUserData && Directory.Exists(UserData)) Directory.Delete(UserData, true);

        // The folder holds this running uninstall.exe: remove it a moment after we have exited.
        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"");
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.WorkingDirectory = Path.GetTempPath();
        Process.Start(psi);
    }

    // ---------- helpers ----------

    static void StopMemBall()
    {
        foreach (Process p in Process.GetProcessesByName("MemBall"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
            p.Dispose();
        }
    }

    static void Extract(string resource, string path)
    {
        Retry(delegate
        {
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (FileStream dst = File.Create(path))
                src.CopyTo(dst);
        });
    }

    // A just-killed MemBall.exe can stay locked for a moment.
    static void Retry(MethodInvoker action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) { if (i >= 20) throw; System.Threading.Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { if (i >= 20) throw; System.Threading.Thread.Sleep(250); }
        }
    }

    static int RunWait(string exe, string args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe, args);
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        using (Process p = Process.Start(psi))
        {
            if (!p.WaitForExit(30000)) return -1;
            return p.ExitCode;
        }
    }

    static void CreateShortcut(string link, string target)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
        try
        {
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "MemBall" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        finally
        {
            Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static string LicenseText()
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE"))
        using (StreamReader r = new StreamReader(s))
            return r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    // ---------- entry point ----------

    [STAThread]
    static void Main(string[] args)
    {
        Lang.Code = Lang.Resolve("auto");
        Application.EnableVisualStyles();
        bool uninstall = Array.IndexOf(args, "/uninstall") >= 0;
        bool quiet = Array.IndexOf(args, "/quiet") >= 0;
        bool update = Array.IndexOf(args, "/update") >= 0;

        if (args.Length >= 2 && args[0] == "--render")
        {
            // Dev aid: --render <png> [zh|en|fr] [uninstall] draws a window to an image (needs a build without the admin manifest).
            if (args.Length >= 3) Lang.Code = Lang.Resolve(args[2]);
            using (Form f = args.Length >= 4 ? (Form)new UninstallForm() : new SetupForm())
                RenderForm(f, args[1]);
            return;
        }

        if (uninstall)
        {
            if (quiet) { try { Uninstall(false); } catch { Environment.Exit(1); } return; }
            Application.Run(new UninstallForm());
        }
        else if (update)
        {
            // Started by MemBall's updater: keep the user's choices, install, start MemBall again.
            try
            {
                Install(RunWait("schtasks.exe", "/query /tn MemBall") == 0, File.Exists(DesktopLink));
                Launch();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("setup.failed", ex.Message), "MemBall", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }
        else
        {
            if (quiet) { try { Install(true, false); } catch { Environment.Exit(1); } return; }
            Application.Run(new SetupForm());
        }
    }

    static void RenderForm(Form f, string path)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.Show();
        Application.DoEvents();
        using (Bitmap bmp = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}

// Shared look for the two small windows.
class SetupWindow : Form
{
    protected readonly float f;
    protected readonly TableLayoutPanel body = new TableLayoutPanel();

    protected int P(int v) { return (int)Math.Round(v * f); }

    protected SetupWindow(string title)
    {
        using (Graphics g = CreateGraphics()) f = g.DpiX / 96f;
        Text = title;
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        body.ColumnCount = 1;
        body.AutoSize = true;
        body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        body.Padding = new Padding(P(20), P(16), P(20), P(14));
        Controls.Add(body);
    }

    protected Label AddLabel(string text, bool bold, Color color)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = true;
        l.MaximumSize = new Size(P(380), 0);
        l.Margin = new Padding(0, P(3), 0, P(3));
        if (bold) l.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        if (color != Color.Empty) l.ForeColor = color;
        body.Controls.Add(l);
        return l;
    }

    protected CheckBox AddCheck(string text, bool on)
    {
        CheckBox c = new CheckBox();
        c.Text = text;
        c.Checked = on;
        c.AutoSize = true;
        c.Margin = new Padding(0, P(3), 0, P(3));
        body.Controls.Add(c);
        return c;
    }

    protected Button MakeButton(string text)
    {
        Button b = new Button();
        b.Text = text;
        b.AutoSize = true;
        b.MinimumSize = new Size(P(88), 0);
        b.Padding = new Padding(P(6), P(2), P(6), P(2));
        return b;
    }

    protected void AddButtons(params Button[] buttons)
    {
        FlowLayoutPanel p = new FlowLayoutPanel();
        p.FlowDirection = FlowDirection.RightToLeft;
        p.AutoSize = true;
        p.Dock = DockStyle.Fill;
        p.Margin = new Padding(0, P(14), 0, 0);
        for (int i = buttons.Length - 1; i >= 0; i--) p.Controls.Add(buttons[i]);
        body.Controls.Add(p);
    }
}

class SetupForm : SetupWindow
{
    readonly CheckBox autoBox, desktopBox, runBox;
    readonly Label status;
    readonly Button install, cancel;
    bool finished;

    public SetupForm() : base(Lang.T("setup.title", Setup.Version))
    {
        AddLabel(Lang.T(Setup.AlreadyInstalled() ? "setup.update" : "setup.header", Setup.Version), true, Color.Empty);
        AddLabel(Lang.T("setup.desc"), false, Color.Empty);
        AddLabel(Lang.T("setup.location") + ":  " + Setup.InstallDir, false, SystemColors.GrayText).Margin = new Padding(0, P(8), 0, P(8));

        autoBox = AddCheck(Lang.T("set.autostart"), true);
        desktopBox = AddCheck(Lang.T("setup.desktop"), false);
        runBox = AddCheck(Lang.T("setup.run"), true);

        // "By clicking Install you accept the [license]"
        FlowLayoutPanel agree = new FlowLayoutPanel();
        agree.AutoSize = true;
        agree.WrapContents = false;
        agree.Margin = new Padding(0, P(10), 0, 0);
        Label pre = new Label();
        pre.Text = Lang.T("setup.agree");
        pre.AutoSize = true;
        pre.Margin = new Padding(0);
        LinkLabel link = new LinkLabel();
        link.Text = Lang.T("setup.license");
        link.AutoSize = true;
        link.Margin = new Padding(P(3), 0, 0, 0);
        link.LinkClicked += delegate { ShowLicense(); };
        agree.Controls.Add(pre);
        agree.Controls.Add(link);
        body.Controls.Add(agree);

        status = AddLabel("", false, SystemColors.GrayText);
        status.Visible = false;

        install = MakeButton(Lang.T("setup.install"));
        cancel = MakeButton(Lang.T("set.cancel"));
        install.Click += delegate { OnInstall(); };
        cancel.Click += delegate { Close(); };
        AcceptButton = install;
        CancelButton = cancel;
        AddButtons(install, cancel);
    }

    void OnInstall()
    {
        if (finished)
        {
            if (runBox.Checked) { try { Setup.Launch(); } catch { } }
            Close();
            return;
        }
        install.Enabled = cancel.Enabled = autoBox.Enabled = desktopBox.Enabled = false;
        status.Text = Lang.T("setup.installing");
        status.Visible = true;
        Cursor = Cursors.WaitCursor;
        Application.DoEvents();
        try
        {
            Setup.Install(autoBox.Checked, desktopBox.Checked);
            finished = true;
            status.Text = Lang.T("setup.done");
            status.ForeColor = Color.FromArgb(0, 128, 72);
            install.Text = Lang.T("setup.finish");
            install.Enabled = true;
            cancel.Visible = false;
        }
        catch (Exception ex)
        {
            status.Visible = false;
            MessageBox.Show(this, Lang.T("setup.failed", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = cancel.Enabled = autoBox.Enabled = desktopBox.Enabled = true;
        }
        finally { Cursor = Cursors.Default; }
    }

    void ShowLicense()
    {
        using (Form f = new Form())
        {
            f.Text = Lang.T("setup.licenseTitle");
            f.Font = Font;
            f.Icon = Icon;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MinimizeBox = false;
            f.ClientSize = new Size(P(560), P(420));
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ReadOnly = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.Dock = DockStyle.Fill;
            t.Text = Setup.LicenseText();
            t.Select(0, 0);
            f.Controls.Add(t);
            f.ShowDialog(this);
        }
    }
}

class UninstallForm : SetupWindow
{
    readonly CheckBox dataBox;
    readonly Button go, cancel;
    bool finished;

    public UninstallForm() : base(Lang.T("uninst.title"))
    {
        AddLabel(Lang.T("uninst.confirm"), true, Color.Empty);
        dataBox = AddCheck(Lang.T("uninst.data"), false);
        dataBox.Margin = new Padding(0, P(10), 0, P(3));
        go = MakeButton(Lang.T("uninst.button"));
        cancel = MakeButton(Lang.T("set.cancel"));
        go.Click += delegate { OnUninstall(); };
        cancel.Click += delegate { Close(); };
        CancelButton = cancel;
        AddButtons(go, cancel);
    }

    void OnUninstall()
    {
        if (finished) { Close(); return; }
        go.Enabled = cancel.Enabled = dataBox.Enabled = false;
        Cursor = Cursors.WaitCursor;
        Application.DoEvents();
        try
        {
            Setup.Uninstall(dataBox.Checked);
            finished = true;
            Cursor = Cursors.Default;
            MessageBox.Show(this, Lang.T("uninst.done"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(this, Lang.T("uninst.failed", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            go.Enabled = cancel.Enabled = dataBox.Enabled = true;
        }
    }
}
