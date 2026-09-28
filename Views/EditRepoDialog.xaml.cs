using System.Collections.Generic;
using System.Windows;

namespace GitAutoBackup.Views;

/// <summary>编辑 GitHub 仓库（可见性 + 描述）的对话框。</summary>
public partial class EditRepoDialog : Window
{
    /// <summary>对话框返回的可见性（true=私有）。</summary>
    public bool IsPrivate { get; private set; }

    /// <summary>对话框返回的描述。</summary>
    public string Description { get; private set; } = string.Empty;

    public EditRepoDialog(string repoName, bool isPrivate, string description)
    {
        InitializeComponent();
        RepoNameText.Text = repoName;

        VisibilityBox.ItemsSource = new List<KeyValuePair<bool, string>>
        {
            new(true, "私有"),
            new(false, "公开"),
        };
        VisibilityBox.SelectedValuePath = "Key";
        VisibilityBox.SelectedValue = isPrivate;

        DescriptionBox.Text = description ?? string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        IsPrivate = VisibilityBox.SelectedValue is true;
        Description = DescriptionBox.Text ?? string.Empty;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
