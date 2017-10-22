using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace FtpExplorer
{
    public class FtpListItemViewModel
    {
        private static Dictionary<string, BitmapImage> iconCache = new Dictionary<string, BitmapImage>();
        private static BitmapImage folderIconCache;

        public static async Task<FtpListItemViewModel> FromFtpListItemAsync(FluentFTP.FtpListItem item)
        {
            var result = new FtpListItemViewModel
            {
                Source = item,
                FileName = item.Name,
                FileSize = GetSizeString(item.Size),
            };
            switch (item.Type)
            {
                case FluentFTP.FtpFileSystemObjectType.Directory:
                    result.Icon = await GetIconOfFolderAsync();
                    break;
                case FluentFTP.FtpFileSystemObjectType.File:
                    result.Icon = await GetIconOfFileAsync(Path.GetExtension(item.FullName));
                    break;
                case FluentFTP.FtpFileSystemObjectType.Link:
                    result.Icon = null;
                    break;
            }
            return result;
        }

        public FluentFTP.FtpListItem Source { get; private set; }

        public ImageSource Icon { get; private set; }

        public string FileName { get; private set; }

        public string FileSize { get; private set; }

        public bool AllowDrop => Source.Type == FluentFTP.FtpFileSystemObjectType.Directory;

        public static async Task<BitmapImage> GetIconOfFileAsync(string extension)
        {
            if (iconCache.ContainsKey(extension))
                return iconCache[extension];
            var dummyFileFolder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("dummy", CreationCollisionOption.OpenIfExists);
            var dummyFile = await dummyFileFolder.CreateFileAsync("empty" + extension, CreationCollisionOption.OpenIfExists);
            var icon = await dummyFile.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem);
            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(icon);
            iconCache.Add(extension, bitmapImage);
            return bitmapImage;
        }

        public static async Task<BitmapImage> GetIconOfFolderAsync()
        {
            if (folderIconCache != null)
                return folderIconCache;
            var dummyFileFolder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("dummy", CreationCollisionOption.OpenIfExists);
            var dummyFolder = await dummyFileFolder.CreateFolderAsync("emptyFolder", CreationCollisionOption.OpenIfExists);
            var icon = await dummyFolder.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem);
            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(icon);
            folderIconCache = bitmapImage;
            return bitmapImage;
        }

        public static string GetSizeString(long longSize)
        {
            double size;
            size = longSize;

            if (size < 0)
                return "";
            if (size < 0x400)
                return string.Format("{0}字节", size);
            if (size < (0x400 * 0x400))
                return string.Format("{0:N2} KiB", size / 0x400);
            if (size < (0x400 * 0x400 * 0x400))
                return string.Format("{0:N2} MiB", size / (0x400 * 0x400));
            if (size < (0x400L * 0x400 * 0x400 * 0x400))
                return string.Format("{0:N2} GiB", size / (0x400 * 0x400 * 0x400));
            if (size < (0x400L * 0x400 * 0x400 * 0x400 * 0x400))
                return string.Format("{0,N2} TiB", size / (0x400L * 0x400 * 0x400 * 0x400));
            return string.Format("{0,N2} PiB", size / (0x400L * 0x400 * 0x400 * 0x400 * 0x400));
        }
    }
}
