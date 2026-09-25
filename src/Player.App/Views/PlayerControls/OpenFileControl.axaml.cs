using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Player.App.ViewModels;
using Player.Playback;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「打开文件」控件：可多选，第一个立即播放，其余按顺序入播放队列。
/// 文件选择器属于平台操作，留在视图层；ViewModel 只接收路径。
/// </summary>
public partial class OpenFileControl : UserControl
{
    public OpenFileControl() => InitializeComponent();

    private async void OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel player
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择媒体文件（可多选，按顺序连续播放）",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("媒体文件")
                {
                    Patterns = MediaKindResolver.SupportedExtensions.Select(static ext => "*" + ext).ToArray(),
                },
            ],
        });

        var paths = files
            .Select(static f => f.TryGetLocalPath())
            .Where(static p => !string.IsNullOrEmpty(p))
            .Select(static p => p!)
            .ToList();

        if (paths.Count > 0)
        {
            player.OpenFiles(paths);
        }
    }
}