using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Core;

namespace FtpExplorer
{
    class FtpJobManager
    {
        SemaphoreSlim semaphore = new SemaphoreSlim(1);

        private ObservableCollection<FtpJob> _jobs = new ObservableCollection<FtpJob>();
        private ReadOnlyObservableCollection<FtpJob> readOnlyJobs;
        public ReadOnlyObservableCollection<FtpJob> Jobs => readOnlyJobs;

        public FtpJobManager()
        {
            readOnlyJobs = new ReadOnlyObservableCollection<FtpJob>(_jobs);
        }

        private static FluentFTP.FtpClient CloneFtpClient(FluentFTP.FtpClient client)
        {
            var newClient = new FluentFTP.FtpClient
            {
                Host = client.Host,
                Credentials = client.Credentials,
                DataConnectionType = client.DataConnectionType,
                Encoding = client.Encoding,
                EncryptionMode = client.EncryptionMode
            };
            newClient.ValidateCertificate += FtpClient_ValidateCertificate;
            return newClient;
        }

        private static void FtpClient_ValidateCertificate(FluentFTP.FtpClient control, FluentFTP.FtpSslValidationEventArgs e)
        {
            e.Accept = true;
        }

        public void AddDownloadFile(FluentFTP.FtpClient client, string remotePath, StorageFile localFile, Action callBack)
        {
            client = CloneFtpClient(client);
            FtpJob job = new FtpJob();
            var cts = new CancellationTokenSource();
            var progress = new Progress<double>(x =>
            {
                job.Progress = x;
            });

            job.Task = DownloadFileAsync(client, remotePath, localFile, cts.Token, progress).ContinueWith(x =>
            {
                if (!cts.IsCancellationRequested)
                {
                    job.Progress = double.NaN;
                    try
                    {
                        callBack();
                    }
                    catch { }
                }
                if (x.IsCanceled)
                    job.Status = FtpJob.JobStatus.Cancelled;
                else if (x.IsFaulted)
                    job.Status = FtpJob.JobStatus.Faulted;
                else if (x.IsCompleted)
                    job.Status = FtpJob.JobStatus.Completed;
                job.Exception = x.Exception;
            });
            job.Name = string.Format("下载{0}", Path.GetFileName(remotePath));
            job.CancellationTokenSource = cts;

            _jobs.Add(job);
        }

        private async Task DownloadFileAsync(FluentFTP.FtpClient client, string remotePath, StorageFile localFile, CancellationToken token, IProgress<double> progress)
        {
            await semaphore.WaitAsync();
            try
            {
                string remoteDirectory = Path.GetDirectoryName(remotePath);
                string remoteFileName = Path.GetFileName(remotePath);
                await client.SetWorkingDirectoryAsync(remoteDirectory);
                using (var stream = await localFile.OpenStreamForWriteAsync())
                {
                    await client.DownloadAsync(stream, remoteFileName, token, progress);
                }
            }
            finally
            {
                client.Dispose();
                semaphore.Release();
            }
        }

        public void AddUploadFile(FluentFTP.FtpClient client, string remotePath, StorageFile localFile, Action callBack)
        {
            client = CloneFtpClient(client);
            FtpJob job = new FtpJob();
            var cts = new CancellationTokenSource();
            var progress = new Progress<double>(x =>
            {
                job.Progress = x;
            });

            job.Task = UploadFileAsync(client, remotePath, localFile, cts.Token, progress).ContinueWith(x =>
            {
                if (!cts.IsCancellationRequested)
                {
                    job.Progress = double.NaN;
                    try
                    {
                        callBack();
                    }
                    catch { }
                }
                if (x.IsCanceled)
                    job.Status = FtpJob.JobStatus.Cancelled;
                else if (x.IsFaulted)
                    job.Status = FtpJob.JobStatus.Faulted;
                else if (x.IsCompleted)
                    job.Status = FtpJob.JobStatus.Completed;
                job.Exception = x.Exception;
            });
            job.Name = string.Format("上传{0}", Path.GetFileName(remotePath));
            job.CancellationTokenSource = cts;

            _jobs.Add(job);
        }

        private async Task UploadFileAsync(FluentFTP.FtpClient client, string remotePath, StorageFile localFile, CancellationToken token, IProgress<double> progress)
        {
            await semaphore.WaitAsync();
            try
            {
                string remoteDirectory = Path.GetDirectoryName(remotePath);
                string remoteFileName = Path.GetFileName(remotePath);
                while (!(await client.DirectoryExistsAsync(remoteDirectory)))
                {
                    string dir = Path.GetFileName(remoteDirectory);
                    remoteDirectory = Path.GetDirectoryName(remoteDirectory);
                    remoteFileName = Path.Combine(dir, remoteFileName);
                }
                await client.SetWorkingDirectoryAsync(remoteDirectory);
                using (var stream = await localFile.OpenStreamForReadAsync())
                {
                    await client.UploadAsync(stream, remoteFileName, FluentFTP.FtpExists.Overwrite, true, token, progress);
                }
            }
            finally
            {
                client.Dispose();
                semaphore.Release();
            }
        }
    }

    class FtpJob
    {
        private Deferral deferral;

        public Task Task { get; set; }
        public string Name { get; set; }

        private double _progress = double.NaN;

        /// <summary>
        /// Gets or sets the progress of the job. Value is <see cref="double.NaN"/> if progress not available.
        /// </summary>
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    ProgressChanged?.Invoke(this, new EventArgs());
                }
            }
        }

        private JobStatus _status = JobStatus.Created;

        public JobStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    if (_status == JobStatus.Created && value != JobStatus.Created)
                    {
                        if (deferral != null)
                        {
                            deferral.Complete();
                        }
                    }

                    _status = value;
                    StatusChanged?.Invoke(this, new EventArgs());
                }
            }
        }

        public enum JobStatus
        {
            Created,
            Cancelled,
            Faulted,
            Completed
        }

        public Exception Exception { get; set; }

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public FtpJob()
        {
            deferral = Zhaobang.ExtendedExecutionHelper.ExtendedExecutionHelper.GetDeferral();
        }

        public event EventHandler ProgressChanged;

        public event EventHandler StatusChanged;
    }
}
