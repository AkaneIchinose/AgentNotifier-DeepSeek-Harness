using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AgentNotifier.App.Views;
using AgentNotifier.Audio;
using AgentNotifier.Core;
using AgentNotifier.Tools;
using Microsoft.Win32;

namespace AgentNotifier.App;

public sealed class SoundChoice
{
    public string Key { get; init; } = "";
    public string Display { get; init; } = "";
}

public sealed class ToolStateUi
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#9CA3AF";
    public string Text { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private OverviewPage? _overview;
    private AudioPage? _audio;
    private NotifyPage? _notify;
    private OnboardPage? _onboard;
    private AboutPage? _about;
    private string _previewText = "点击对应工具的\"预览配置\"查看将写入的 hooks 配置。";

    public MainViewModel(AppServices svc)
    {
        _svc = svc;
        LoadSounds();
        ApplyAudioUi();
        RefreshEvents();
        RefreshToolStates();
        svc.Dsh.ConnectionChanged += _ =>
        {
            try { System.Windows.Application.Current.Dispatcher.Invoke(RefreshToolStates); }
            catch { RefreshToolStates(); }
        };
    }

    // ============ 导航 ============
    private UserControl? _currentPage;
    public UserControl? CurrentPage
    {
        get => _currentPage;
        private set { _currentPage = value; OnPropertyChanged(); }
    }

    public void SetPage(string tag)
    {
        switch (tag)
        {
            case "audio":
                _audio ??= new AudioPage { DataContext = this };
                ApplyAudioUi();
                CurrentPage = _audio;
                break;
            case "notify":
                _notify ??= new NotifyPage { DataContext = this };
                LoadDnd();
                LoadModelStyles();
                CurrentPage = _notify;
                break;
            case "onboard":
                _onboard ??= new OnboardPage { DataContext = this };
                RefreshToolStates();
                CurrentPage = _onboard;
                break;
            case "about":
                _about ??= new AboutPage { DataContext = this };
                CurrentPage = _about;
                break;
            default:
                _overview ??= new OverviewPage { DataContext = this };
                RefreshEvents();
                CurrentPage = _overview;
                break;
        }
    }

    // ============ 概览 ============
    public ObservableCollection<LogEntry> RecentEvents { get; } = new();
    public ObservableCollection<ToolStateUi> ToolStates { get; } = new();

    public void RefreshEvents()
    {
        RecentEvents.Clear();
        foreach (var e in _svc.Logs.Recent(50)) RecentEvents.Add(e);
    }

    public void RefreshToolStates()
    {
        ToolStates.Clear();
        ToolStates.Add(ToUi("Claude Code", _svc.Wizard.GetState(ToolKind.ClaudeCode)));
        var dshConnected = _svc.Dsh.Connected;
        ToolStates.Add(new ToolStateUi
        {
            Name = "DSH Web Harness",
            Color = dshConnected ? "#22C55E" : "#9CA3AF",
            Text = dshConnected ? "已监听" : "未连接（DSH 未运行）",
            Detail = "http://127.0.0.1:3080 · WebSocket 事件流监听，无需修改配置"
        });
    }

    private static ToolStateUi ToUi(string name, ToolState? s)
    {
        if (s == null) return new ToolStateUi { Name = name, Text = "未知" };
        var color = s.Hooked ? "#22C55E" : s.Error != null ? "#F59E0B" : "#9CA3AF";
        var text = s.Hooked ? "已接入" : s.Error != null ? s.Error : "未接入";
        return new ToolStateUi { Name = name, Color = color, Text = text, Detail = s.ConfigPath };
    }

    public string ServerInfo => "http://127.0.0.1:" + _svc.Cfg.Config.Server.Port;
    public string ServerStatusText => _svc.Server.Running ? "事件服务：运行中" : "事件服务：不可用（端口被占用）";
    public string ServerStatusColor => _svc.Server.Running ? "#22C55E" : "#EF4444";
    public string TokenShort => "令牌 " + _svc.Cfg.Config.Server.Token[..6] + "…（在接入页可预览 hooks 中的完整令牌）";

    public bool Muted
    {
        get => _svc.Cfg.Config.Muted;
        set
        {
            _svc.Cfg.Update(c => c.Muted = value);
            _svc.Tray.SetMuted(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MutedText));
            OnPropertyChanged(nameof(MutedColor));
        }
    }

    public string MutedText => Muted ? "已静音（只弹通知）" : "正常提醒（响铃 + 通知）";
    public string MutedColor => Muted ? "#EF4444" : "#22C55E";
    public void ToggleMute() => Muted = !Muted;
    public RelayCommand ToggleMuteCommand => new(_ => ToggleMute());

    public RelayCommand TestNeedsUserCommand => new(_ => PublishTest(EventKind.NeedsUser));
    public RelayCommand TestDoneCommand => new(_ => PublishTest(EventKind.Done));

    private void PublishTest(EventKind kind)
    {
        _svc.Bus.Publish(new AgentEvent("test", kind, "test-session",
            kind == EventKind.NeedsUser ? "测试：需要用户介入（选择/权限）" : "测试：任务完成（结果已提交）",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "test-agent",
            kind == EventKind.NeedsUser ? MsgType.Choice : MsgType.Result));
        RefreshEvents();
    }

    // ============ 音频 ============
    public IReadOnlyList<SoundChoice> SoundChoices { get; private set; } = Array.Empty<SoundChoice>();

    private void LoadSounds()
    {
        var list = new List<SoundChoice>();
        foreach (var k in _svc.Sounds.AllKeys())
            list.Add(new SoundChoice { Key = k, Display = _svc.Sounds.DisplayName(k) });
        SoundChoices = list;
        OnPropertyChanged(nameof(SoundChoices));
    }

    private SoundChoice? _needsUserChoice;
    public SoundChoice? NeedsUserChoice
    {
        get => _needsUserChoice;
        set
        {
            _needsUserChoice = value;
            if (value != null) _svc.Cfg.Update(c => c.NeedsUser.File = value.Key);
            OnPropertyChanged();
        }
    }

    private SoundChoice? _doneChoice;
    public SoundChoice? DoneChoice
    {
        get => _doneChoice;
        set
        {
            _doneChoice = value;
            if (value != null) _svc.Cfg.Update(c => c.Done.File = value.Key);
            OnPropertyChanged();
        }
    }

    public int NeedsUserVolume { get => _svc.Cfg.Config.NeedsUser.Volume; set { _svc.Cfg.Update(c => c.NeedsUser.Volume = value); OnPropertyChanged(); } }
    public int NeedsUserRepeats { get => _svc.Cfg.Config.NeedsUser.Repeats; set { _svc.Cfg.Update(c => c.NeedsUser.Repeats = value); OnPropertyChanged(); } }
    public double NeedsUserInterval { get => _svc.Cfg.Config.NeedsUser.IntervalSec; set { _svc.Cfg.Update(c => c.NeedsUser.IntervalSec = value); OnPropertyChanged(); } }
    public int DoneVolume { get => _svc.Cfg.Config.Done.Volume; set { _svc.Cfg.Update(c => c.Done.Volume = value); OnPropertyChanged(); } }
    public int DoneRepeats { get => _svc.Cfg.Config.Done.Repeats; set { _svc.Cfg.Update(c => c.Done.Repeats = value); OnPropertyChanged(); } }
    public double DoneInterval { get => _svc.Cfg.Config.Done.IntervalSec; set { _svc.Cfg.Update(c => c.Done.IntervalSec = value); OnPropertyChanged(); } }

    private void ApplyAudioUi()
    {
        NeedsUserChoice = SoundChoices.FirstOrDefault(s => s.Key == _svc.Cfg.Config.NeedsUser.File) ?? SoundChoices.FirstOrDefault();
        DoneChoice = SoundChoices.FirstOrDefault(s => s.Key == _svc.Cfg.Config.Done.File) ?? SoundChoices.FirstOrDefault();
        OnPropertyChanged(nameof(NeedsUserVolume));
        OnPropertyChanged(nameof(NeedsUserRepeats));
        OnPropertyChanged(nameof(NeedsUserInterval));
        OnPropertyChanged(nameof(DoneVolume));
        OnPropertyChanged(nameof(DoneRepeats));
        OnPropertyChanged(nameof(DoneInterval));
    }

    public RelayCommand PreviewNeedsUserCommand => new(_ =>
    {
        var a = _svc.Cfg.Config.NeedsUser;
        _ = _svc.Player.PlayAsync(a.File, a.Volume, 1, 0.3);
    });
    public RelayCommand PreviewDoneCommand => new(_ =>
    {
        var a = _svc.Cfg.Config.Done;
        _ = _svc.Player.PlayAsync(a.File, a.Volume, 1, 0.3);
    });

    public RelayCommand ImportSoundCommand => new(_ => ImportSound());
    public RelayCommand DeleteCustomSoundCommand => new(_ => DeleteCustomSound());

    private void ImportSound()
    {
        var dlg = new OpenFileDialog { Filter = "WAV 音频 (*.wav)|*.wav|所有文件 (*.*)|*.*", Title = "导入自定义音效" };
        if (dlg.ShowDialog() != true) return;
        var r = _svc.Sounds.Import(dlg.FileName);
        if (r.ok)
        {
            LoadSounds();
            ApplyAudioUi();
        }
        else MessageBox.Show(r.key, "导入失败");
    }

    private void DeleteCustomSound()
    {
        bool IsCustom(string? key) => key != null && (key.StartsWith("custom:") || key.StartsWith("mp3:") || key.StartsWith("flac:"));
        var target = IsCustom(NeedsUserChoice?.Key) ? NeedsUserChoice
            : IsCustom(DoneChoice?.Key) ? DoneChoice : null;
        if (target == null)
        {
            MessageBox.Show("请先在“需要介入”或“任务完成”中选择一个自定义音效，再点击删除。", "提示");
            return;
        }
        _svc.Sounds.Delete(target.Key);
        LoadSounds();
        ApplyAudioUi();
    }

    // ============ 通知 ============
    public bool ToastEnabled { get => _svc.Cfg.Config.ToastEnabled; set { _svc.Cfg.Update(c => c.ToastEnabled = value); OnPropertyChanged(); } }
    public int DebounceSec { get => _svc.Cfg.Config.DebounceSec; set { _svc.Cfg.Update(c => c.DebounceSec = value); OnPropertyChanged(); OnPropertyChanged(nameof(DebounceText)); } }
    public string DebounceText => DebounceSec + " 秒";

    public sealed class DndRuleUi
    {
        public DndRule Rule { get; init; } = new();
        public string Label => Rule.Start + " – " + Rule.End + "（" + (Rule.Mode == "silent" ? "完全静默" : "仅通知不响铃") + "）";
    }

    public ObservableCollection<DndRuleUi> DndRules { get; } = new();
    public string NewDndStart { get; set; } = "22:00";
    public string NewDndEnd { get; set; } = "08:00";
    public string NewDndMode { get; set; } = "完全静默";
    public string[] DndModeChoices => new[] { "silent", "toast_only" };
    public string DndModeDisplay(string m) => m == "silent" ? "完全静默" : "仅通知不响铃";

    public void LoadDnd()
    {
        DndRules.Clear();
        foreach (var r in _svc.Cfg.Config.Dnd) DndRules.Add(new DndRuleUi { Rule = r });
        OnPropertyChanged(nameof(NewDndStart));
        OnPropertyChanged(nameof(NewDndEnd));
        OnPropertyChanged(nameof(NewDndMode));
    }

    public RelayCommand AddDndCommand => new(_ => AddDnd());
    public RelayCommand RemoveDndCommand => new(p => RemoveDnd(p as DndRuleUi));

    private void AddDnd()
    {
        var mode = NewDndMode == "仅通知不响铃" ? "toast_only" : "silent";
        _svc.Cfg.Update(c => c.Dnd.Add(new DndRule { Start = NewDndStart, End = NewDndEnd, Mode = mode }));
        LoadDnd();
    }

    private void RemoveDnd(DndRuleUi? r)
    {
        if (r == null) return;
        _svc.Cfg.Update(c => c.Dnd.Remove(r.Rule));
        LoadDnd();
    }

    // ============ 弹窗（应用内富通知） ============
    public string[] NotifyModeChoices => new[] { "应用内弹窗（推荐）", "系统 Toast", "原生气泡" };

    public string NotifyMode
    {
        get => _svc.Cfg.Config.Notification.Mode switch
        {
            "toast" => "系统 Toast",
            "balloon" => "原生气泡",
            _ => "应用内弹窗（推荐）"
        };
        set
        {
            var m = value switch { "系统 Toast" => "toast", "原生气泡" => "balloon", _ => "window" };
            _svc.Cfg.Update(c => c.Notification.Mode = m);
            OnPropertyChanged();
        }
    }

    public string PopupImagePath
    {
        get => _svc.Cfg.Config.Notification.ImagePath;
        set
        {
            _svc.Cfg.Update(c => c.Notification.ImagePath = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PopupImagePathHint));
        }
    }

    public string PopupImagePathHint => PopupImagePath.Trim() == ""
        ? "未设置：弹窗不显示图片（支持 png/jpg/gif/bmp，相对路径按数据目录解析）"
        : PopupImagePath;

    public string PopupCustomText
    {
        get => _svc.Cfg.Config.Notification.CustomText;
        set
        {
            _svc.Cfg.Update(c => c.Notification.CustomText = value);
            OnPropertyChanged();
        }
    }

    public int PopupDurationSec
    {
        get => _svc.Cfg.Config.Notification.DurationSec;
        set
        {
            _svc.Cfg.Update(c => c.Notification.DurationSec = Math.Clamp(value, 3, 30));
            OnPropertyChanged();
            OnPropertyChanged(nameof(PopupDurationText));
        }
    }

    public string PopupDurationText => PopupDurationSec + " 秒";

    public RelayCommand ChoosePopupImageCommand => new(_ => ChoosePopupImage());
    public RelayCommand PreviewPopupCommand => new(_ => PopupNotifier.Preview(_svc.Cfg.Config, _svc.Cfg.BaseDir));

    private void ChoosePopupImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件 (*.*)|*.*",
            Title = "选择弹窗顶部图片"
        };
        if (dlg.ShowDialog() != true) return;
        PopupImagePath = dlg.FileName;
    }

    // ============ 模型样式（自动识别 + 按模型完全自定义） ============
    public sealed class ModelStyleUi
    {
        public ModelStyle Style { get; init; } = new();
        public string Display => string.IsNullOrWhiteSpace(Style.Name)
            ? (Style.ModelId != "" ? Style.ModelId : "（未命名模型）")
            : Style.Name + (Style.ModelId != "" && Style.ModelId != Style.Name ? " · " + Style.ModelId : "");
        public string ColorHex => string.IsNullOrWhiteSpace(Style.Color) ? "#9CA3AF" : Style.Color;
        public bool HasImage => !string.IsNullOrWhiteSpace(Style.ImagePath);
        public string StatusText => HasImage ? "已配置" : "默认";
    }

    public string[] ModelColorChoices => new[]
    {
        "#4FC3F7", "#A78BFA", "#2DD4BF", "#22C55E", "#F59E0B", "#EF4444", "#F472B6", "#9CA3AF"
    };

    public ObservableCollection<ModelStyleUi> ModelStyles { get; } = new();

    private ModelStyleUi? _selectedModel;
    public ModelStyleUi? SelectedModel
    {
        get => _selectedModel;
        set
        {
            _selectedModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedModelId));
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(SelectedIsDetected));
            OnPropertyChanged(nameof(EditName));
            OnPropertyChanged(nameof(EditColor));
            OnPropertyChanged(nameof(EditImagePath));
            OnPropertyChanged(nameof(EditTitle));
            OnPropertyChanged(nameof(EditContent));
        }
    }

    public string SelectedModelId => _selectedModel?.Style.ModelId ?? "";
    public string SelectedStatus => _selectedModel == null ? "" : _selectedModel.StatusText;

    public string NewModelId { get; set; } = "";

    public void LoadModelStyles()
    {
        var keepId = _selectedModel?.Style.ModelId;
        ModelStyles.Clear();
        foreach (var s in _svc.Cfg.Config.Models) ModelStyles.Add(new ModelStyleUi { Style = s });
        SelectedModel = ModelStyles.FirstOrDefault(m => m.Style.ModelId == keepId) ?? ModelStyles.FirstOrDefault();
        OnPropertyChanged(nameof(NewModelId));
    }

    /// <summary>自动识别到新模型后由 AppServices 调用（保持当前选中）</summary>
    public void RefreshModelStyles() => LoadModelStyles();

    // ---- 选中模型的编辑（写穿到配置） ----
    public string EditName
    {
        get => _selectedModel?.Style.Name ?? "";
        set { if (_selectedModel == null) return; _selectedModel.Style.Name = value; _svc.Cfg.Save(); OnPropertyChanged(nameof(SelectedModel)); OnPropertyChanged(); }
    }

    public string EditColor
    {
        get => _selectedModel?.Style.Color ?? "";
        set
        {
            if (_selectedModel == null) return;
            var v = (value ?? "").Trim();
            _selectedModel.Style.Color = v.StartsWith("#") ? v : "#" + v;
            _svc.Cfg.Save();
            OnPropertyChanged();
        }
    }

    public string EditImagePath
    {
        get => _selectedModel?.Style.ImagePath ?? "";
        set { if (_selectedModel == null) return; _selectedModel.Style.ImagePath = value; _svc.Cfg.Save(); OnPropertyChanged(nameof(SelectedStatus)); OnPropertyChanged(); }
    }

    public string EditTitle
    {
        get => _selectedModel?.Style.Title ?? "";
        set { if (_selectedModel == null) return; _selectedModel.Style.Title = value; _svc.Cfg.Save(); OnPropertyChanged(); }
    }

    public string EditContent
    {
        get => _selectedModel?.Style.Content ?? "";
        set { if (_selectedModel == null) return; _selectedModel.Style.Content = value; _svc.Cfg.Save(); OnPropertyChanged(); }
    }

    public RelayCommand PreviewSelectedModelCommand => new(_ =>
    {
        if (_selectedModel == null) return;
        PopupNotifier.Preview(_svc.Cfg.Config, _svc.Cfg.BaseDir, _selectedModel.Style);
    });

    /// <summary>重置默认：清空该模型的样式字段（显示名恢复目录名/ID），模型本身不可删除</summary>
    public RelayCommand ResetSelectedModelCommand => new(_ => ResetSelectedModel());

    /// <summary>删除：仅对"匹配关键词"类条目（无模型 ID）可用；识别到的模型只能重置</summary>
    public RelayCommand DeleteSelectedModelCommand => new(_ => DeleteSelectedModel(), _ => SelectedModel?.Style.ModelId == "");

    /// <summary>当前选中是否为识别到的模型（不可删除，只能重置）</summary>
    public bool SelectedIsDetected => _selectedModel != null && _selectedModel.Style.ModelId != "";

    public RelayCommand AddManualModelCommand => new(_ => AddManualModel());

    public RelayCommand ChooseSelectedImageCommand => new(_ => ChooseSelectedImage());

    private void AddManualModel()
    {
        var id = NewModelId.Trim();
        if (id == "")
        {
            MessageBox.Show("请输入模型 ID（如 deepseek-v4-pro 或 claude）。", "模型样式");
            return;
        }
        if (_svc.Cfg.Config.Models.Any(x => x.ModelId == id))
        {
            MessageBox.Show("该模型已在列表中。", "模型样式");
            return;
        }
        _svc.Cfg.Update(c => c.Models.Add(new ModelStyle { ModelId = id, Name = id }));
        LoadModelStyles();
        SelectedModel = ModelStyles.FirstOrDefault(m => m.Style.ModelId == id);
    }

    /// <summary>重置默认样式：模型保留，颜色/图片/标题/内容清空，显示名恢复为目录显示名或模型 ID</summary>
    private void ResetSelectedModel()
    {
        if (_selectedModel == null) return;
        var id = _selectedModel.Style.ModelId;
        var catalogName = id != "" && _svc.Dsh.KnownModels().TryGetValue(id, out var n) && n != "" ? n : id;
        if (MessageBox.Show("重置「" + _selectedModel.Display + "」为默认样式？\n\n模型本身会保留，仅清空颜色、图片、标题与内容设置。", "模型样式", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _svc.Cfg.Update(c =>
        {
            var s = c.Models.FirstOrDefault(x => ReferenceEquals(x, _selectedModel.Style));
            if (s == null) return;
            s.Name = catalogName;
            s.Color = "";
            s.ImagePath = "";
            s.Title = "";
            s.Content = "";
        });
        LoadModelStyles();
        SelectedModel = ModelStyles.FirstOrDefault(m => m.Style.ModelId == id);
    }

    private void DeleteSelectedModel()
    {
        if (_selectedModel == null || _selectedModel.Style.ModelId != "") return;
        if (MessageBox.Show("删除匹配规则「" + _selectedModel.Display + "」？", "模型样式", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _svc.Cfg.Update(c => c.Models.Remove(_selectedModel.Style));
        LoadModelStyles();
    }

    private void ChooseSelectedImage()
    {
        if (_selectedModel == null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件 (*.*)|*.*",
            Title = "选择该模型的弹窗横幅图片"
        };
        if (dlg.ShowDialog() != true) return;
        EditImagePath = dlg.FileName;
    }

    // ============ 接入 ============
    public string PreviewText
    {
        get => _previewText;
        set { _previewText = value; OnPropertyChanged(); }
    }

    public RelayCommand InstallClaudeCommand => new(_ => Install(ToolKind.ClaudeCode));
    public RelayCommand RollbackClaudeCommand => new(_ => Rollback(ToolKind.ClaudeCode));
    public RelayCommand PreviewClaudeCommand => new(_ => Preview(ToolKind.ClaudeCode));
    public RelayCommand WriteUninstallCommand => new(_ => WriteUninstallScript());
    public RelayCommand RunUninstallCommand => new(_ => RunUninstall());

    private void Install(ToolKind k)
    {
        var r = _svc.Wizard.Install(k);
        MessageBox.Show(r.msg, r.ok ? "接入完成" : "接入失败");
        RefreshToolStates();
    }

    private void Rollback(ToolKind k)
    {
        var r = _svc.Wizard.Rollback(k);
        MessageBox.Show(r.msg, r.ok ? "已回滚" : "回滚失败");
        RefreshToolStates();
    }

    private void Preview(ToolKind k)
    {
        PreviewText = _svc.Wizard.Preview(k);
        RefreshToolStates();
    }

    private void WriteUninstallScript()
    {
        var path = _svc.Wizard.WriteUninstallScript(AppContext.BaseDirectory);
        MessageBox.Show("已生成一键恢复脚本：\n" + path + "\n\n运行它可删除全部写入的 hooks 并还原原始配置。", "一键恢复脚本");
    }

    private void RunUninstall()
    {
        var path = _svc.Wizard.WriteUninstallScript(AppContext.BaseDirectory);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"") { UseShellExecute = false });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "执行失败"); }
    }

    // ============ 关于 ============
    public string VersionText => "Agent 提醒器 v" + _svc.Cfg.Config.Version + "（M1+M2）";

    public string[] ThemeChoices => new[] { "跟随系统", "浅色", "深色" };
    public string Theme
    {
        get => _svc.Cfg.Config.Theme switch { "light" => "浅色", "dark" => "深色", _ => "跟随系统" };
        set
        {
            var mode = value switch { "浅色" => "light", "深色" => "dark", _ => "system" };
            _svc.Cfg.Update(c => c.Theme = mode);
            _svc.ApplyTheme(mode);
            OnPropertyChanged();
        }
    }

    public bool Autostart
    {
        get => _svc.Cfg.Config.Autostart;
        set
        {
            _svc.Cfg.Update(c => c.Autostart = value);
            _svc.ApplyAutostart(value);
            OnPropertyChanged();
        }
    }

    public RelayCommand OpenDataDirCommand => new(_ => OpenDir(_svc.Cfg.BaseDir));
    public RelayCommand OpenLogDirCommand => new(_ => OpenDir(_svc.Logs.LogsDir));
    public RelayCommand OpenAppDirCommand => new(_ => OpenDir(AppContext.BaseDirectory));

    private static void OpenDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch { }
    }
}
