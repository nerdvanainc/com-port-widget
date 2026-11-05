using System.Drawing;
using System.Runtime.InteropServices;
using ComPortWidget.Core.Text;
using WinForms = System.Windows.Forms;

namespace ComPortWidget.App;

/// <summary>트레이 메뉴 색(위젯의 어두운 색과 맞춤).</summary>
internal sealed class DarkMenuColors : WinForms.ProfessionalColorTable
{
    public static readonly Color Surface = Color.FromArgb(0x26, 0x2A, 0x31);
    public static readonly Color Text = Color.FromArgb(0xE6, 0xE8, 0xEB);
    private static readonly Color Line = Color.FromArgb(0x35, 0x3A, 0x43);
    private static readonly Color Highlight = Color.FromArgb(0x17, 0x44, 0x3A);
    private static readonly Color Accent = Color.FromArgb(0x2F, 0xBF, 0x8F);

    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Accent;
    public override Color MenuItemSelected => Highlight;
    public override Color MenuItemSelectedGradientBegin => Highlight;
    public override Color MenuItemSelectedGradientEnd => Highlight;
    public override Color MenuItemPressedGradientBegin => Highlight;
    public override Color MenuItemPressedGradientEnd => Highlight;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
}

/// <summary>트레이 아이콘과 메뉴.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly WinForms.ToolStripMenuItem _show, _reset, _settings, _exit;

    public event Action? ShowRequested;
    public event Action? ResetRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon(string language)
    {
        _image = CreateIcon();
        _show = new WinForms.ToolStripMenuItem();
        _reset = new WinForms.ToolStripMenuItem();
        _settings = new WinForms.ToolStripMenuItem();
        _exit = new WinForms.ToolStripMenuItem();
        _show.Click += (_, _) => ShowRequested?.Invoke();
        _reset.Click += (_, _) => ResetRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new WinForms.ContextMenuStrip
        {
            // 위젯과 같은 색
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = DarkMenuColors.Surface,
            ForeColor = DarkMenuColors.Text,
            Font = new Font("Segoe UI", 9.75f),
        };
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _show, new WinForms.ToolStripSeparator(), _reset, _settings, new WinForms.ToolStripSeparator(), _exit,
        });
        _icon = new WinForms.NotifyIcon { Icon = _image, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowRequested?.Invoke();
        };
        SetLanguage(language);
    }

    public void SetLanguage(string language)
    {
        _icon.Text = Strings.Get("app.name", language);
        _show.Text = Strings.Get("tray.show", language);
        _reset.Text = Strings.Get("tray.reset", language);
        _settings.Text = Strings.Get("tray.settings", language);
        _exit.Text = Strings.Get("tray.exit", language);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    // 별도 리소스 파일 없이 코드로 그린 아이콘: 어두운 바탕에 초록 점
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(0x1E, 0x21, 0x27));
            using var fg = new SolidBrush(Color.FromArgb(0x2F, 0xBF, 0x8F));
            g.FillEllipse(bg, 0, 0, 31, 31);
            g.FillEllipse(fg, 8, 8, 15, 15);
        }
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
