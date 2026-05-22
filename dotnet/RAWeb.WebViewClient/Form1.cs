using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace RAWeb.WebViewClient;

class WindowHelpers {
    public static TitlebarlessWindowHandler RemoveTitlebar(Form form) {
        return new TitlebarlessWindowHandler(form);
    }

    public class TitlebarlessWindowHandler : NativeWindow {
        private const int WM_NCCALCSIZE = 0x0083;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS {
            public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NCCALCSIZE_PARAMS {
            public RECT rgrc0, rgrc1, rgrc2;
            public IntPtr lppos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT {
            public int Left, Top, Right, Bottom;
        }

        private readonly Form _form;

        public TitlebarlessWindowHandler(Form form) {
            _form = form;
            AssignHandle(form.Handle);
        }

        public void TriggerFrameRecalculation() {
            SetWindowPos(_form.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);
        }

        public void ExtendFrameIntoClient() {
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            _ = DwmExtendFrameIntoClientArea(_form.Handle, ref margins);
        }

        const int WM_NCHITTEST = 0x0084;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);


        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT {
            public int length;
            public int flags;
            public int showCmd;
            public Point ptMinPosition;
            public Point ptMaxPosition;
            public Rectangle rcNormalPosition;
        }

        private const int SW_MAXIMIZE = 3;


        protected override void WndProc(ref Message m) {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero) {
                var nccsp = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);

                var wp = new WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(wp);
                GetWindowPlacement(this.Handle, ref wp);
                var isMaximized = wp.showCmd == SW_MAXIMIZE;

                // Extend client area to cover entire window
                nccsp.rgrc0.Top += isMaximized ? 8 : 0;
                nccsp.rgrc0.Left += 8;
                nccsp.rgrc0.Right -= 8;
                nccsp.rgrc0.Bottom -= 8;

                Marshal.StructureToPtr(nccsp, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            // make the window borders resizable by handling the WM_NCHITTEST message
            // and returning the appropriate hit test values for the window edges
            if (m.Msg == WM_NCHITTEST) {
                Console.WriteLine($"WM_NCHITTEST: {m.LParam.ToInt32()}");
                base.WndProc(ref m);

                if (m.Result == (IntPtr)1) { // HTCLIENT
                    Point pos = _form.PointToClient(new Point(m.LParam.ToInt32()));
                    var borderSize = 8; // Resize border width in pixels

                    var left = pos.X <= borderSize;
                    var right = pos.X >= _form.ClientSize.Width - borderSize;
                    var top = pos.Y <= borderSize;
                    var bottom = pos.Y >= _form.ClientSize.Height - borderSize;

                    if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                    else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                }
                return;
            }

            const int WM_ERASEBKGND = 0x0014;

            // replace WM_ERASEBKGND messages with a solid color fill to
            // prevent a white flash when the window is re-drawn
            if (m.Msg == WM_ERASEBKGND) {
                // extract the device context (HDC) handle from the message
                var hdc = m.WParam;

                // wrap the HDC into a standard .NET Graphics object that contains
                // the correct solid color fill
                using (var g = Graphics.FromHdc(hdc)) {
                    var isDarkMode = IsWindowsInDarkMode();
                    var BackColor = isDarkMode ? Color.FromArgb(32, 32, 32) : SystemColors.Window;
                    using var brush = new SolidBrush(BackColor);
                    g.FillRectangle(brush, _form.ClientRectangle);
                }

                // tell Windows that we handled the message and no further processing is needed
                m.Result = 1;
                return;
            }

            base.WndProc(ref m);
        }
    }

    public static bool IsWindowsInDarkMode() {
        try {
            var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
            );
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch {
            return false;
        }
    }


}

sealed class DoubleBufferedPanel : Panel {
    public DoubleBufferedPanel() {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true
        );
    }

    // needed for the background color to actually draw
    protected override void OnPaint(PaintEventArgs e) {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        base.OnPaint(e);
    }
}

public partial class Form1 : Form {
    private readonly WebView2 _webView;
    private readonly DoubleBufferedPanel _splashPanel;
    private readonly DoubleBufferedPanel _backgroundPanel;
    private readonly WindowHelpers.TitlebarlessWindowHandler _titlebarHandler;

    protected override CreateParams CreateParams {
        get {
            CreateParams handleParam = base.CreateParams;
            handleParam.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return handleParam;
        }
    }

    protected override void OnLayout(LayoutEventArgs e) {
        base.OnLayout(e);

        // leave 1px around the panels so that the window border is visible
        const int margin = 8;
        var bounds = new Rectangle(0, 1, Width - margin * 2, Height - margin - 1);
        _splashPanel?.Bounds = bounds;
        _webView?.Bounds = bounds;
        _backgroundPanel?.Bounds = bounds;
    }

    public Form1() {
        var isDarkMode = WindowHelpers.IsWindowsInDarkMode();
        var BackColor = isDarkMode ? Color.FromArgb(32, 32, 32) : SystemColors.Window;

        InitializeComponent();
        _titlebarHandler = WindowHelpers.RemoveTitlebar(this);

        _splashPanel = new DoubleBufferedPanel { BackColor = BackColor, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        _splashPanel.Paint += SplashPanel_Paint;
        Controls.Add(_splashPanel);

        _webView = new WebView2 { };
        Controls.Add(_webView);

        _backgroundPanel = new DoubleBufferedPanel { BackColor = BackColor, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(_backgroundPanel);

        Load += Form1_Load;
    }

    private async void Form1_Load(object? sender, EventArgs e) {
        await Task.Delay(1);
        _titlebarHandler.TriggerFrameRecalculation();
        _titlebarHandler.ExtendFrameIntoClient();

        const int margin = 10;
        _splashPanel.Bounds = new Rectangle(margin, margin, Width - margin * 2, Height - margin * 2);

        try {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RAWeb.WebViewClient"
                )
            );

            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            _webView.DefaultBackgroundColor = BackColor;
            _webView.BackColor = BackColor;

            _webView.CoreWebView2.Navigate("https://localhost:5174");
            await _webView.CoreWebView2.ExecuteScriptWithResultAsync(@"return new Promise(resolve => {
                window.addEventListener('RAWebReady', () => resolve());
            })
            ");
            _webView.CoreWebView2.DocumentTitleChanged += (s, args) => {
                if (!string.IsNullOrEmpty(_webView.CoreWebView2.DocumentTitle)) {
                    Text = _webView.CoreWebView2.DocumentTitle;
                }
            };
        }
        finally {
            _webView.Visible = true;
            SuspendLayout();
            // Wait for two animation frames: the first fires before the paint, the second
            // fires after that paint has committed to screen, so content is actually visible.
            await _webView.CoreWebView2.ExecuteScriptWithResultAsync(
                "new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))"
            );
            _splashPanel.Visible = false;
            ResumeLayout();
        }
    }

    private void SplashPanel_Paint(object sender, PaintEventArgs e) {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // 96 DPI is standard 100% scaling. 144 DPI is 150%, etc.
        var dpiScaleX = g.DpiX / 96f;
        var dpiScaleY = g.DpiY / 96f;
        var targetSizeX = 96f * dpiScaleX;
        var targetSizeY = 96f * dpiScaleY;
        Console.WriteLine($"DPI Scale: {dpiScaleX}, {dpiScaleY}, Target Size: {targetSizeX}, {targetSizeY}");

        // Use the sender object to dynamically get the panel's current size
        var panel = (Panel)sender;
        var offsetX = (panel.Width - targetSizeX) / 2f;
        var offsetY = (panel.Height - targetSizeY) / 2f;

        // Center and scale the SVG viewport (64px to 48px)
        g.TranslateTransform(offsetX, offsetY);
        var scaleX = targetSizeX / 64f;
        var scaleY = targetSizeY / 64f;
        g.ScaleTransform(scaleX, scaleY);

        // SVG coordinates
        var blueBounds = new RectangleF(8, 8, 20, 20);
        var yellowBounds = new RectangleF(36, 8, 20, 20);
        var redBounds = new RectangleF(8, 36, 20, 20);
        var greenBounds = new RectangleF(36, 36, 20, 20);

        // Render shapes with gradients
        using (var blueBrush = new System.Drawing.Drawing2D.LinearGradientBrush(blueBounds.Location, new PointF(blueBounds.Right, blueBounds.Bottom), Color.FromArgb(0x64, 0xB5, 0xF6), Color.FromArgb(0x19, 0x76, 0xD2)))
        using (var yellowBrush = new System.Drawing.Drawing2D.LinearGradientBrush(yellowBounds.Location, new PointF(yellowBounds.Right, yellowBounds.Bottom), Color.FromArgb(0xFF, 0xD5, 0x4F), Color.FromArgb(0xF5, 0x7C, 0x00)))
        using (var redBrush = new System.Drawing.Drawing2D.LinearGradientBrush(redBounds.Location, new PointF(redBounds.Right, redBounds.Bottom), Color.FromArgb(0xEF, 0x53, 0x50), Color.FromArgb(0xC6, 0x28, 0x28)))
        using (var greenBrush = new System.Drawing.Drawing2D.LinearGradientBrush(greenBounds.Location, new PointF(greenBounds.Right, greenBounds.Bottom), Color.FromArgb(0x81, 0xC7, 0x84), Color.FromArgb(0x2E, 0x7D, 0x32))) {
            using (var path = GetRoundedRect(blueBounds, 4)) g.FillPath(blueBrush, path);
            using (var path = GetRoundedRect(yellowBounds, 4)) g.FillPath(yellowBrush, path);
            using (var path = GetRoundedRect(redBounds, 4)) g.FillPath(redBrush, path);

            g.FillEllipse(greenBrush, greenBounds);
        }
    }

    private System.Drawing.Drawing2D.GraphicsPath GetRoundedRect(RectangleF bounds, float radius) {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
