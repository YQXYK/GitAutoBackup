using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitAutoBackup.ViewModels;

/// <summary>底部面板标签。</summary>
public enum BottomPanelTab
{
    Log,
    Terminal,
    Tasks,
}

/// <summary>一条任务记录（底部「任务」面板显示，最新在前）。</summary>
public class TaskRecord : INotifyPropertyChanged
{
    public string Title { get; init; } = string.Empty;

    public string TimeText { get; init; } = DateTime.Now.ToString("HH:mm:ss");

    private string _status = "进行中 ...";
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFailed));
        }
    }

    public bool IsFailed => _status.StartsWith('✗');

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
