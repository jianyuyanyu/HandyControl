using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Media;

namespace WpfApp1;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var prepWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Background = Brushes.Black
        };
        prepWindow.Show();
        prepWindow.Hide(); // 此时渲染管线已完成初始化

        base.OnStartup(e);
    }
}
