using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentNotifier.Core;

namespace AgentNotifier.App;

/// <summary>
/// 应用内富通知弹窗管理器：右下角堆叠显示、入场/退场动画、自动关闭；
/// 支持自定义图片与自定义内容模板（占位符见 RenderTemplate）。
/// </summary>
public static class PopupNotifier
{
    private static readonly List<NotifyWindow> Open = new();
    private static readonly object Gate = new();

    /// <summary>点击弹窗时执行的动作（由 AppServices 注入：显示主窗口）</summary>
    public static Action? ClickAction;

    public static void RaiseClick() => ClickAction?.Invoke();

    /// <summary>按事件 + 当前设置组装富内容并弹窗（可从任意线程调用）；baseDir 用于解析相对图片路径</summary>
    public static void Show(AgentEvent e, string toolDisplay, string defaultTitle, string summary, AppConfig cfg, string baseDir)
    {
        var typeLabel = e.Msg == MsgType.Generic ? EventKindText.Display(e.Kind) : MsgTypeText.Display(e.Msg);
        var typeColor = MsgTypeText.Color(e.Msg, e.Kind);
        var agentLabel = e.Tool == "dsh"
            ? (!string.IsNullOrWhiteSpace(e.AgentTitle)
                ? e.AgentTitle + "（" + e.Agent + "）"
                : (string.IsNullOrWhiteSpace(e.Agent) ? "DSH Web Harness" : "DSH 会话 " + e.Agent))
            : toolDisplay;
        var style = ResolveModelStyle(cfg, e.Model, e.Tool);
        // 模型显示名：事件自带 → 命中的模型样式名 → 原始 ID（{modelName} 占位符不再暴露原始 ID）
        if (e.ModelName == "" && style != null && !string.IsNullOrWhiteSpace(style.Name))
            e = e with { ModelName = style.Name };
        var rawModel = e.Model != "" ? e.Model : e.ModelName;
        var modelLabel = style != null
            ? (!string.IsNullOrWhiteSpace(style.Name) ? style.Name : (rawModel != "" ? rawModel : toolDisplay))
            : (e.ModelName != "" ? e.ModelName : "");
        var modelColor = style?.Color?.Trim() ?? "";

        // 标题：模型 Title 模板 → 默认标题
        var title = defaultTitle;
        if (style != null && !string.IsNullOrWhiteSpace(style.Title))
            title = RenderTemplate(style.Title, e, toolDisplay, defaultTitle, summary, agentLabel, typeLabel);

        // 内容：模型 Content → 全局自定义内容 → 事件摘要
        var body = summary;
        if (style != null && !string.IsNullOrWhiteSpace(style.Content))
            body = RenderTemplate(style.Content, e, toolDisplay, title, summary, agentLabel, typeLabel);
        else if (!string.IsNullOrWhiteSpace(cfg.Notification.CustomText))
            body = RenderTemplate(cfg.Notification.CustomText, e, toolDisplay, title, summary, agentLabel, typeLabel);

        var image = ResolveImagePath((cfg.Notification.ImagePath ?? "").Trim(), baseDir);
        if (style != null && !string.IsNullOrWhiteSpace(style.ImagePath))
        {
            var mi = ResolveImagePath(style.ImagePath.Trim(), baseDir);
            if (mi != "") image = mi;
        }
        var footer = DateTime.Now.ToString("HH:mm:ss") + " · " + toolDisplay +
            (string.IsNullOrWhiteSpace(e.SessionId) ? "" : " · 会话 " + e.SessionId) +
            (e.ModelName != "" ? " · " + e.ModelName : e.Model != "" ? " · " + e.Model : "");
        var duration = cfg.Notification.DurationSec <= 0 ? 8 : cfg.Notification.DurationSec;

        Dispatch(() => ShowCore(typeLabel, typeColor, modelLabel, modelColor, agentLabel, title, body, summary,
            image, modelColor, footer, duration));
    }

    /// <summary>设置页预览：用示例事件 + 当前设置即时弹窗；style 非空时按该模型样式预览</summary>
    public static void Preview(AppConfig cfg, string baseDir, ModelStyle? style = null)
    {
        var modelId = style != null && !string.IsNullOrWhiteSpace(style.ModelId) ? style.ModelId.Trim()
            : style != null && !string.IsNullOrWhiteSpace(style.Name) ? style.Name.Trim() : "deepseek-v4-flash";
        var modelName = style != null && !string.IsNullOrWhiteSpace(style.Name) ? style.Name.Trim() : "DeepSeek-V4-Flash";
        var sample = new AgentEvent("dsh", EventKind.NeedsUser, "a1b2c3d4e5f6g7h8",
            "示例：代理正在等待你的选择", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            "a1b2c3d4e5", MsgType.Choice, modelId, modelName, "示例会话");
        Show(sample, "DSH", "需要你的介入", "示例：代理正在等待你的选择（真实事件将显示实际摘要）", cfg, baseDir);
    }

    /// <summary>模型样式匹配：先按模型 ID 精确匹配，再按显示名/关键词子串匹配（不区分大小写）</summary>
    public static ModelStyle? ResolveModelStyle(AppConfig cfg, string model, string tool)
    {
        var models = cfg.Models ?? new List<ModelStyle>();
        var m = (model ?? "").Trim();
        if (m != "")
        {
            foreach (var s in models)
                if (!string.IsNullOrWhiteSpace(s.ModelId) &&
                    s.ModelId.Trim().Equals(m, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        var hay = (m + " " + (tool ?? "")).ToLowerInvariant();
        foreach (var s in models)
        {
            var key = (s.Name ?? "").Trim().ToLowerInvariant();
            if (key != "" && hay.Contains(key)) return s;
        }
        return null;
    }

    /// <summary>
    /// 模板占位符：
    /// {agent} 代理标识（含会话标题）· {tool} 工具 · {type} 消息类型 · {model} 模型 ID ·
    /// {modelName} 模型显示名（如 DeepSeek-V4-Flash）· {session} 会话 ID ·
    /// {summary} 事件摘要 · {time} 当前时间 · {title} 标题；空模板 = 直接显示摘要。
    /// </summary>
    private static string RenderTemplate(string tpl, AgentEvent e, string toolDisplay, string title,
        string summary, string agentLabel, string typeLabel)
    {
        if (string.IsNullOrWhiteSpace(tpl)) return summary;
        var time = DateTime.Now.ToString("HH:mm:ss");
        return tpl
            .Replace("{agent}", agentLabel)
            .Replace("{tool}", toolDisplay)
            .Replace("{type}", typeLabel)
            .Replace("{model}", e.Model)
            .Replace("{modelName}", e.ModelName != "" ? e.ModelName : e.Model)
            .Replace("{session}", string.IsNullOrWhiteSpace(e.SessionId) ? "-" : e.SessionId)
            .Replace("{summary}", summary)
            .Replace("{time}", time)
            .Replace("{title}", title);
    }

    /// <summary>
    /// 解析图片路径：绝对路径直接用；相对路径相对数据目录解析；不存在返回空。
    /// "builtin:fish" 等内置标记返回程序集内嵌资源（打包进 exe，不含路径信息）。
    /// </summary>
    private static string ResolveImagePath(string p, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        var t = p.Trim();
        if (t.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            return BuiltinImageUri(t["builtin:".Length..].Trim().ToLowerInvariant());
        var full = Path.IsPathRooted(t) ? t : Path.Combine(baseDir, t);
        return File.Exists(full) ? full : "";
    }

    /// <summary>内置图片 → 程序集 pack URI（单文件发布时随 exe 嵌入）</summary>
    private static string BuiltinImageUri(string key) => key switch
    {
        "fish" => "pack://application:,,,/fish.png",
        _ => ""
    };

    private static void Dispatch(Action a)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.CheckAccess()) { a(); return; }
        app.Dispatcher.Invoke(a);
    }

    private static void ShowCore(string typeLabel, string typeColor, string modelLabel, string modelColor,
        string agentLabel, string title, string body, string summary, string image, string bannerColor, string footer, int duration)
    {
        var win = new NotifyWindow();
        win.Bind(typeLabel, typeColor, modelLabel, modelColor, agentLabel, title, body, summary,
            image, bannerColor, footer);
        lock (Gate) Open.Add(win);
        win.Closed += (_, _) => { lock (Gate) Open.Remove(win); Reposition(); };
        // SizeToContent 下 Loaded 时尺寸可能未定型（会算错右下角位置），
        // 改为内容渲染完成后再定位，并在每次布局变化时重排整个堆叠。
        win.ContentRendered += (_, _) => win.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Reposition());
        win.SizeChanged += (_, _) => Reposition();
        win.Show();
        win.PlayIn();
        win.StartAutoClose(duration);
    }

    /// <summary>从工作区右下角向上堆叠：最新在底（贴近任务栏），旧的依次上移</summary>
    private static void Reposition()
    {
        List<NotifyWindow> snapshot;
        lock (Gate) snapshot = Open.Where(w => w.IsVisible).ToList();
        if (snapshot.Count == 0) return;
        var wa = SystemParameters.WorkArea;
        var y = wa.Bottom - 14;
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            var w = snapshot[i];
            y -= w.ActualHeight;
            w.Left = wa.Right - w.ActualWidth - 14;
            w.Top = y;
            y -= 10;
        }
    }
}
