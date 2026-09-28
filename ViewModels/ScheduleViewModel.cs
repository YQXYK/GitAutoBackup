using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitAutoBackup.Models;
using GitAutoBackup.Services;

namespace GitAutoBackup.ViewModels;

/// <summary>「定时设置」页：编辑定时间隔，并把变更通知宿主（触发重新调度）。</summary>
public class ScheduleViewModel : INotifyPropertyChanged
{
    private readonly Settings _settings;

    public event Action? Changed;

    public string[] IntervalUnits { get; } = { "分钟", "小时", "天" };

    public bool SchedulerEnabled
    {
        get => _settings.SchedulerEnabled;
        set { _settings.SchedulerEnabled = value; OnPropertyChanged(); Changed?.Invoke(); Save(); }
    }

    public double IntervalValue
    {
        get => _settings.IntervalValue;
        set { _settings.IntervalValue = value; OnPropertyChanged(); Changed?.Invoke(); Save(); }
    }

    public string IntervalUnit
    {
        get => _settings.IntervalUnit;
        set { _settings.IntervalUnit = value; OnPropertyChanged(); Changed?.Invoke(); Save(); }
    }

    public ScheduleViewModel(Settings settings)
    {
        _settings = settings;
    }

    private void Save() => SettingsService.Save(_settings);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}