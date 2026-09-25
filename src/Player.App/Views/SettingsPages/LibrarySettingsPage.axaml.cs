using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views.SettingsPages;

/// <summary>
/// 媒体库设置页。目录选择器属于平台操作，留在视图层（与主窗口"打开文件"的做法一致），
/// ViewModel 只接收路径字符串。
/// </summary>
public partial class LibrarySettingsPage : UserControl
{
    public LibrarySettingsPage() => InitializeComponent();

    private async void OnBrowseLibraryRootClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibrarySettingsViewModel library)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择媒体库根目录",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            library.LibraryRoot = path;
            StartupTrace.Mark($"设置：媒体库根目录选择为 {path}");
        }
    }
}
