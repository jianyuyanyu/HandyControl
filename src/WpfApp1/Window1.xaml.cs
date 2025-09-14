using System.Windows;
using HandyControl.Controls;
using Window = System.Windows.Window;

namespace WpfApp1;

public partial class Window1 : Window
{
    public Window1()
    {
        InitializeComponent();
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        Growl.Info("test");
    }
}

