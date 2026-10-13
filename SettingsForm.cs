// Settings: the values the user can change, how they are stored, and the window to change them.
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

class Settings
{
    public string Language = "auto";   // auto | zh | en | fr
    public bool HideInFullscreen = true;
    public bool Tuck = true;
    public double TuckDelaySec = 1.5;
    public int Opacity = 100;          // percent, 40..100
    public bool AutoClean;             // clean memory on a schedule
    public int AutoCleanMinutes = 30;  // 5..720
    public bool Welcomed;              // the first-run hint has been shown
    public bool AutoUpdate = true;
    public string SkipVersion = "";    // "Skip this version" in the update window
    public DateTime LastUpdateCheck = DateTime.MinValue; // UTC

    public Settings Clone() { return (Settings)MemberwiseClone(); }

    // settings.txt holds one key=value per line; unknown keys and bad values fall back to the defaults.
    public static Settings Load(string path)
    {
        Settings s = new Settings();
        try
        {
            if (!File.Exists(path)) return s;
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                double d;
                if (k == "language") s.Language = v;
                else if (k == "hideInFullscreen") s.HideInFullscreen = v != "0";
                else if (k == "tuck") s.Tuck = v != "0";
                else if (k == "tuckDelay" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.TuckDelaySec = d;
                else if (k == "opacity" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Opacity = (int)d;
                else if (k == "autoClean") s.AutoClean = v == "1";
                else if (k == "autoCleanMinutes" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.AutoCleanMinutes = (int)d;
                else if (k == "welcomed") s.Welcomed = v == "1";
                else if (k == "autoUpdate") s.AutoUpdate = v != "0";
                else if (k == "skipVersion") s.SkipVersion = v;
                else if (k == "lastUpdateCheck")
                {
                    DateTime t;
                    if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) s.LastUpdateCheck = t;
                }
            }
        }
        catch { } // unreadable file: run with the defaults
        s.TuckDelaySec = Math.Max(0.5, Math.Min(10, s.TuckDelaySec));
        s.Opacity = Math.Max(40, Math.Min(100, s.Opacity));
        s.AutoCleanMinutes = Math.Max(5, Math.Min(720, s.AutoCleanMinutes));
        return s;
    }

    public void Save(string path)
    {
        string text =
            "language=" + Language + "\r\n" +
            "hideInFullscreen=" + (HideInFullscreen ? "1" : "0") + "\r\n" +
            "tuck=" + (Tuck ? "1" : "0") + "\r\n" +
            "tuckDelay=" + TuckDelaySec.ToString(CultureInfo.InvariantCulture) + "\r\n" +
            "opacity=" + Opacity + "\r\n" +
            "autoClean=" + (AutoClean ? "1" : "0") + "\r\n" +
            "autoCleanMinutes=" + AutoCleanMinutes + "\r\n" +
            "welcomed=" + (Welcomed ? "1" : "0") + "\r\n" +
            "autoUpdate=" + (AutoUpdate ? "1" : "0") + "\r\n" +
            "skipVersion=" + SkipVersion + "\r\n" +
            "lastUpdateCheck=" + LastUpdateCheck.ToString("o", CultureInfo.InvariantCulture) + "\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}

class SettingsForm : Form
{
    static readonly string[] LangCodes = { "auto", "zh", "en", "fr" };

    readonly MemBall ball;
    Settings current;
    readonly ComboBox langBox = new ComboBox();
    readonly CheckBox autoBox = new CheckBox(), fsBox = new CheckBox(), tuckBox = new CheckBox(), updBox = new CheckBox(), cleanBox = new CheckBox();
    readonly Button checkNow = new Button();
    readonly NumericUpDown delayBox = new NumericUpDown(), cleanEvery = new NumericUpDown();
    readonly TrackBar opBar = new TrackBar();
    readonly Label opValue = new Label();
    readonly Button apply = new Button();
    readonly float f; // DPI scale for the few fixed widths; everything else sizes itself from the font

    int P(int v) { return (int)Math.Round(v * f); }

    public SettingsForm(MemBall ball, Settings cur, bool autostart, string hotkey)
    {
        this.ball = ball;
        current = cur;
        using (Graphics g = CreateGraphics()) f = g.DpiX / 96f;

        Text = Lang.T("set.title");
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true; // the ball is topmost; keep this window above it
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        TableLayoutPanel t = new TableLayoutPanel();
        t.ColumnCount = 2;
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.Padding = new Padding(P(16), P(14), P(16), P(12));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        langBox.DropDownStyle = ComboBoxStyle.DropDownList;
        langBox.Items.AddRange(new object[] { Lang.T("set.langAuto"), "简体中文", "English", "Français" });
        langBox.SelectedIndex = Math.Max(0, Array.IndexOf(LangCodes, cur.Language));
        langBox.Width = P(190);
        Row(t, Caption(Lang.T("set.lang")), langBox);

        Check(autoBox, Lang.T("set.autostart"), autostart);
        Row(t, autoBox, null);
        Check(fsBox, Lang.T("set.fullscreen"), cur.HideInFullscreen);
        Row(t, fsBox, null);
        Check(tuckBox, Lang.T("set.tuck"), cur.Tuck);
        Row(t, tuckBox, null);

        delayBox.DecimalPlaces = 1;
        delayBox.Increment = 0.5m;
        delayBox.Minimum = 0.5m;
        delayBox.Maximum = 10m;
        delayBox.Value = (decimal)cur.TuckDelaySec;
        delayBox.Width = P(64);
        // Sub-options: caption indented under its checkbox in the left column, the value in the right column,
        // so every input lines up with the language box and the opacity slider.
        Label delayCaption = SubCaption(Lang.T("set.tuckDelay"));
        Label secondsCaption = Caption(Lang.T("set.seconds"));
        Row(t, delayCaption, Flow(delayBox, secondsCaption));
        tuckBox.CheckedChanged += delegate { delayBox.Enabled = delayCaption.Enabled = secondsCaption.Enabled = tuckBox.Checked; };
        delayBox.Enabled = delayCaption.Enabled = secondsCaption.Enabled = cur.Tuck;

        Check(cleanBox, Lang.T("set.autoClean"), cur.AutoClean);
        Row(t, cleanBox, null);
        cleanEvery.Minimum = 5;
        cleanEvery.Maximum = 720;
        cleanEvery.Increment = 5;
        cleanEvery.Value = cur.AutoCleanMinutes;
        cleanEvery.Width = P(64);
        Label everyCaption = SubCaption(Lang.T("set.every"));
        Label minutesCaption = Caption(Lang.T("set.minutes"));
        Row(t, everyCaption, Flow(cleanEvery, minutesCaption));
        cleanBox.CheckedChanged += delegate { cleanEvery.Enabled = everyCaption.Enabled = minutesCaption.Enabled = cleanBox.Checked; };
        cleanEvery.Enabled = everyCaption.Enabled = minutesCaption.Enabled = cur.AutoClean;

        opBar.Minimum = 40;
        opBar.Maximum = 100;
        opBar.TickFrequency = 10;
        opBar.SmallChange = 5;
        opBar.LargeChange = 10;
        opBar.Value = cur.Opacity;
        opBar.Width = P(170);
        opValue.AutoSize = true;
        opValue.Anchor = AnchorStyles.Left;
        opValue.Text = cur.Opacity + "%";
        opBar.ValueChanged += delegate { opValue.Text = opBar.Value + "%"; };
        Row(t, Caption(Lang.T("set.opacity")), Flow(opBar, opValue));

        Row(t, Caption(Lang.T("set.hotkey")), Caption(hotkey ?? Lang.T("set.hotkeyNone")));

        Check(updBox, Lang.T("set.autoUpdate"), cur.AutoUpdate);
        Btn(checkNow, Lang.T("set.checkNow"));
        checkNow.Anchor = AnchorStyles.Left;
        checkNow.Click += delegate
        {
            checkNow.Enabled = false;
            checkNow.Text = Lang.T("upd.checking");
            ball.CheckForUpdates(true, this, delegate { if (!IsDisposed) { checkNow.Text = Lang.T("set.checkNow"); checkNow.Enabled = true; } });
        };
        Row(t, updBox, checkNow);

        Label version = Caption(Lang.T("set.version", Application.ProductVersion));
        version.ForeColor = SystemColors.GrayText;
        Row(t, version, null);

        Button export = new Button(), ok = new Button(), cancel = new Button();
        Btn(export, Lang.T("set.export"));
        Btn(ok, Lang.T("set.ok"));
        Btn(apply, Lang.T("set.apply"));
        Btn(cancel, Lang.T("set.cancel"));
        export.Click += delegate { ball.ExportLog(this); };
        ok.Click += delegate { Apply(); Close(); };
        apply.Click += delegate
        {
            // Apply keeps the window open. After a language change it reopens in place so its own text switches too.
            if (Apply()) { Point at = Location; Close(); ball.OpenSettings(at); }
            else apply.Enabled = false;
        };
        cancel.Click += delegate { Close(); }; // changes already applied stay, as in other Windows dialogs
        AcceptButton = ok;
        CancelButton = cancel;

        TableLayoutPanel buttons = new TableLayoutPanel();
        buttons.ColumnCount = 2;
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.Fill;
        buttons.Margin = new Padding(0, P(12), 0, 0);
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        export.Anchor = AnchorStyles.Left;
        buttons.Controls.Add(export, 0, 0);
        FlowLayoutPanel right = Flow(ok, apply, cancel);
        right.Anchor = AnchorStyles.Right;
        buttons.Controls.Add(right, 1, 0);
        Row(t, buttons, null);

        Controls.Add(t);

        // Apply lights up once something differs from what is in effect.
        apply.Enabled = false;
        EventHandler dirty = delegate { apply.Enabled = true; };
        langBox.SelectedIndexChanged += dirty;
        autoBox.CheckedChanged += dirty;
        fsBox.CheckedChanged += dirty;
        tuckBox.CheckedChanged += dirty;
        delayBox.ValueChanged += dirty;
        opBar.ValueChanged += dirty;
        updBox.CheckedChanged += dirty;
        cleanBox.CheckedChanged += dirty;
        cleanEvery.ValueChanged += dirty;
    }

    // Returns true when the language changed.
    bool Apply()
    {
        Settings s = current.Clone();
        s.Language = LangCodes[Math.Max(0, langBox.SelectedIndex)];
        s.HideInFullscreen = fsBox.Checked;
        s.Tuck = tuckBox.Checked;
        s.TuckDelaySec = (double)delayBox.Value;
        s.Opacity = opBar.Value;
        s.AutoUpdate = updBox.Checked;
        s.AutoClean = cleanBox.Checked;
        s.AutoCleanMinutes = (int)cleanEvery.Value;
        bool langChanged = s.Language != current.Language;
        if (!ball.ApplySettings(s, autoBox.Checked))
            MessageBox.Show(this, Lang.T("set.saveFailed"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        current = s;
        return langChanged;
    }

    // ---------- layout helpers ----------

    static void Row(TableLayoutPanel t, Control left, Control right)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(left, 0, row);
        if (right == null) t.SetColumnSpan(left, 2);
        else t.Controls.Add(right, 1, row);
    }

    Label Caption(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = true;
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, P(6), P(14), P(6));
        return l;
    }

    Label SubCaption(string text)
    {
        Label l = Caption(text);
        l.Margin = new Padding(P(20), P(6), P(14), P(6));
        return l;
    }

    void Check(CheckBox c, string text, bool on)
    {
        c.Text = text;
        c.Checked = on;
        c.AutoSize = true;
        c.Margin = new Padding(0, P(4), 0, P(4));
    }

    void Btn(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.MinimumSize = new Size(P(80), 0);
        b.Padding = new Padding(P(6), P(2), P(6), P(2));
    }

    static FlowLayoutPanel Flow(params Control[] items)
    {
        FlowLayoutPanel p = new FlowLayoutPanel();
        p.AutoSize = true;
        p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        p.WrapContents = false;
        p.Margin = new Padding(0);
        p.Anchor = AnchorStyles.Left;
        foreach (Control c in items) p.Controls.Add(c);
        return p;
    }
}
