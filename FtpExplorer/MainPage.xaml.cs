using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace FtpExplorer
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        FluentFTP.FtpClient client;
        ObservableCollection<FtpListItemViewModel> listItemsVM = new ObservableCollection<FtpListItemViewModel>();

        /// <summary>
        /// 当前导航到的地址。考虑到FTP服务器不一定采用UTF-8编码，不需要对其转义。
        /// </summary>
        Uri currentAddress;
        History<Uri> history = new History<Uri>();
        SemaphoreSlim ftpSemaphore = new SemaphoreSlim(1, 1);
        CancellationTokenSource cancelSource = new CancellationTokenSource();

        FtpJobManager jobManager = new FtpJobManager();
        FtpJobsViewModel jobsViewModel;

        StorageFolder tempFolder;

        System.Text.Encoding preferredEncoding;

        public MainPage()
        {
            this.InitializeComponent();

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            preferredEncoding = System.Text.Encoding.GetEncoding("GBK");

            backButton.DataContext = history;
            forwardButton.DataContext = history;

            filesPanel.ItemsSource = listItemsVM;

            tempFolder = ApplicationData.Current.TemporaryFolder;

            jobsViewModel = new FtpJobsViewModel(jobManager, Dispatcher);
            jobListView.ItemsSource = jobsViewModel.JobVMs;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if(e.Parameter is Uri uri)
            {
                await NavigateAsync(uri);
                if (history.Current != currentAddress)
                    history.Navigate(currentAddress);
            }
        }

        /// <summary>
        /// 导航到指定的地址。需要登录时自动弹出登录界面。导航失败时，自动导航到失败页面。
        /// 不抛出异常。
        /// </summary>
        /// <param name="address">要导航到的地址</param>
        /// <returns>导航是否成功</returns>
        private async Task<bool> NavigateAsync(Uri address)
        {
            await ftpSemaphore.WaitAsync();
            try
            {
                errorMessage.Visibility = Visibility.Collapsed;
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;

                currentAddress = address;
                // 显示新的地址
                UriBuilder uriBuilder = new UriBuilder(currentAddress);
                uriBuilder.UserName = string.Empty;
                uriBuilder.Password = string.Empty;
                addressBox.Text = uriBuilder.Uri.ToString();


                // 不是ftp协议则抛出异常
                if (address.Scheme != "ftp" && address.Scheme != "ftps")
                    throw new InvalidOperationException("只支持ftp协议");

                // Host改变时重新连接
                if (client == null)
                {
                    client = CreateFtpClient(address, GetCredential(address.UserInfo), address.Scheme == "ftps");
                    await FtpConnectAsync(client);
                }
                else if (client.Host != address.Host ||
                    (client.EncryptionMode != FluentFTP.FtpEncryptionMode.None && address.Scheme == "ftp") ||
                    (client.EncryptionMode != FluentFTP.FtpEncryptionMode.Explicit && address.Scheme == "ftps") ||
                    (client.Port != 21 && address.Port < 0) ||
                    (client.Port != address.Port && address.Port >= 0) ||
                    !string.IsNullOrEmpty(address.UserInfo))
                {
                    client.Disconnect();
                    client.Dispose();
                    client = CreateFtpClient(address, GetCredential(address.UserInfo), address.Scheme == "ftps");
                    await FtpConnectAsync(client);
                }

                // FTP路径可能包含#号，#号后面的内容会被归入Fragment。
                string remotePath = address.LocalPath + address.Fragment;

                listItemsVM.Clear();
                foreach (var item in (await client.GetListingAsync(remotePath)).OrderBy(x => x.Name))
                    listItemsVM.Add(await FtpListItemViewModel.FromFtpListItemAsync(item));

                return true;
            }
            catch (FluentFTP.FtpCommandException exception)
            {
                if (exception.CompletionCode == "530")
                {
                    errorMessage.Text = "认证失败，请尝试登录。";
                    loginButton.Flyout.ShowAt(loginButton);
                    loginErrorMessage.Visibility = Visibility.Visible;
                }
                else
                {
                    errorMessage.Text = string.Format(
                        "发生错误，FTP返回代码：{0}。详细信息：\n{1}", exception.CompletionCode, exception.Message);
                }

                errorMessage.Visibility = Visibility.Visible;
                listItemsVM.Clear();

                return false;
            }
            catch (Exception exception)
            {
                errorMessage.Text = string.Format("发生错误。详细信息：\n{0}", exception.Message);
                errorMessage.Visibility = Visibility.Visible;
                listItemsVM.Clear();

                return false;
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                ftpSemaphore.Release();
            }
        }

        private FluentFTP.FtpClient CreateFtpClient(Uri address, System.Net.NetworkCredential credential, bool encrypt)
        {
            var client = new FluentFTP.FtpClient
            {
                Host = address.Host,
                Port = address.Port >= 0 ? address.Port : 21,
                Credentials = GetCredential(address.UserInfo),
            };

            client.DataConnectionType = FluentFTP.FtpDataConnectionType.PASV;
            client.DownloadDataType = FluentFTP.FtpDataType.Binary;
            if (encrypt)
            {
                client.EncryptionMode = FluentFTP.FtpEncryptionMode.Explicit;
                client.ValidateCertificate += FtpClient_ValidateCertificate;
            }
            else
                client.EncryptionMode = FluentFTP.FtpEncryptionMode.None;

            return client;
        }

        private void FtpClient_ValidateCertificate(FluentFTP.FtpClient control, FluentFTP.FtpSslValidationEventArgs e)
        {
            e.Accept = true;
        }

        private async Task FtpConnectAsync(FluentFTP.FtpClient client)
        {
            var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("log.log", CreationCollisionOption.OpenIfExists);
            FluentFTP.FtpTrace.LogToFile = file.Path;

            await client.ConnectAsync();
            if (client.Capabilities.HasFlag(FluentFTP.FtpCapability.UTF8))
            {
                client.Encoding = System.Text.Encoding.UTF8;
            }
            else
            {
                client.Encoding = preferredEncoding;
            }
        }

        private async Task OpenFileAsync(string remotePath)
        {
            await ftpSemaphore.WaitAsync();
            try
            {
                var file = await tempFolder.CreateFileAsync(Path.GetFileName(remotePath), CreationCollisionOption.GenerateUniqueName);
                jobManager.AddDownloadFile(client, remotePath, file, async () =>
                {
                    if (!await Launcher.LaunchFileAsync(file))
                        await Launcher.LaunchFolderAsync(tempFolder);
                });
                jobListFlyout.ShowAt(jobListButton);
            }
            catch(Exception ex)
            {
                ContentDialog dialog = new ContentDialog()
                {
                    Content = string.Format("发生错误，无法启动下载。\n错误信息：{0}", ex.Message),
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
            finally
            {
                ftpSemaphore.Release();
            }
        }

        private async Task UploadFilesAsync(IEnumerable<FileUploadInfo> fileInfos)
        {
            await ftpSemaphore.WaitAsync();
            try
            {
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;
                jobListFlyout.ShowAt(jobListButton);

                cancelSource = new CancellationTokenSource();
                bool overwriteAll = false;
                bool skipAll = false;

                var count = fileInfos.Count();
                int doneCount = 0;
                foreach (var fileInfo in fileInfos)
                {
                    if (!overwriteAll && await client.FileExistsAsync(fileInfo.RemotePath))
                    {
                        if (skipAll)
                            continue;
                        OverwriteDialog content = new OverwriteDialog
                        {
                            Text = string.Format("文件{0}已存在，是否覆盖？", fileInfo.File.Name),
                            CheckBoxText = "对所有项目执行此操作"
                        };
                        ContentDialog dialog = new ContentDialog()
                        {
                            Content = content,
                            PrimaryButtonText = "覆盖",
                            IsPrimaryButtonEnabled = true,
                            SecondaryButtonText = "跳过",
                            IsSecondaryButtonEnabled = true,
                            CloseButtonText = "取消"
                        };
                        switch (await dialog.ShowAsync())
                        {
                            case ContentDialogResult.Primary:
                                if (content.IsChecked)
                                    overwriteAll = true;
                                break;
                            case ContentDialogResult.Secondary:
                                if (content.IsChecked)
                                    skipAll = true;
                                continue;
                            case ContentDialogResult.None:
                                goto CancelAll;
                        }
                    }

                    jobManager.AddUploadFile(client, fileInfo.RemotePath, fileInfo.File, () => { });

                    doneCount++;
                    progressBar.Value = (double)doneCount / count * 100;
                }
                CancelAll: { }
            }
            finally
            {
                ftpSemaphore.Release();
                progressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task UploadStorageItems(IEnumerable<IStorageItem> storageItems, string remotePath)
        {
            try
            {
                List<FileUploadInfo> filesToUpload = new List<FileUploadInfo>();
                foreach (var item in storageItems)
                {
                    if (item is StorageFile file)
                    {
                        filesToUpload.Add(new FileUploadInfo
                        {
                            File = file,
                            RemotePath = Path.Combine(remotePath, file.Name)
                        });
                    }
                    else if (item is StorageFolder folder)
                    {
                        await FileUploadInfo.LoadFromFolderAsync(folder, remotePath, filesToUpload);
                    }
                    else
                    {
                        System.Diagnostics.Debugger.Break();
                    }
                }

                await UploadFilesAsync(filesToUpload);
            }
            catch (Exception ex)
            {
                string message;
                message = string.Format("无法创建上传任务。错误信息：\n{0}", ex.Message);
                ContentDialog dialog = new ContentDialog()
                {
                    Content = message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        /// <summary>
        /// 删除指定的项目并在删除前要求用户确认。
        /// </summary>
        /// <param name="item">要删除的项目</param>
        /// <returns>是否已删除项目</returns>
        private async Task<bool> DeleteItemAsync(FluentFTP.FtpListItem item)
        {
            await ftpSemaphore.WaitAsync();
            try
            {
                ContentDialog dialog = new ContentDialog()
                {
                    Content = string.Format("你确定要删除{0}吗？", item.Name),
                    PrimaryButtonText = "是",
                    CloseButtonText = "否"
                };
                var result = await dialog.ShowAsync();

                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;

                if (result != ContentDialogResult.Primary)
                    return false;
                switch (item.Type)
                {
                    case FluentFTP.FtpFileSystemObjectType.Directory:
                        await client.DeleteDirectoryAsync(item.FullName);
                        break;
                    case FluentFTP.FtpFileSystemObjectType.File:
                        await client.DeleteFileAsync(item.FullName);
                        break;
                    default:
                        throw new NotImplementedException("不支持删除除文件夹和文件以外的类型");
                }

                progressBar.Visibility = Visibility.Collapsed;

                ContentDialog resultDialog = new ContentDialog()
                {
                    Content = string.Format("已经成功删除{0}。", item.Name),
                    CloseButtonText = "确定"
                };
                await resultDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ContentDialog exceptionDialog = new ContentDialog()
                {
                    Content = string.Format("遇到未知错误。错误信息：{0}", ex.Message),
                    CloseButtonText = "确定"
                };
                await exceptionDialog.ShowAsync();
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;

                ftpSemaphore.Release();
            }
            return true;
        }

        private async Task CreateFolderAsync()
        {
            await ftpSemaphore.WaitAsync();
            try
            {
                DialogContentWithTextBox content = new DialogContentWithTextBox();
                content.ContentText = "新文件夹名称：";
                ContentDialog dialog = new ContentDialog()
                {
                    Content = content,
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消"
                };
                
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    progressBar.IsIndeterminate = true;
                    progressBar.Visibility = Visibility.Visible;

                    var currentPath = currentAddress.LocalPath + currentAddress.Fragment;
                    var remotePath = Path.Combine(currentPath, content.TextBoxText);
                    await client.CreateDirectoryAsync(remotePath);
                }
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                ftpSemaphore.Release();
            }
        }

        /// <summary>
        /// 从“用户名:密码”格式的字符串获取登录信息
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private static System.Net.NetworkCredential GetCredential(string source)
        {
            var index = source.IndexOf(':');
            if (index >= 0)
            {
                return new System.Net.NetworkCredential(
                    userName: source.Substring(0, index),
                    password: source.Substring(index + 1));
            }
            else if (string.IsNullOrEmpty(source))
            {
                return new System.Net.NetworkCredential(
                    userName: "anonymous",
                    password: "anonymous");
            }
            else
            {
                return new System.Net.NetworkCredential(
                    userName: source,
                    password: "");
            }
        }

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

        private async void FilesPanel_ItemClick(object sender, ItemClickEventArgs e)
        {
            var viewItem = e.ClickedItem as FtpListItemViewModel;
            var item = viewItem.Source;
            var addressBuilder = new UriBuilder(currentAddress);
            addressBuilder.Path = item.FullName;
            if (item.Type == FluentFTP.FtpFileSystemObjectType.Directory)
            {
                await NavigateAsync(addressBuilder.Uri);
                if (history.Current != currentAddress)
                    history.Navigate(currentAddress);
            }
            else
                await OpenFileAsync(item.FullName);
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
            loginSubmitButton.IsEnabled = false;
            await ftpSemaphore.WaitAsync();
            loginSubmitButton.IsEnabled = false;
            loginErrorMessage.Visibility = Visibility.Collapsed;
            loginProgressBar.Visibility = Visibility.Visible;
            try
            {
                if (client != null)
                {
                    try
                    {
                        client.Disconnect();
                        client.Credentials = new System.Net.NetworkCredential()
                        {
                            UserName = userNameBox.Text,
                            Password = passwordBox.Password
                        };
                        await FtpConnectAsync(client);
                        var navigateResult = NavigateAsync(currentAddress);
                        loginFlyout.Hide();
                    }
                    catch (FluentFTP.FtpCommandException exception)
                    {
                        if (exception.CompletionCode == "530")
                        {
                            loginErrorMessage.Visibility = Visibility.Visible;
                            loginErrorMessage.Text = "用户名或密码不正确，请重试";
                        }
                        else
                        {
                            throw exception;
                        }
                    }
                    catch (Exception ex)
                    {
                        loginErrorMessage.Visibility = Visibility.Visible;
                        loginErrorMessage.Text = string.Format("发生错误：{0}", ex.Message);
                    }
                }
                else
                {
                    loginErrorMessage.Visibility = Visibility.Visible;
                    loginErrorMessage.Text = "未连接到FTP服务器，无法登录。";
                }
            }
            finally
            {
                loginProgressBar.Visibility = Visibility.Collapsed;
                loginSubmitButton.IsEnabled = true;
                ftpSemaphore.Release();
            }
        }

        private void LoginFlyout_Opening(object sender, object e)
        {
            loginErrorMessage.Visibility = Visibility.Collapsed;
        }

        private void FilesPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.AvailableFormats.Contains(StandardDataFormats.StorageItems))
                e.AcceptedOperation = DataPackageOperation.Copy;
            else
                e.AcceptedOperation = DataPackageOperation.None;
        }

        private async void FilesPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.AvailableFormats.Contains(StandardDataFormats.StorageItems))
            {
                e.Handled = true;
                string remotePath = currentAddress.LocalPath + currentAddress.Fragment;
                await UploadStorageItems(await e.DataView.GetStorageItemsAsync(), remotePath);
            }
        }

        private void Folder_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.AvailableFormats.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                var senderVM = (sender as Grid).DataContext as FtpListItemViewModel;
                e.DragUIOverride.Caption = string.Format("复制到{0}", senderVM.FileName);
            }
            else
                e.AcceptedOperation = DataPackageOperation.None;
        }

        private async void Folder_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.AvailableFormats.Contains(StandardDataFormats.StorageItems))
            {
                e.Handled = true;
                var senderVM = (sender as Grid).DataContext as FtpListItemViewModel;

                string remotePath = currentAddress.LocalPath + currentAddress.Fragment;
                remotePath = Path.Combine(remotePath, senderVM.FileName);
                await UploadStorageItems(await e.DataView.GetStorageItemsAsync(), remotePath);
            }
        }

        private async void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var senderVM = (sender as MenuFlyoutItem).DataContext as FtpListItemViewModel;

            if (await DeleteItemAsync(senderVM.Source))
                await NavigateAsync(currentAddress);
        }

        private async void PanelMenuCreateFolder_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync();
            await NavigateAsync(currentAddress);
        }
    }

    struct FileUploadInfo
    {
        public StorageFile File { get; set; }
        public string RemotePath;

        public override string ToString()
        {
            return File.Name + "," + RemotePath;
        }

        public static async Task LoadFromFolderAsync(StorageFolder folder, string remotePath, ICollection<FileUploadInfo> resultOutput)
        {
            string newPath = Path.Combine(remotePath, folder.Name);
            var files = await folder.GetFilesAsync(Windows.Storage.Search.CommonFileQuery.DefaultQuery, 0, 100);
            foreach (var file in files)
            {
                resultOutput.Add(new FileUploadInfo
                {
                    File = file,
                    RemotePath = Path.Combine(newPath, file.Name)
                });
            }
            foreach(var subFolder in await folder.GetFoldersAsync())
            {
                await LoadFromFolderAsync(subFolder, newPath, resultOutput);
            }
        }
    }
}
