using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace FtpExplorer
{
    class FtpJobsViewModel
    {
        FtpJobManager manager;
        CoreDispatcher dispatcher;

        ObservableCollection<FtpJobViewModel> _jobVMs;

        public ReadOnlyObservableCollection<FtpJobViewModel> JobVMs { get; private set; }

        public FtpJobsViewModel(FtpJobManager manager, CoreDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;

            this.manager = manager;
            _jobVMs = new ObservableCollection<FtpJobViewModel>(
                this.manager.Jobs.Select(job => new FtpJobViewModel(job, dispatcher)));
            JobVMs = new ReadOnlyObservableCollection<FtpJobViewModel>(_jobVMs);
            (this.manager.Jobs as INotifyCollectionChanged).CollectionChanged += JobsChanged;
        }

        private async void JobsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (e.OldItems != null)
                {
                    var index = e.OldStartingIndex;
                    foreach (FtpJob job in e.OldItems)
                    {
                        _jobVMs.RemoveAt(index);
                        index++;
                    }
                }

                if (e.NewItems != null)
                {
                    var index = e.NewStartingIndex;
                    foreach (FtpJob job in e.NewItems)
                    {
                        _jobVMs.Insert(index, new FtpJobViewModel(job, dispatcher));
                        index++;
                    }
                }
            });
        }
    }

    class FtpJobViewModel : INotifyPropertyChanged
    {
        FtpJob job;

        public FtpJobViewModel(FtpJob job, CoreDispatcher dispatcher)
        {
            this.job = job;
            Name = job.Name;
            if (double.IsNaN(job.Progress))
            {
                Progress = 0;
                IsProgressIndeterminate = true;
            }
            else
            {
                Progress = job.Progress;
                IsProgressIndeterminate = false;
            }
            UpdateStatusFromJob(job);
            {
                job.ProgressChanged += async (sender, e) =>
                  {
                      await dispatcher.RunIdleAsync(e1 =>
                      {
                          if (double.IsNaN(job.Progress))
                          {
                              IsProgressIndeterminate = true;
                          }
                          else
                          {
                              IsProgressIndeterminate = false;
                              Progress = job.Progress;
                          }
                      });
                  };
            }
            {
                job.StatusChanged+= async (sender, e)=>
                {
                    await dispatcher.RunIdleAsync(e1 =>
                    {
                        UpdateStatusFromJob(job);
                    });
                };
            }
        }

        private void UpdateStatusFromJob(FtpJob job)
        {
            switch (job.Status)
            {
                case FtpJob.JobStatus.Cancelled:
                    Status = "已取消";
                    ShowProgress = false;
                    break;
                case FtpJob.JobStatus.Faulted:
                    var message = job.Exception?.Message ?? "无错误信息。";
                    if (job.Exception?.InnerException is FluentFTP.FtpCommandException cmdEx)
                    {
                        if (cmdEx.CompletionCode == "550")
                            message = "没有访问权限。";
                    }
                    Status = string.Format("出错：{0}", message);
                    ShowProgress = false;
                    break;
                case FtpJob.JobStatus.Completed:
                    Status = string.Format("已完成");
                    ShowProgress = false;
                    break;
                case FtpJob.JobStatus.Created:
                    Status = string.Empty;
                    ShowProgress = true;
                    break;
            }
        }

        private string _name;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    InvokePropertyChanged(nameof(Name));
                }
            }
        }

        private double _progress;

        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    InvokePropertyChanged(nameof(Progress));
                }
            }
        }

        private bool _isProgressIndeterminate;

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set
            {
                if (_isProgressIndeterminate!=value)
                {
                    _isProgressIndeterminate = value;
                    InvokePropertyChanged(nameof(IsProgressIndeterminate));
                }
            }
        }

        private string _status;

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    InvokePropertyChanged(nameof(Status));
                }
            }
        }

        private bool _showProgress;

        public bool ShowProgress
        {
            get => _showProgress;
            set
            {
                if (_showProgress != value)
                {
                    _showProgress = value;
                    InvokePropertyChanged(nameof(ShowProgress));
                }
            }
        }

        private void InvokePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Cancel()
        {
            job.CancellationTokenSource.Cancel();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
