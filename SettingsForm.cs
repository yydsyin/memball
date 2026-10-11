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
            }
        }
        catch { } // unreadable file: run with the defaults
        s.TuckDelaySec = Math.Max(0.5, Math.Min(10, s.TuckDelaySec));
        s.Opacity = Math.Max(40, Math.Min(100, s.Opacity));
        return s;
    }

    public void Save(string path)
    {
        string text =
            "language=" + Language + "\r\n" +
            "hideInFullscreen=" + (HideInFullscreen ? "1" : "0") + "\r\n" +
            "tuck=" + (Tuck ? "1" : "0") + "\r\n" +
            "tuckDelay=" + TuckDelaySec.ToString(CultureInfo.InvariantCulture) + "\r\n" +
            "opacity=" + Opacity + "\r\n";
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
    readonly Settings current;
    readonly ComboBox langBox = new ComboBox();
    readonly CheckBox autoBox = new CheckBox(), fsBox = new CheckBox(), tuckBox = new CheckBox();
    readonly NumericUpDown delayBox = new NumericUpDown();
    readonly TrackBar opBar = new TrackBar();
    readonly Label opValue = new Label();
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
        // One row across both columns, indented under its checkbox, so a long caption does not widen the left column.
        Label delayCaption = Caption(Lang.T("set.tuckDelay"));
        delayCaption.Margin = new Padding(P(20), P(6), P(6), P(6));
        Row(t, Flow(delayCaption, delayBox, Caption(Lang.T("set.seconds"))), null);
        tuckBox.CheckedChanged += delegate { delayBox.Enabled = delayCaption.Enabled = tuckBox.Checked; };
        delayBox.Enabled = delayCaption.Enabled = cur.Tuck;

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

        Label version = Caption(Lang.T("set.version", Application.ProductVersion));
        version.ForeColor = SystemColors.GrayText;
        Row(t, version, null);

        Button export = new Button(), ok = new Button(), cancel = new Button();
        Btn(export, Lang.T("set.export"));
        Btn(ok, Lang.T("set.ok"));
        Btn(cancel, Lang.T("set.cancel"));
        export.Click += delegate { ball.ExportLog(this); };
        ok.Click += delegate { Apply(); Close(); };
        cancel.Click += delegate { Close(); };
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
        FlowLayoutPanel right = Flow(ok, cancel);
        right.Anchor = AnchorStyles.Right;
        buttons.Controls.Add(right, 1, 0);
        Row(t, buttons, null);

        Controls.Add(t);
    }

    void Apply()
    {
        Settings s = current.Clone();
        s.Language = LangCodes[Math.Max(0, langBox.SelectedIndex)];
        s.HideInFullscreen = fsBox.Checked;
        s.Tuck = tuckBox.Checked;
        s.TuckDelaySec = (double)delayBox.Value;
        s.Opacity = opBar.Value;
        ball.ApplySettings(s, autoBox.Checked);
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
