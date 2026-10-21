// Colours and the badge glyph of the ball (ice blue with a four-point sparkle).
// One accent drives the badge, the low-load ring, highlights and texts;
// the 60-80% (orange) and 80%+ (red) ring colours stay fixed because they carry meaning.
using System.Drawing;

static class Theme
{
    public static Color Accent = Color.FromArgb(96, 176, 255);      // halo, pulse, result value, "cleaning" text
    public static Color BadgeCenter = Color.FromArgb(76, 150, 252);  // badge gradient, centre
    public static Color BadgeEdge = Color.FromArgb(198, 222, 255);   // badge gradient, rim
    public static Color Sheen = Color.FromArgb(178, 160, 255);       // cool sheen on the badge's left side
    public static Color Highlight = Color.FromArgb(236, 244, 255);   // glossy highlight on the badge
    public static Color BadgeGlint = Color.FromArgb(170, 208, 255);  // light running round the badge on hover
    public static Color RingGlint = Color.FromArgb(188, 216, 255);   // light running round the ring on hover
    public static Color Hint = Color.FromArgb(150, 196, 255);        // "click to clean" text
    public static Color Ring1 = Color.FromArgb(112, 196, 255);       // ring below 60%, gradient start
    public static Color Ring2 = Color.FromArgb(76, 128, 250);        // ring below 60%, gradient end (also tray and logo)
    public static Color Up = Color.FromArgb(80, 200, 255);           // upload arrow
    public static Color Down = Color.FromArgb(150, 130, 255);        // download arrow

    // 0 check mark, 1 ring, 2 lightning bolt, 3 four-point sparkle
    public static int Glyph = 3;
}
