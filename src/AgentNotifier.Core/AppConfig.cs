namespace AgentNotifier.Core;

public sealed class AudioSettings
{
    public string File { get; set; } = "";
    public int Volume { get; set; } = 70;       // 0-100
    public int Repeats { get; set; } = 3;       // 1-10
    public double IntervalSec { get; set; } = 1.0;
}

public sealed class DndRule
{
    public string Start { get; set; } = "22:00";
    public string End { get; set; } = "08:00";
    public string Mode { get; set; } = "silent"; // silent | toast_only
}

public sealed class ServerSettings
{
    public int Port { get; set; } = 28150;
    public string Token { get; set; } = "";
}

/// <summary>
/// 模型样式：一个模型一套完全自定义的弹窗（显示名/颜色/图片/标题/内容）。
/// 软件端自动识别事件来源模型（DSH session.models），按 ModelId 精确匹配，其次按 Name 子串匹配。
/// </summary>
public sealed class ModelStyle
{
    /// <summary>模型 ID（软件端自动识别写入，如 deepseek-v4-flash）；也兼容手动关键词匹配</summary>
    public string ModelId { get; set; } = "";
    /// <summary>显示名：弹窗模型徽标与选择列表显示（自动识别时写入目录显示名，如 DeepSeek-V4-Flash，可改）</summary>
    public string Name { get; set; } = "";
    /// <summary>徽标/横幅颜色 #RRGGBB（留空 = 按消息类型默认色）</summary>
    public string Color { get; set; } = "";
    /// <summary>该模型的弹窗横幅图片（留空 = 用全局弹窗图片；再留空 = 用模型颜色纯色横幅）</summary>
    public string ImagePath { get; set; } = "";
    /// <summary>该模型的弹窗标题模板（占位符见说明；留空 = 默认标题"需要你的介入/任务完成"）</summary>
    public string Title { get; set; } = "";
    /// <summary>该模型的弹窗回复内容模板（占位符见说明；留空 = 全局自定义内容，再留空 = 事件摘要）</summary>
    public string Content { get; set; } = "";
}

/// <summary>通知设置：弹窗模式（window=应用内富弹窗 / toast=系统 Toast / balloon=原生气泡）</summary>
public sealed class NotificationSettings
{
    public string Mode { get; set; } = "window";
    /// <summary>弹窗顶部自定义图片（绝对路径或相对数据目录路径；空 = 不显示图片）</summary>
    public string ImagePath { get; set; } = "";
    /// <summary>弹窗自定义内容模板，支持占位符 {agent} {tool} {type} {session} {summary} {time} {title}；空 = 直接显示摘要</summary>
    public string CustomText { get; set; } = "";
    /// <summary>弹窗显示时长（秒，3-30）</summary>
    public int DurationSec { get; set; } = 8;
}

public sealed class AppConfig
{
    public AudioSettings NeedsUser { get; set; } = new() { File = "soft-chime" };
    public AudioSettings Done { get; set; } = new() { File = "clear-alert" };
    public int DebounceSec { get; set; } = 30;
    public bool ToastEnabled { get; set; } = true;
    public bool Autostart { get; set; } = false;
    public bool Muted { get; set; } = false;
    public string Theme { get; set; } = "system"; // system | light | dark
    public List<DndRule> Dnd { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
    public NotificationSettings Notification { get; set; } = new();
    /// <summary>模型样式表（顺序匹配，第一条命中生效）</summary>
    public List<ModelStyle> Models { get; set; } = new();
    public string Version { get; set; } = "0.3.0";
}
