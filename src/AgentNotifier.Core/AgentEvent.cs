namespace AgentNotifier.Core;

/// <summary>事件类型：需要用户介入 / 任务完成</summary>
public enum EventKind { NeedsUser, Done }

/// <summary>消息细类：决定提醒弹窗中的类型徽标（选择 / 权限 / 提交结果 / 通用）</summary>
public enum MsgType { Generic, Choice, Permission, Result }

/// <summary>
/// 来自某工具的提醒事件。Agent 为来源代理标识（DSH 会话短 ID / 工具名）；
/// Model 为会话所用模型 ID（软件端自动识别）；ModelName 为目录显示名（如 DeepSeek-V4-Flash）；
/// AgentTitle 为会话标题（DSH 自动识别，可为空）。
/// </summary>
public sealed record AgentEvent(string Tool, EventKind Kind, string SessionId, string Summary, long Ts,
    string Agent = "", MsgType Msg = MsgType.Generic, string Model = "",
    string ModelName = "", string AgentTitle = "");

public static class EventKindText
{
    public static string Display(EventKind kind) =>
        kind == EventKind.NeedsUser ? "需要介入" : "任务完成";
}

public static class MsgTypeText
{
    public static string Display(MsgType msg) => msg switch
    {
        MsgType.Choice => "选择",
        MsgType.Permission => "权限",
        MsgType.Result => "提交结果",
        _ => "通用"
    };

    /// <summary>弹窗类型徽标颜色（BGR 风格 #RRGGBB）：选择=蓝，权限=琥珀，结果=绿，通用按事件类型</summary>
    public static string Color(MsgType msg, EventKind kind) => msg switch
    {
        MsgType.Choice => "#4FC3F7",
        MsgType.Permission => "#F59E0B",
        MsgType.Result => "#22C55E",
        _ => kind == EventKind.NeedsUser ? "#F59E0B" : "#22C55E"
    };
}
