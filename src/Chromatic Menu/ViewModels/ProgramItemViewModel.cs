using System;
using System.IO;
using System.Windows.Media;
using ChromaticMenu.Models;
using ChromaticMenu.Services;

namespace ChromaticMenu.ViewModels
{
    public class ProgramItemViewModel : ObservableObject
    {
        private readonly ProgramItem _model;
        private ImageSource _displayIcon;
        private string _fallbackIconName = "app-window";
        private bool _isBroken = false;
        private string _tabName = string.Empty;
        private string _toolTipText = string.Empty;

        private string _name;
        private bool _isEditingName;
        private string _editedName;

        public ProgramItem Model => _model;

        public string Id => _model.Id;
        public string TabId
        {
            get => _model.TabId;
            set
            {
                if (_model.TabId != value)
                {
                    _model.TabId = value;
                    OnPropertyChanged(nameof(TabId));
                }
            }
        }
        public int Order
        {
            get => _model.Order;
            set
            {
                if (_model.Order != value)
                {
                    _model.Order = value;
                    OnPropertyChanged(nameof(Order));
                }
            }
        }
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    _model.Name = value;
                    CheckTargetExists();
                }
            }
        }
        public string Kind => _model.Kind;
        public string Target => _model.Target;
        public string Arguments => _model.Arguments;
        public string WorkingDirectory => _model.WorkingDirectory;

        public bool IsEditingName
        {
            get => _isEditingName;
            set => SetProperty(ref _isEditingName, value);
        }

        public string EditedName
        {
            get => _editedName;
            set => SetProperty(ref _editedName, value);
        }

        public ImageSource DisplayIcon
        {
            get => _displayIcon;
            set => SetProperty(ref _displayIcon, value);
        }

        public string FallbackIconName
        {
            get => _fallbackIconName;
            set => SetProperty(ref _fallbackIconName, value);
        }

        public bool IsBroken
        {
            get => _isBroken;
            set => SetProperty(ref _isBroken, value);
        }

        public string TabName
        {
            get => _tabName;
            set => SetProperty(ref _tabName, value);
        }

        public string ToolTipText
        {
            get => _toolTipText;
            set => SetProperty(ref _toolTipText, value);
        }

        public static Action<string> OnLaunchError { get; set; }
        public static Action OnItemModified { get; set; }
        public static Action<ProgramItemViewModel> OnRequestEditDetails { get; set; }
        public static Action<ProgramItemViewModel> OnRequestRemove { get; set; }
        public static Action<ProgramItemViewModel, string> OnRequestMoveToTab { get; set; }
        public static System.Collections.ObjectModel.ObservableCollection<TabModel> AllTabs { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<TabModel> AvailableTabs => AllTabs;

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                {
                    OnPropertyChanged(nameof(CanShowFileLocation));
                    OnPropertyChanged(nameof(CanShowProperties));
                }
            }
        }

        public bool HasCustomIcon => _model.Icon != null && string.Equals(_model.Icon.Source, "custom", StringComparison.OrdinalIgnoreCase);

        public RelayCommand LaunchCommand { get; }
        public RelayCommand RunAsAdminCommand { get; }
        public RelayCommand OpenFileLocationCommand { get; }
        public RelayCommand ShowPropertiesCommand { get; }

        public RelayCommand StartRenameCommand { get; }
        public RelayCommand CommitRenameCommand { get; }
        public RelayCommand CancelRenameCommand { get; }
        public RelayCommand ChangeIconCommand { get; }
        public RelayCommand ResetIconCommand { get; }
        public RelayCommand EditDetailsCommand { get; }
        public RelayCommand MoveToTabCommand { get; }
        public RelayCommand RemoveCommand { get; }

        public bool CanShowFileLocation => !IsEditMode && !string.Equals(Kind, "url", StringComparison.OrdinalIgnoreCase);
        public bool CanShowProperties => !IsEditMode && !string.Equals(Kind, "url", StringComparison.OrdinalIgnoreCase);

        public ProgramItemViewModel(ProgramItem model, string tabName = "")
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _tabName = tabName;
            _name = _model.Name ?? string.Empty;
            _editedName = _name;

            LaunchCommand = new RelayCommand(() =>
            {
                if (!LaunchService.Instance.LaunchItem(this, out string error))
                {
                    OnLaunchError?.Invoke(error);
                }
            });

            RunAsAdminCommand = new RelayCommand(() =>
            {
                if (!LaunchService.Instance.RunAsAdmin(this, out string error))
                {
                    OnLaunchError?.Invoke(error);
                }
            });

            OpenFileLocationCommand = new RelayCommand(() =>
            {
                if (!LaunchService.Instance.OpenFileLocation(this, out string error))
                {
                    OnLaunchError?.Invoke(error);
                }
            });

            ShowPropertiesCommand = new RelayCommand(() =>
            {
                if (!LaunchService.Instance.ShowProperties(this, out string error))
                {
                    OnLaunchError?.Invoke(error);
                }
            });

            StartRenameCommand = new RelayCommand(() =>
            {
                EditedName = Name;
                IsEditingName = true;
            });

            CommitRenameCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrWhiteSpace(EditedName))
                {
                    Name = EditedName.Trim();
                    OnItemModified?.Invoke();
                }
                IsEditingName = false;
            });

            CancelRenameCommand = new RelayCommand(() =>
            {
                EditedName = Name;
                IsEditingName = false;
            });

            ChangeIconCommand = new RelayCommand(() =>
            {
                string path = _model.Icon?.Path;
                if (string.IsNullOrEmpty(path))
                {
                    path = Target;
                }
                path = Environment.ExpandEnvironmentVariables(path ?? string.Empty);
                int index = _model.Icon?.Index ?? 0;

                IntPtr ownerHwnd = IntPtr.Zero;
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    ownerHwnd = new System.Windows.Interop.WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;
                }

                if (IconService.Instance.ShowPickIconDialog(ownerHwnd, ref path, ref index))
                {
                    var newIcon = IconService.Instance.ExtractIconFromPathAndIndex(Id, path, index, 256);
                    if (newIcon != null)
                    {
                        DisplayIcon = newIcon;
                        _model.Icon = new IconConfig
                        {
                            Source = "custom",
                            Path = PathHelper.CollapsePath(path),
                            Index = index
                        };
                        OnPropertyChanged(nameof(HasCustomIcon));
                        OnItemModified?.Invoke();
                    }
                }
            });

            ResetIconCommand = new RelayCommand(() =>
            {
                _model.Icon = new IconConfig { Source = "auto", Path = null, Index = 0 };
                var defIcon = IconService.Instance.ResetIconToDefault(Id, Target, Kind, 256);
                DisplayIcon = defIcon;
                OnPropertyChanged(nameof(HasCustomIcon));
                OnItemModified?.Invoke();
            });

            EditDetailsCommand = new RelayCommand(() => OnRequestEditDetails?.Invoke(this));
            RemoveCommand = new RelayCommand(() => OnRequestRemove?.Invoke(this));
            MoveToTabCommand = new RelayCommand(param =>
            {
                if (param is TabModel targetTab)
                {
                    OnRequestMoveToTab?.Invoke(this, targetTab.Id);
                }
                else if (param is string targetTabId)
                {
                    OnRequestMoveToTab?.Invoke(this, targetTabId);
                }
            });

            SetDefaultFallbackIcon();
            CheckTargetExists();
            LoadIcon(256);
        }

        private void SetDefaultFallbackIcon()
        {
            if (string.Equals(Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                FallbackIconName = "globe";
            }
            else if (string.Equals(Kind, "folder", StringComparison.OrdinalIgnoreCase))
            {
                FallbackIconName = "folder";
            }
            else
            {
                FallbackIconName = "app-window";
            }
        }

        public void CheckTargetExists()
        {
            if (string.Equals(Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                IsBroken = false;
                ToolTipText = $"{Name}\n{Target}";
                return;
            }

            string expanded = Environment.ExpandEnvironmentVariables(Target ?? string.Empty);
            bool exists = File.Exists(expanded) || Directory.Exists(expanded);

            if (exists)
            {
                IsBroken = false;
                ToolTipText = $"{Name}\n{expanded}";
            }
            else
            {
                IsBroken = true;
                ToolTipText = $"Program not found: {expanded}";
            }
        }

        public void LoadIcon(int iconSize = 96)
        {
            var img = IconService.Instance.GetOrExtractIcon(Id, Target, Kind, iconSize);
            DisplayIcon = img;
        }
    }
}
