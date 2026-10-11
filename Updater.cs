// Update check against the GitHub releases of yydsyin/memball, download with SHA-256 check, and install.
// Installed copies (Program Files, with uninstall.exe) update through MemBall-Setup.exe /update;
// portable copies replace their own MemBall.exe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Release
{
    public Version Version;
    public string Tag, Notes, PageUrl;
    public Asset Setup, Portable;
}

class Asset
{
    public string Url, Sha256; // Sha256 is null when GitHub did not report a digest
    public long Size;
}

static class Updater
{
    public const string Repo = "yydsyin/memball";
    static readonly string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";

    public static Version Current
    {
        get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
    }

    public static string Short(Version v) { return v.Major + "." + v.Minor + "." + v.Build; }

    static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

    // "v1.2.3" / "1.2" -> 1.2.3.0; null when the tag is not a version.
    static Version ParseTag(string tag)
    {
        if (tag == null) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        Version v;
        if (t.IndexOf('.') < 0) t += ".0";
        return Version.TryParse(t, out v) ? Normalize(v) : null;
    }

    // An installed copy lives in Program Files next to the uninstaller the installer put there.
    public static bool IsInstalled
    {
        get { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "uninstall.exe")); }
    }

    static HttpWebRequest Request(string url)
    {
        // .NET Framework 4.x does not enable TLS 1.2 by default; GitHub requires it.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.UserAgent = "MemBall/" + Short(Current);
        r.Timeout = 20000;
        r.ReadWriteTimeout = 20000;
        return r;
    }

    // Latest published release; throws on network or format errors.
    public static Release FetchLatest()
    {
        HttpWebRequest req = Request(ApiUrl);
        req.Accept = "application/vnd.github+json";
        string json;
        using (WebResponse resp = req.GetResponse())
        using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            json = rd.ReadToEnd();

        Dictionary<string, object> o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        Release rel = new Release();
        rel.Tag = Str(o, "tag_name");
        rel.Version = ParseTag(rel.Tag);
        if (rel.Version == null) throw new FormatException("tag " + rel.Tag);
        rel.Notes = PlainText(Str(o, "body") ?? "");
        rel.PageUrl = Str(o, "html_url") ?? ReleasesPage;
        object[] assets = o.ContainsKey("assets") ? o["assets"] as object[] : null;
        if (assets != null)
            foreach (object a in assets)
            {
                Dictionary<string, object> d = a as Dictionary<string, object>;
                if (d == null) continue;
                string name = Str(d, "name") ?? "";
                Asset asset = new Asset();
                asset.Url = Str(d, "browser_download_url");
                asset.Size = d.ContainsKey("size") ? Convert.ToInt64(d["size"]) : -1;
                string digest = Str(d, "digest");
                if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    asset.Sha256 = digest.Substring(7).ToLowerInvariant();
                if (name.Equals("MemBall-Setup.exe", StringComparison.OrdinalIgnoreCase)) rel.Setup = asset;
                else if (name.Equals("MemBall.exe", StringComparison.OrdinalIgnoreCase)) rel.Portable = asset;
            }
        return rel;
    }

    // Release notes are Markdown; the update window shows plain text, so drop the common markup:
    // heading marks, bold/italic markers and inline code backticks.
    static string PlainText(string md)
    {
        string s = md.Replace("\r\n", "\n");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^\s{0,3}#{1,6}\s+", "");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return s.Replace("\n", "\r\n").Trim();
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
    }

    // Downloads into %TEMP%\MemBall-update and checks size and SHA-256; returns the file, or throws.
    public static string Download(Asset a, string fileName, Action<int> progress)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MemBall-update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        using (WebResponse resp = Request(a.Url).GetResponse())
        using (Stream src = resp.GetResponseStream())
        using (FileStream dst = File.Create(path))
        {
            long total = a.Size > 0 ? a.Size : resp.ContentLength, done = 0;
            byte[] buf = new byte[65536];
            int n, lastPct = -1;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
                done += n;
                int pct = total > 0 ? (int)(done * 100 / total) : 0;
                if (pct != lastPct) { lastPct = pct; progress(pct); }
            }
        }
        bool ok = a.Size <= 0 || new FileInfo(path).Length == a.Size;
        if (ok && a.Sha256 != null) ok = Sha256(path) == a.Sha256;
        if (!ok)
        {
            File.Delete(path);
            throw new InvalidDataException(Lang.T("upd.badFile"));
        }
        return path;
    }

    static string Sha256(string path)
    {
        using (SHA256 h = SHA256.Create())
        using (FileStream f = File.OpenRead(path))
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in h.ComputeHash(f)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // Installed: the installer stops this process, installs and starts MemBall again.
    public static void RunSetup(string setupPath)
    {
        Process.Start(setupPath, "/update");
    }

    // Portable: a running exe can be renamed but not overwritten, so move it aside and put the new one in its place.
    public static void ReplacePortable(string newExe)
    {
        string exe = Application.ExecutablePath, old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try { File.Move(newExe, exe); }
        catch { File.Move(old, exe); throw; }
        Process.Start(exe, "--after-update");
    }

    // Left behind by ReplacePortable; removed on the next start (retrying while the old process finishes exiting).
    public static void CleanupOldExe()
    {
        string old = Application.ExecutablePath + ".old";
        for (int i = 0; i < 10 && File.Exists(old); i++)
        {
            try { File.Delete(old); }
            catch { Thread.Sleep(300); }
        }
    }
}

// "MemBall x.y.z is available": release notes, then Update now / Later / Skip this version.
class UpdateForm : Form
{
    readonly MemBall ball;
    readonly Release rel;
    readonly Label status = new Label();
    readonly ProgressBar bar = new ProgressBar();
    readonly Button now = new Button(), later = new Button(), skip = new Button();
    readonly LinkLabel page = new LinkLabel();
    readonly float f;

    int P(int v) { return (int)Math.Round(v * f); }

    public UpdateForm(MemBall ball, Release rel)
    {
        this.ball = ball;
        this.rel = rel;
        using (Graphics g = CreateGraphics()) f = g.DpiX / 96f;
        Text = Lang.T("upd.window");
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        TableLayoutPanel t = new TableLayoutPanel();
        t.ColumnCount = 1;
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.Padding = new Padding(P(18), P(14), P(18), P(12));

        Label head = new Label();
        head.Text = Lang.T("upd.header", Updater.Short(rel.Version));
        head.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        head.AutoSize = true;
        t.Controls.Add(head);

        Label cur = new Label();
        cur.Text = Lang.T("upd.current", Updater.Short(Updater.Current));
        cur.ForeColor = SystemColors.GrayText;
        cur.AutoSize = true;
        cur.Margin = new Padding(0, P(2), 0, P(8));
        t.Controls.Add(cur);

        if (rel.Notes.Length > 0)
        {
            TextBox notes = new TextBox();
            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.Text = rel.Notes;
            notes.Size = new Size(P(400), P(150));
            notes.Select(0, 0);
            t.Controls.Add(notes);
        }

        bar.Size = new Size(P(400), P(14));
        bar.Visible = false;
        bar.Margin = new Padding(0, P(10), 0, 0);
        t.Controls.Add(bar);
        status.AutoSize = true;
        status.MaximumSize = new Size(P(400), 0);
        status.Visible = false;
        t.Controls.Add(status);
        page.Text = Lang.T("upd.openPage");
        page.AutoSize = true;
        page.Visible = false;
        page.LinkClicked += delegate { try { Process.Start(rel.PageUrl); } catch { } };
        t.Controls.Add(page);

        foreach (Button b in new[] { now, later, skip })
        {
            b.AutoSize = true;
            b.MinimumSize = new Size(P(88), 0);
            b.Padding = new Padding(P(6), P(2), P(6), P(2));
        }
        now.Text = Lang.T("upd.now");
        later.Text = Lang.T("upd.later");
        skip.Text = Lang.T("upd.skip");
        now.Click += delegate { StartUpdate(); };
        later.Click += delegate { Close(); };
        skip.Click += delegate { ball.SkipVersion(Updater.Short(rel.Version)); Close(); };
        AcceptButton = now;
        CancelButton = later;

        FlowLayoutPanel buttons = new FlowLayoutPanel();
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.Fill;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Margin = new Padding(0, P(12), 0, 0);
        buttons.Controls.Add(later);
        buttons.Controls.Add(now);
        buttons.Controls.Add(skip);
        t.Controls.Add(buttons);
        Controls.Add(t);
    }

    void StartUpdate()
    {
        bool installed = Updater.IsInstalled;
        Asset a = installed ? rel.Setup : rel.Portable;
        if (a == null || a.Url == null) { Fail(Lang.T("upd.noAsset")); return; }
        now.Enabled = skip.Enabled = false;
        bar.Visible = status.Visible = true;
        page.Visible = false;
        status.ForeColor = SystemColors.ControlText;
        status.Text = Lang.T("upd.downloading", 0);

        Thread th = new Thread(delegate ()
        {
            try
            {
                string file = Updater.Download(a, installed ? "MemBall-Setup.exe" : "MemBall.exe",
                    delegate (int pct) { BeginInvoke((MethodInvoker)delegate { bar.Value = Math.Min(100, pct); status.Text = Lang.T("upd.downloading", pct); }); });
                BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        status.Text = Lang.T("upd.installing");
                        if (installed) Updater.RunSetup(file); else Updater.ReplacePortable(file);
                        ball.ExitForUpdate();
                    }
                    catch (Exception ex) { MemBall.LogError(ex); Fail(ex.Message); }
                });
            }
            catch (Exception ex)
            {
                MemBall.LogError(ex);
                try { BeginInvoke((MethodInvoker)delegate { Fail(ex.Message); }); } catch { }
            }
        });
        th.IsBackground = true;
        th.Start();
    }

    void Fail(string why)
    {
        bar.Visible = false;
        status.Visible = page.Visible = true;
        status.ForeColor = Color.FromArgb(192, 0, 0);
        status.Text = Lang.T("upd.failed", why);
        now.Enabled = skip.Enabled = true;
    }
}
