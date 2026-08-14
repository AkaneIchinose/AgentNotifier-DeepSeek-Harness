namespace AgentNotifier.Notify;

/// <summary>运行时生成的蓝色圆形托盘图标</summary>
public static class TrayIconFactory
{
    public static System.Drawing.Icon Create()
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(79, 195, 247));
                g.FillEllipse(brush, 0, 0, 15, 15);
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
                g.DrawEllipse(pen, 4, 4, 7, 7);
            }
            var h = bmp.GetHicon();
            try { return System.Drawing.Icon.FromHandle(h); }
            catch { return System.Drawing.SystemIcons.Application; }
        }
        catch { return System.Drawing.SystemIcons.Application; }
    }
}
