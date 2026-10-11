using System;
using System.Text;
using System.Threading.Tasks;
using Screenbox.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Screenbox.Dialogs;

/// <summary>
/// Asks for the 4-digit PIN an AirPlay receiver shows while pairing, one digit
/// per box. Typing moves to the next box, Backspace in an empty box moves back,
/// and pasting a PIN fills the boxes.
/// </summary>
public sealed partial class AirPlayPinDialog : ContentDialog
{
    private readonly TextBox[] _digits;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public AirPlayPinDialog(string receiverName)
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
        // The name is network-supplied text; a TextBlock title shows it literally.
        Title = Strings.Resources.AirPlayPinDialogTitle(receiverName);
        _digits = [Digit0, Digit1, Digit2, Digit3];
    }

    /// <summary>
    /// Shows the dialog and returns the entered PIN, or <see langword="null"/> when
    /// cancelled. The boxes are cleared afterwards; the PIN is never logged.
    /// </summary>
    public async Task<string?> GetPinAsync()
    {
        ContentDialogResult result = await ShowAsync();
        StringBuilder pin = new(_digits.Length);
        foreach (TextBox digit in _digits)
        {
            pin.Append(digit.Text);
            digit.Text = string.Empty;
        }

        return result == ContentDialogResult.Primary ? pin.ToString() : null;
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Digit0.Focus(FocusState.Programmatic);
    }

    private void Digit_OnBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        // Each box holds at most one digit.
        args.Cancel = args.NewText.Length > 1 || (args.NewText.Length == 1 && !IsDigit(args.NewText[0]));
    }

    private void Digit_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        int index = Array.IndexOf(_digits, box);
        if (box.Text.Length == 1 && index < _digits.Length - 1)
        {
            _digits[index + 1].Focus(FocusState.Keyboard);
        }

        UpdatePrimaryButton();
    }

    private void Digit_OnGotFocus(object sender, RoutedEventArgs e)
    {
        // Typing over a filled box replaces its digit.
        ((TextBox)sender).SelectAll();
    }

    private void Digit_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var box = (TextBox)sender;
        int index = Array.IndexOf(_digits, box);
        switch (e.Key)
        {
            case VirtualKey.Back when box.Text.Length == 0 && index > 0:
                _digits[index - 1].Text = string.Empty;
                _digits[index - 1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Left when index > 0:
                _digits[index - 1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Right when index < _digits.Length - 1:
                _digits[index + 1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Spreads a pasted PIN over the boxes, from the box pasted into.</summary>
    private async void Digit_OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        int index = Array.IndexOf(_digits, (TextBox)sender);
        DataPackageView clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.Text)) return;
        string text;
        try
        {
            text = await clipboard.GetTextAsync();
        }
        catch (Exception)
        {
            // The clipboard can be unavailable (for example, held by another app).
            return;
        }

        foreach (char c in text)
        {
            if (index >= _digits.Length) break;
            if (!IsDigit(c)) continue;
            _digits[index++].Text = c.ToString();
        }
    }

    private void UpdatePrimaryButton()
    {
        foreach (TextBox digit in _digits)
        {
            if (digit.Text.Length != 1)
            {
                IsPrimaryButtonEnabled = false;
                return;
            }
        }

        IsPrimaryButtonEnabled = true;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
