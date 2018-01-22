using System;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace FtpExplorer
{
    public sealed partial class MainPage : Page
    {
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

        private async void PanelMenuCreateFolder_Click(object sender, RoutedEventArgs e)
        {
            await CreateFolderAsync();
            await NavigateAsync(currentAddress);
        }

        private async void PanelMenuUploadFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string remotePath = currentAddress.LocalPath + currentAddress.Fragment;

                FileOpenPicker picker = new FileOpenPicker();
                picker.CommitButtonText = "上传";
                picker.FileTypeFilter.Add("*");
                var files = await picker.PickMultipleFilesAsync();

                if (files.Any())
                {
                    FileTransferInfo[] fileUploadList = new FileTransferInfo[files.Count];
                    for (int i = 0; i < files.Count; i++)
                    {
                        fileUploadList[i] = new FileTransferInfo
                        {
                            File = files[i],
                            RemotePath = Path.Combine(remotePath, files[i].Name)
                        };
                    }
                    await UploadFilesAsync(fileUploadList);
                }
            }
            catch (Exception ex)
            {
                string message = string.Format("创建上传任务失败。错误信息：\n{0}", ex.Message);
                ContentDialog dialog = new ContentDialog
                {
                    Content = message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        private async void PanelMenuUploadFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string remotePath = currentAddress.LocalPath + currentAddress.Fragment;

                FolderPicker picker = new FolderPicker();
                picker.CommitButtonText = "上传";
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();

                if (folder != null)
                    await UploadStorageItems(new[] { folder }, remotePath);
            }
            catch (Exception ex)
            {
                string message = string.Format("创建上传任务失败。错误信息：\n{0}", ex.Message);
                ContentDialog dialog = new ContentDialog
                {
                    Content = message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        private async void PanelMenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(currentAddress);
        }

        private async void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var senderVM = (sender as MenuFlyoutItem).DataContext as FtpListItemViewModel;

            if (await DeleteItemAsync(senderVM.Source))
                await NavigateAsync(currentAddress);
        }

        private async void ContextMenuDownload_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as MenuFlyoutItem).DataContext as FtpListItemViewModel;
            var item = vm.Source;

            try
            {
                if (item.Type == FluentFTP.FtpFileSystemObjectType.File)
                {
                    FileSavePicker picker = new FileSavePicker();
                    picker.SuggestedFileName = item.Name;
                    string extension = Path.GetExtension(item.Name);
                    if (!extension.StartsWith('.'))
                        extension = ".";
                    picker.FileTypeChoices.Add(extension, new[] { extension });
                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        await DownloadFileAsync(item.FullName, file);
                    }
                }
                else if (item.Type == FluentFTP.FtpFileSystemObjectType.Directory)
                {
                    FolderPicker picker = new FolderPicker();
                    picker.CommitButtonText = "下载到此处";
                    picker.FileTypeFilter.Add("*");
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        var subFolder = await folder.CreateFolderAsync(item.Name, CreationCollisionOption.OpenIfExists);
                        await DownloadFolderAsync(item.FullName, subFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                string message;
                message = string.Format("无法创建下载任务。错误信息：\n{0}", ex.Message);
                ContentDialog dialog = new ContentDialog()
                {
                    Content = message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
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
    }
}