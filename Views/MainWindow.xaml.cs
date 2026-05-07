using System;
using System.Linq;
using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Plantagotchi.ViewModels;
using Microsoft.Toolkit.Uwp.Notifications;
using System.IO;
namespace Plantagotchi.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitClose = false;
    private DispatcherTimer _backgroundTimer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // --- SETTING UP THE BACKGROUND TIMER ---
        _backgroundTimer = new DispatcherTimer();
        // Set how often to check in the background (currently: every 4 hours)
        _backgroundTimer.Interval = TimeSpan.FromHours(4);
        _backgroundTimer.Tick += BackgroundTimer_Tick;
        _backgroundTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(ThemeToggle.IsChecked == true ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);
    }

    // --- BACKGROUND OPERATION AND NOTIFICATIONS ---

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isExplicitClose)
        {
            e.Cancel = true;
            this.Hide();

            // When minimizing to the tray, run an immediate check!
            CheckPlantsAndNotify();
        }
    }

    private void BackgroundTimer_Tick(object? sender, EventArgs e)
    {
        // This runs every 4 hours in the background
        CheckPlantsAndNotify();
    }

    private void CheckPlantsAndNotify()
    {
        if (DataContext is MainViewModel vm)
        {
            var thirstyPlants = vm.Plants.Where(p => p.NeedsWatering).Select(p => p.Name).ToList();
            var hasWeatherAlerts = vm.ShowWeatherAlerts;

            if (thirstyPlants.Any() || hasWeatherAlerts)
            {
                string iconPath = "file:///" + Path.GetFullPath("icon.ico");

                var toast = new ToastContentBuilder()
                    .AddAppLogoOverride(new Uri(iconPath, UriKind.Absolute))
                    .AddText("🌿 Plantagotchi Update");

                if (thirstyPlants.Any())
                {
                    toast.AddText($"Needs water: {string.Join(", ", thirstyPlants)}");
                }

                if (hasWeatherAlerts)
                {
                    toast.AddText("⚠️ Check the app for severe weather alerts!");
                }

                toast.Show();
            }
        }
    }
    private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowApp_Click(sender, e);
    }

    private void ShowApp_Click(object sender, RoutedEventArgs e)
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitClose = true;
        Application.Current.Shutdown();
    }

    // Test button to verify notifications at any time.
    private void TestNotify_Click(object sender, RoutedEventArgs e)
    {
        string iconPath = "file:///" + Path.GetFullPath("icon.ico");

        new ToastContentBuilder()
            .AddAppLogoOverride(new Uri(iconPath, UriKind.Absolute))
            .AddText("🌵 Plantagotchi Test")
            .AddText("Notifications are working perfectly!")
            .Show();
    }
}