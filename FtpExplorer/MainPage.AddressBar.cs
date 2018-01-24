using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace FtpExplorer
{
    public sealed partial class MainPage : Page
    {
        private const string RememberPasswordSetting = "RememberPassword";

        private async void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
                sender.Text = args.ChosenSuggestion as string;
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

        private async void AddressBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen)
                return;
            string text = sender.Text;
            string[] result;
            using (var db = new Data.HistoryContext())
            {
                var query = from Data.HistoryEntry item in db.Histories
                            where item.Url.Contains(text)
                            orderby item.Time descending
                            select item.Url;
                result = await query.Take(10).Distinct().ToArrayAsync();
            }
            sender.ItemsSource = result;
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
            userNameBox.Text = string.Empty;
            passwordBox.Password = string.Empty;
        }

        private async void UserNameBox_GettingFocus(UIElement sender, GettingFocusEventArgs args)
        {
            AutoSuggestBox autoSuggestBox = sender as AutoSuggestBox;
            if (currentAddress == null)
            {
                autoSuggestBox.ItemsSource = null;
            }
            else
            {
                var userNames = await PasswordManager.Current.GetUserNamesAsync(
                    currentAddress.Host, currentAddress.Port, autoSuggestBox.Text);
                autoSuggestBox.ItemsSource = userNames;
            }
        }

        private async void UserNameBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen)
                return;
            if (currentAddress == null)
            {
                sender.ItemsSource = null;
            }
            else
            {
                var userNames = await PasswordManager.Current.GetUserNamesAsync(
                    currentAddress.Host, currentAddress.Port, sender.Text);
                sender.ItemsSource = userNames;
            }
        }

        private async void UserNameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (currentAddress != null)
            {
                string password = await PasswordManager.Current.GetPasswordAsync(
                    currentAddress.Host, currentAddress.Port, userNameBox.Text);
                if (password != null)
                    passwordBox.Password = password;
            }
        }

        private async void UserNameBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                sender.Text = args.ChosenSuggestion as string;
                if (currentAddress != null)
                {
                    string password = await PasswordManager.Current.GetPasswordAsync(
                        currentAddress.Host, currentAddress.Port, userNameBox.Text);
                    if (password != null)
                        passwordBox.Password = password;
                }
                await LoginAsync();
            }
            else
            {
                passwordBox.Focus(FocusState.Keyboard);
            }
        }

        private void RememberPasswordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values[RememberPasswordSetting] = true;
        }

        private void RememberPasswordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values[RememberPasswordSetting] = false;
        }
    }
}