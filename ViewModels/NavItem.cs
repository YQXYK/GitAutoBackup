using System.ComponentModel;

namespace GitAutoBackup.ViewModels;

/// <summary>导航项：标题 + 目标页面 ViewModel。</summary>
public class NavItem : INotifyPropertyChanged
{
    private readonly string _title;
    private readonly object _vm;
    private bool _isSelected;

    public NavItem(string title, object vm)
    {
        _title = title;
        _vm = vm;
    }

    public string Title => _title;

    /// <summary>当前选中状态（高亮左侧导航项）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
    }

    /// <summary>切换导航选中时展示的页面 ViewModel</summary>
    public object PageViewModel => _vm;

    public event PropertyChangedEventHandler? PropertyChanged;
}