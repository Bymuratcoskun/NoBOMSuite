using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NoBOMSuite.Desktop;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}