using System;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace FtpExplorer
{
    public sealed partial class MainPage : Page
    {
        private async void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Uri address;
                if (!Uri.TryCreate(addressBox.Text, UriKind.Absolute, out address))
                {
                    if (!Uri.TryCreate("ftp://" + addressBox.Text, UriKind.Absolute, out address))
                    {
                        return;
                    }
                }

                await NavigateAsync(address);
                if (history.Current != currentAddress)
                    history.Navigate(currentAddress);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(currentAddress);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(history.GoBack());
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(history.GoForward());
        }

        private void AnonymousCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            userNameBox.Text = "anonymous";
            userNameBox.IsEnabled = false;
            passwordBox.Password = "anonymous";
            passwordBox.IsEnabled = false;
        }

        private void AnonymousCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            userNameBox.Text = string.Empty;
            userNameBox.IsEnabled = true;
            passwordBox.Password = string.Empty;
            passwordBox.IsEnabled = true;
        }

        private async void LoginSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }

        private async void LoginPanel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (loginSubmitButton.IsEnabled)
            {
                if (e.Key == VirtualKey.Enter)
                {
                    e.Handled = true;
                    await LoginAsync();
                }
            }
        }

        private void LoginFlyout_Opening(object sender, object e)
        {
            loginErrorMessage.Visibility = Visibility.Collapsed;
        }
    }
}