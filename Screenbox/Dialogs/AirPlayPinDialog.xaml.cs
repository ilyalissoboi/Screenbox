using System;
using System.Threading.Tasks;
using Screenbox.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Dialogs;

/// <summary>
/// Asks for the PIN an AirPlay receiver shows while pairing.
/// </summary>
public sealed partial class AirPlayPinDialog : ContentDialog
{
    private const int MinPinLength = 4;
    private const int MaxPinLength = 8;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public AirPlayPinDialog(string receiverName)
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
        // The name is network-supplied text; a TextBlock title shows it literally.
        Title = Strings.Resources.AirPlayPinDialogTitle(receiverName);
    }

    /// <summary>
    /// Shows the dialog and returns the entered PIN, or <see langword="null"/> when
    /// cancelled. The PIN box is cleared afterwards; the PIN is never logged.
    /// </summary>
    public async Task<string?> GetPinAsync()
    {
        ContentDialogResult result = await ShowAsync();
        string pin = PinBox.Password;
        PinBox.Password = string.Empty;
        return result == ContentDialogResult.Primary ? pin : null;
    }

    private void PinBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        string pin = PinBox.Password;
        IsPrimaryButtonEnabled = pin.Length is >= MinPinLength and <= MaxPinLength && IsAllDigits(pin);
    }

    private static bool IsAllDigits(string text)
    {
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
