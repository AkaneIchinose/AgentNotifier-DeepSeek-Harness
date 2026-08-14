using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AgentNotifier.App;

/// <summary>
/// 应用内富通知弹窗：类型徽标（选择/权限/提交结果）+ 模型徽标（按模型样式着色）+
/// Agent 标识 + 自定义图片/模型色横幅 + 自定义内容 + 摘要 + 时间页脚。
/// 右下角滑入、自动淡出，点击弹窗打开主窗口。
/// </summary>
public partial class NotifyWindow : Window
{
    private DispatcherTimer? _timer;

    public NotifyWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 填充弹窗内容。imagePath 非空且可加载 → 图片横幅；否则 bannerColor 非空 → 纯色横幅；
    /// 两者都无 → 不显示横幅。图片加载失败自动降级纯色/隐藏。
    /// </summary>
    public void Bind(string typeLabel, string typeColor, string modelLabel, string modelColor,
        string agentLabel, string title, string body, string summary,
        string imagePath, string bannerColor, string footer)
    {
        TypeText.Text = typeLabel;
        var typeBrush = BrushFromHex(typeColor);
        TypeChip.Background = typeBrush;
        TypeText.Foreground = ChipTextBrush(typeBrush);

        if (!string.IsNullOrWhiteSpace(modelLabel))
        {
            var mb = BrushFromHex(string.IsNullOrWhiteSpace(modelColor) ? "#9CA3AF" : modelColor);
            ModelChip.Background = mb;
            ModelText.Foreground = ChipTextBrush(mb);
            ModelText.Text = modelLabel;
            ModelChip.Visibility = Visibility.Visible;
        }
        AgentText.Text = agentLabel;
        TitleText.Text = title;
        BodyText.Text = body;
        if (summary != "" && summary != body)
        {
            SummaryText.Text = summary;
            SummaryText.Visibility = Visibility.Visible;
        }
        FooterText.Text = footer;

        var showedBanner = false;
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 1200;
                bmp.EndInit();
                bmp.Freeze();
                BannerImage.Source = bmp;
                BannerHost.Background = BrushFromHex(string.IsNullOrWhiteSpace(bannerColor) ? "#4FC3F7" : bannerColor);
                BannerHost.Visibility = Visibility.Visible;
                showedBanner = true;
            }
            catch { }
        }
        if (!showedBanner && !string.IsNullOrWhiteSpace(bannerColor))
        {
            BannerHost.Background = BrushFromHex(bannerColor);
            BannerHost.Visibility = Visibility.Visible;
        }
        UpdateBannerClip();
    }

    /// <summary>
    /// 图片裁剪：上沿与弹窗边沿一致为圆角（12px），下沿与文字区相接处保持直角。
    /// ClipToBounds 只按矩形裁剪，RectangleGeometry 四角同半径，因此用自定义路径：
    /// 上沿两角圆弧 + 左右/下沿直线。
    /// </summary>
    private void UpdateBannerClip()
    {
        var w = BannerHost.ActualWidth;
        var h = BannerHost.ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double r = 12;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, r), true, true);
            ctx.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(w - r, 0), true, false);
            ctx.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(w, h), true, false);
            ctx.LineTo(new Point(0, h), true, false);
        }
        geo.Freeze();
        BannerHost.Clip = geo;
    }

    private void BannerHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBannerClip();
    }

    /// <summary>入场动画：淡入 + 上滑（Window 的 RenderTransform 受系统限制，动画施加在内部 Border 上）</summary>
    public void PlayIn()
    {
        Opacity = 0;
        var rt = new TranslateTransform(0, 26);
        Root.RenderTransform = rt;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        rt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>定时自动关闭（3-30 秒）</summary>
    public void StartAutoClose(int seconds)
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 30)) };
        _timer.Tick += (_, _) => Dismiss();
        _timer.Start();
    }

    /// <summary>退场动画后关闭</summary>
    public void Dismiss()
    {
        _timer?.Stop();
        if (!IsVisible) return;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Dismiss();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        PopupNotifier.RaiseClick();
        Dismiss();
    }

    private static Brush BrushFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch { return new SolidColorBrush(Colors.SteelBlue); }
    }

    /// <summary>按背景亮度选择徽标文字颜色：亮底用深字，暗底用白字</summary>
    private static Brush ChipTextBrush(Brush bg)
    {
        if (bg is SolidColorBrush sb)
        {
            var lum = 0.299 * sb.Color.R + 0.587 * sb.Color.G + 0.114 * sb.Color.B;
            return new SolidColorBrush(lum > 150 ? Color.FromRgb(31, 41, 55) : Colors.White);
        }
        return new SolidColorBrush(Colors.White);
    }
}
