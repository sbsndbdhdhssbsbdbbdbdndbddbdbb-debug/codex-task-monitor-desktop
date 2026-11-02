using System.Windows;
using System.Windows.Controls;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public partial class SettingsWindow : Window
{
    private readonly MonitorSettings _settings;

    public SettingsWindow(MonitorSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        NotificationModeBox.SelectedValue = settings.NotificationMode.ToString();
        SoundCheck.IsChecked = settings.SoundEnabled;
        DoNotDisturbCheck.IsChecked = settings.DoNotDisturbEnabled;
        PrivacyCheck.IsChecked = settings.PrivacyMode;
        DesktopPetCheck.IsChecked = settings.DesktopPetEnabled;
        DesktopPetTopmostCheck.IsChecked = settings.DesktopPetAlwaysOnTop;
        DesktopPetAnimationsCheck.IsChecked = settings.DesktopPetAnimationsEnabled;
        PetApiCheck.IsChecked = settings.PetApiEnabled;
        AttentionReminderBox.Text = settings.AttentionReminderMinutes.ToString();
        PortBox.Text = settings.ApiPort.ToString();
        AutoStartCheck.IsChecked = settings.AutoStart;
        StartMinimizedCheck.IsChecked = settings.StartMinimized;
    }

    public MonitorSettings? Result { get; private set; }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1024 or > 65535)
        {
            MessageBox.Show(
                this,
                "端口请输入 1024 到 65535 之间的数字。",
                "端口不可用",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PortBox.Focus();
            return;
        }

        if (!int.TryParse(AttentionReminderBox.Text, out var reminderMinutes)
            || reminderMinutes is < 5 or > 240)
        {
            MessageBox.Show(
                this,
                "提醒间隔请输入 5 到 240 分钟之间的数字。",
                "提醒间隔不可用",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            AttentionReminderBox.Focus();
            return;
        }

        var selected = NotificationModeBox.SelectedItem as ComboBoxItem;
        var notificationMode = Enum.TryParse<NotificationMode>(
            selected?.Tag?.ToString(),
            out var parsed)
            ? parsed
            : NotificationMode.BackgroundOnly;
        Result = _settings with
        {
            NotificationMode = notificationMode,
            SoundEnabled = SoundCheck.IsChecked == true,
            DoNotDisturbEnabled = DoNotDisturbCheck.IsChecked == true,
            PrivacyMode = PrivacyCheck.IsChecked == true,
            DesktopPetEnabled = DesktopPetCheck.IsChecked == true,
            DesktopPetAlwaysOnTop = DesktopPetTopmostCheck.IsChecked == true,
            DesktopPetAnimationsEnabled = DesktopPetAnimationsCheck.IsChecked == true,
            PetApiEnabled = PetApiCheck.IsChecked == true,
            AttentionReminderMinutes = reminderMinutes,
            ApiPort = port,
            AutoStart = AutoStartCheck.IsChecked == true,
            StartMinimized = StartMinimizedCheck.IsChecked == true,
        };
        DialogResult = true;
    }
}
