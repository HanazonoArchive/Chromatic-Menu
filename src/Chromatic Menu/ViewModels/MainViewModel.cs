using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChromaticMenu.Models;
using ChromaticMenu.Services;

namespace ChromaticMenu.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private readonly SystemInfoService _systemInfoService;
        private readonly ToolLaunchService _toolLaunchService;
        private readonly PasswordService _passwordService;
        private readonly LaunchService _launchService;
        private readonly StartupService _startupService;
        private readonly FontService _fontService;
        private readonly ExportImportService _exportImportService;

        private string _shopName = "PisoNet";
        private string _shopLogoPath;
        private ImageSource _shopLogoSource;
        private string _searchQuery = string.Empty;
        private TabModel _selectedTab;
        private bool _isDarkMode = true;
        private bool _isEditMode = false;
        private string _inAppMessage;
        private bool _showInAppMessage;
        private int _iconSize = 96;
        private ImageSource _wallpaperSource;
        private string _wallpaperPath;
        private bool _startWithWindows;
        private string _startupError;
        private bool _isLoadingConfig = false;

        // Settings Navigation & Appearance State
        private int _settingsPageIndex = 0;
        private string _selectedAccentColor = "#0284C7";
        private FontFamily _currentFontFamily = new FontFamily("Segoe UI");
        private string _selectedFontFamily = "Segoe UI";
        private double _fontScale = 1.0;
        private string _customBgColor;
        private string _customSurfaceColor;
        private string _customTextColor;
        private string _customAccentColor;

        // Tabs Settings State
        private TabModel _selectedSettingsTab;
        private string _addTabName = string.Empty;
        private string _addTabIcon = "folder";
        private object _iconPickerTarget;
        private TabModel _pendingDeleteTab;
        private int _pendingDeleteTabItemCount;
        private string _pendingDeleteTabName;
        private ObservableCollection<TabModel> _availableMoveTargetTabs = new ObservableCollection<TabModel>();
        private TabModel _selectedMoveTargetTab;

        // Security Settings State
        private string _securitySuccess;
        private string _securityError;

        // Backup Settings State
        private string _backupStatusMessage;
        private string _backupErrorMessage;

        // Modal & Dialog state
        private DialogType _currentDialog = DialogType.None;
        private string _modalTitle = string.Empty;
        private string _modalMessage = string.Empty;
        private string _passwordError = string.Empty;
        private string _newPasswordError = string.Empty;
        private Action _pendingUnlockAction;

        public SystemInfoModel SystemInfo { get; } = new SystemInfoModel();
        public ObservableCollection<TechnicalToolModel> TechnicalTools { get; } = new ObservableCollection<TechnicalToolModel>();
        public ObservableCollection<TabModel> Tabs { get; } = new ObservableCollection<TabModel>();
        public ObservableCollection<ProgramItemViewModel> AllItems { get; } = new ObservableCollection<ProgramItemViewModel>();
        public ObservableCollection<ProgramItemViewModel> FilteredItems { get; } = new ObservableCollection<ProgramItemViewModel>();

        public string ShopName
        {
            get => _shopName;
            set
            {
                if (SetProperty(ref _shopName, value))
                {
                    if (_configService.Current?.Branding != null)
                    {
                        _configService.Current.Branding.ShopName = value;
                        if (!_isLoadingConfig) SaveConfig();
                    }
                }
            }
        }

        public string ShopLogoPath
        {
            get => _shopLogoPath;
            set => SetProperty(ref _shopLogoPath, value);
        }

        public ImageSource ShopLogoSource
        {
            get => _shopLogoSource;
            set => SetProperty(ref _shopLogoSource, value);
        }

        public string WallpaperPath
        {
            get => _wallpaperPath;
            set => SetProperty(ref _wallpaperPath, value);
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (_startWithWindows != value)
                {
                    if (_startupService.SetRunAtStartup(value, out string error))
                    {
                        _startWithWindows = value;
                        StartupError = null;
                        if (_configService.Current != null)
                        {
                            _configService.Current.StartWithWindows = value;
                            if (!_isLoadingConfig) SaveConfig();
                        }
                        OnPropertyChanged();
                    }
                    else
                    {
                        StartupError = error;
                        OnPropertyChanged();
                    }
                }
            }
        }

        public string StartupError
        {
            get => _startupError;
            set => SetProperty(ref _startupError, value);
        }

        public int SettingsPageIndex
        {
            get => _settingsPageIndex;
            set => SetProperty(ref _settingsPageIndex, value);
        }

        public string SelectedAccentColor
        {
            get => _selectedAccentColor;
            set
            {
                if (SetProperty(ref _selectedAccentColor, value))
                {
                    if (_configService.Current?.Appearance != null)
                    {
                        _configService.Current.Appearance.Accent = value;
                        ApplyAppearance(_configService.Current.Appearance);
                        if (!_isLoadingConfig) SaveConfig();
                    }
                }
            }
        }

        public static readonly string[] AccentPalette = new[]
        {
            "#0284C7", // Sky Blue (Default)
            "#6366F1", // Indigo
            "#10B981", // Emerald
            "#F59E0B", // Amber
            "#F43F5E", // Rose
            "#A855F7", // Purple
            "#06B6D4", // Cyan
            "#EA580C"  // Orange
        };

        public FontFamily CurrentFontFamily
        {
            get => _currentFontFamily;
            set => SetProperty(ref _currentFontFamily, value);
        }

        public List<string> AvailableFontFamilies => _fontService.GetCuratedInstalledFonts();

        public string SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                if (SetProperty(ref _selectedFontFamily, value))
                {
                    try
                    {
                        CurrentFontFamily = new FontFamily(value);
                    }
                    catch { }

                    if (_configService.Current?.Appearance != null)
                    {
                        _configService.Current.Appearance.FontFamily = value;
                        if (!_isLoadingConfig) SaveConfig();
                    }
                }
            }
        }

        public double FontScale
        {
            get => _fontScale;
            set
            {
                double clamped = Math.Max(0.8, Math.Min(1.4, Math.Round(value, 2)));
                if (SetProperty(ref _fontScale, clamped))
                {
                    OnPropertyChanged(nameof(FontScaleDisplay));
                    OnPropertyChanged(nameof(RootFontSize));
                    if (_configService.Current?.Appearance != null)
                    {
                        _configService.Current.Appearance.FontScale = clamped;
                        if (!_isLoadingConfig) SaveConfig();
                    }
                }
            }
        }

        public string FontScaleDisplay => $"{_fontScale:F2}x";
        public double RootFontSize => 14.0 * _fontScale;

        public bool IsDirectoryWritable => _configService.IsDirectoryWritable;
        public bool MustChangePassword => _configService.Current?.MustChangePassword ?? false;

        public ICommand CloseCommand { get; private set; }

        public string CustomBgColor
        {
            get => _customBgColor;
            set => SetProperty(ref _customBgColor, value);
        }

        public string CustomSurfaceColor
        {
            get => _customSurfaceColor;
            set => SetProperty(ref _customSurfaceColor, value);
        }

        public string CustomTextColor
        {
            get => _customTextColor;
            set => SetProperty(ref _customTextColor, value);
        }

        public string CustomAccentColor
        {
            get => _customAccentColor;
            set => SetProperty(ref _customAccentColor, value);
        }

        // Tabs Settings Properties
        public TabModel SelectedSettingsTab
        {
            get => _selectedSettingsTab;
            set => SetProperty(ref _selectedSettingsTab, value);
        }

        public string AddTabName
        {
            get => _addTabName;
            set => SetProperty(ref _addTabName, value);
        }

        public string AddTabIcon
        {
            get => _addTabIcon;
            set => SetProperty(ref _addTabIcon, value);
        }

        private static readonly string[] _availableTabIcons = new[]
        {
            "gamepad-2", "globe", "app-window", "graduation-cap", "layout-grid",
            "folder", "folder-plus", "play", "star", "heart",
            "flame", "zap", "music", "video", "film",
            "camera", "tv", "book", "code", "wrench",
            "coffee", "headphones", "message-square", "archive", "shield",
            "info", "pencil", "image", "sliders", "search"
        };

        public IReadOnlyList<string> AvailableTabIcons => _availableTabIcons;

        public int PendingDeleteTabItemCount
        {
            get => _pendingDeleteTabItemCount;
            set => SetProperty(ref _pendingDeleteTabItemCount, value);
        }

        public string PendingDeleteTabName
        {
            get => _pendingDeleteTabName;
            set => SetProperty(ref _pendingDeleteTabName, value);
        }

        public ObservableCollection<TabModel> AvailableMoveTargetTabs => _availableMoveTargetTabs;

        public TabModel SelectedMoveTargetTab
        {
            get => _selectedMoveTargetTab;
            set => SetProperty(ref _selectedMoveTargetTab, value);
        }

        // Security Properties
        public string SecuritySuccess
        {
            get => _securitySuccess;
            set => SetProperty(ref _securitySuccess, value);
        }

        public string SecurityError
        {
            get => _securityError;
            set => SetProperty(ref _securityError, value);
        }

        // Backup Properties
        public string BackupStatusMessage
        {
            get => _backupStatusMessage;
            set => SetProperty(ref _backupStatusMessage, value);
        }

        public string BackupErrorMessage
        {
            get => _backupErrorMessage;
            set => SetProperty(ref _backupErrorMessage, value);
        }

        // About Properties
        public string AppVersion => "1.0.0";
        public string TargetOs => "Windows 10 / Windows 11 (All Editions)";
        public string ConfigPath => _configService.ConfigPath;
        public string LogPath => Path.Combine(_configService.DataDirectory, "logs", "launcher.log");

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    UpdateFilteredItems();
                    OnPropertyChanged(nameof(IsSearching));
                }
            }
        }

        public TabModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    UpdateFilteredItems();
                    CheckAllShortcuts();
                }
            }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (SetProperty(ref _isDarkMode, value))
                {
                    OnPropertyChanged(nameof(IsLightMode));
                    ApplyTheme(value);
                }
            }
        }

        public bool IsLightMode
        {
            get => !_isDarkMode;
            set => IsDarkMode = !value;
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                {
                    foreach (var item in AllItems)
                    {
                        item.IsEditMode = value;
                    }
                }
            }
        }

        public int IconSize
        {
            get => _iconSize;
            set
            {
                if (SetProperty(ref _iconSize, value))
                {
                    if (_configService.Current?.Appearance != null)
                    {
                        _configService.Current.Appearance.IconSize = value;
                        if (!_isLoadingConfig) SaveConfig();
                    }
                }
            }
        }

        public ImageSource WallpaperSource
        {
            get => _wallpaperSource;
            set => SetProperty(ref _wallpaperSource, value);
        }

        public bool IsSearching => !string.IsNullOrWhiteSpace(SearchQuery);
        public bool HasNoItems => FilteredItems.Count == 0;
        public string EmptyStateMessage => IsSearching ? "No programs found" : "No programs in this tab";

        private bool _isDeepFreezeInstalled;
        public bool IsDeepFreezeInstalled
        {
            get => _isDeepFreezeInstalled;
            private set => SetProperty(ref _isDeepFreezeInstalled, value);
        }

        private bool _isSessionUnlocked;
        public bool IsSessionUnlocked
        {
            get => _isSessionUnlocked;
            set => SetProperty(ref _isSessionUnlocked, value);
        }

        public string InAppMessage
        {
            get => _inAppMessage;
            set
            {
                if (SetProperty(ref _inAppMessage, value))
                {
                    ShowInAppMessage = !string.IsNullOrEmpty(value);
                }
            }
        }

        public bool ShowInAppMessage
        {
            get => _showInAppMessage;
            set => SetProperty(ref _showInAppMessage, value);
        }

        // Dialog Properties
        public DialogType CurrentDialog
        {
            get => _currentDialog;
            set
            {
                if (SetProperty(ref _currentDialog, value))
                {
                    OnPropertyChanged(nameof(IsModalOpen));
                    OnPropertyChanged(nameof(IsPasswordDialogVisible));
                    OnPropertyChanged(nameof(IsChangePasswordDialogVisible));
                    OnPropertyChanged(nameof(IsSettingsHostVisible));
                    OnPropertyChanged(nameof(IsMessageDialogVisible));
                    OnPropertyChanged(nameof(IsConfirmDialogVisible));
                    OnPropertyChanged(nameof(IsAddWebsiteDialogVisible));
                    OnPropertyChanged(nameof(IsEditDetailsDialogVisible));
                    OnPropertyChanged(nameof(IsConfirmDeleteDialogVisible));
                    OnPropertyChanged(nameof(IsTabDeletePromptVisible));
                    OnPropertyChanged(nameof(IsIconPickerVisible));
                    OnPropertyChanged(nameof(IsCompactDialogVisible));
                }
            }
        }

        public bool IsModalOpen => CurrentDialog != DialogType.None;
        public bool IsPasswordDialogVisible => CurrentDialog == DialogType.Password;
        public bool IsChangePasswordDialogVisible => CurrentDialog == DialogType.ChangePassword;
        public bool IsSettingsHostVisible => CurrentDialog == DialogType.SettingsHost;
        public bool IsMessageDialogVisible => CurrentDialog == DialogType.Message;
        public bool IsConfirmDialogVisible => CurrentDialog == DialogType.Confirm;
        public bool IsAddWebsiteDialogVisible => CurrentDialog == DialogType.AddWebsite;
        public bool IsEditDetailsDialogVisible => CurrentDialog == DialogType.EditDetails;
        public bool IsConfirmDeleteDialogVisible => CurrentDialog == DialogType.ConfirmDelete;
        public bool IsTabDeletePromptVisible => CurrentDialog == DialogType.TabDeletePrompt;
        public bool IsIconPickerVisible => CurrentDialog == DialogType.IconPicker;
        public bool IsCompactDialogVisible => CurrentDialog != DialogType.None &&
                                              CurrentDialog != DialogType.SettingsHost &&
                                              CurrentDialog != DialogType.TabDeletePrompt &&
                                              CurrentDialog != DialogType.IconPicker;

        public string ModalTitle
        {
            get => _modalTitle;
            set => SetProperty(ref _modalTitle, value);
        }

        public string ModalMessage
        {
            get => _modalMessage;
            set => SetProperty(ref _modalMessage, value);
        }

        public string PasswordError
        {
            get => _passwordError;
            set => SetProperty(ref _passwordError, value);
        }

        public string NewPasswordError
        {
            get => _newPasswordError;
            set => SetProperty(ref _newPasswordError, value);
        }

        // Add Website Dialog State
        private string _websiteName = string.Empty;
        private string _websiteUrl = "https://";
        private string _websiteError = string.Empty;

        public string WebsiteName
        {
            get => _websiteName;
            set => SetProperty(ref _websiteName, value);
        }

        public string WebsiteUrl
        {
            get => _websiteUrl;
            set => SetProperty(ref _websiteUrl, value);
        }

        public string WebsiteError
        {
            get => _websiteError;
            set => SetProperty(ref _websiteError, value);
        }

        // Edit Details Dialog State
        private ProgramItemViewModel _editDetailsItem;
        private string _editItemName = string.Empty;
        private string _editItemTarget = string.Empty;
        private string _editItemArgs = string.Empty;
        private string _editItemWorkDir = string.Empty;
        private string _editDetailsError = string.Empty;

        public ProgramItemViewModel EditDetailsItem
        {
            get => _editDetailsItem;
            set => SetProperty(ref _editDetailsItem, value);
        }

        public string EditItemName
        {
            get => _editItemName;
            set => SetProperty(ref _editItemName, value);
        }

        public string EditItemTarget
        {
            get => _editItemTarget;
            set => SetProperty(ref _editItemTarget, value);
        }

        public string EditItemArgs
        {
            get => _editItemArgs;
            set => SetProperty(ref _editItemArgs, value);
        }

        public string EditItemWorkDir
        {
            get => _editItemWorkDir;
            set => SetProperty(ref _editItemWorkDir, value);
        }

        public string EditDetailsError
        {
            get => _editDetailsError;
            set => SetProperty(ref _editDetailsError, value);
        }

        // Confirm Delete Dialog State
        private List<ProgramItemViewModel> _pendingDeleteItems;
        private string _confirmDeleteMessage = string.Empty;

        public string ConfirmDeleteMessage
        {
            get => _confirmDeleteMessage;
            set => SetProperty(ref _confirmDeleteMessage, value);
        }

        // Commands
        public RelayCommand ToggleThemeCommand { get; }
        public RelayCommand ToggleSessionLockCommand { get; }
        public RelayCommand DismissMessageCommand { get; }
        public RelayCommand ClearSearchCommand { get; }
        public RelayCommand LaunchToolCommand { get; }
        public RelayCommand RequestSettingsCommand { get; }
        public RelayCommand RequestThemeCommand { get; }
        public RelayCommand RequestFontCommand { get; }
        public RelayCommand RequestEditModeCommand { get; }
        public RelayCommand ExitEditModeCommand { get; }
        public RelayCommand CloseModalCommand { get; }
        public RelayCommand SelectTabCommand { get; }
        public RelayCommand LaunchItemCommand { get; }
        public RelayCommand RunAsAdminCommand { get; }
        public RelayCommand OpenFileLocationCommand { get; }
        public RelayCommand ShowPropertiesCommand { get; }

        public RelayCommand AddProgramCommand { get; }
        public RelayCommand AddWebsiteCommand { get; }
        public RelayCommand AddFolderCommand { get; }
        public RelayCommand SubmitAddWebsiteCommand { get; }
        public RelayCommand SubmitEditDetailsCommand { get; }
        public RelayCommand BrowseTargetCommand { get; }
        public RelayCommand BrowseWorkDirCommand { get; }
        public RelayCommand SubmitConfirmDeleteCommand { get; }

        // Settings Commands
        public RelayCommand SelectSettingsPageCommand { get; }
        public RelayCommand OpenGithubCommand { get; }
        public RelayCommand BrowseLogoCommand { get; }
        public RelayCommand ClearLogoCommand { get; }
        public RelayCommand SelectAccentColorCommand { get; }
        public RelayCommand ApplyCustomColorsCommand { get; }
        public RelayCommand ResetAppearanceDefaultsCommand { get; }
        public RelayCommand BrowseWallpaperCommand { get; }
        public RelayCommand ClearWallpaperCommand { get; }
        public RelayCommand ResetFontScaleCommand { get; }
        public RelayCommand MoveTabUpCommand { get; }
        public RelayCommand MoveTabDownCommand { get; }
        public RelayCommand AddTabCommand { get; }
        public RelayCommand DeleteTabCommand { get; }
        public RelayCommand OpenTabIconPickerCommand { get; }
        public RelayCommand SelectTabIconCommand { get; }
        public RelayCommand SubmitDeleteTabMoveCommand { get; }
        public RelayCommand SubmitDeleteTabAllCommand { get; }
        public RelayCommand ExportBackupCommand { get; }
        public RelayCommand ImportBackupCommand { get; }
        public RelayCommand OpenConfigFolderCommand { get; }
        public RelayCommand OpenLogFolderCommand { get; }

        public Action OnFocusPasswordInput { get; set; }
        public Action OnFocusNewPasswordInput { get; set; }
        public static Func<List<ProgramItemViewModel>> GetSelectedItems { get; set; }

        public MainViewModel()
        {
            _configService = ConfigService.Instance;
            _systemInfoService = SystemInfoService.Instance;
            _toolLaunchService = ToolLaunchService.Instance;
            _passwordService = PasswordService.Instance;
            _launchService = LaunchService.Instance;
            _startupService = StartupService.Instance;
            _fontService = FontService.Instance;
            _exportImportService = ExportImportService.Instance;

            _isDeepFreezeInstalled = DeepFreezeService.Instance.IsDeepFreezeInstalled();

            ToggleSessionLockCommand = new RelayCommand(() =>
            {
                if (IsSessionUnlocked)
                {
                    IsSessionUnlocked = false;
                    if (IsEditMode)
                    {
                        IsEditMode = false;
                    }
                }
                else
                {
                    RequestUnlock(() => { /* Session is unlocked now */ });
                }
            });

            ProgramItemViewModel.OnLaunchError = msg => ShowMessage(msg);
            ProgramItemViewModel.OnItemModified = SaveConfig;
            ProgramItemViewModel.OnRequestEditDetails = OpenEditDetailsDialog;
            ProgramItemViewModel.OnRequestRemove = item =>
            {
                var selected = GetSelectedItems?.Invoke();
                if (selected != null && selected.Contains(item) && selected.Count > 1)
                {
                    PromptRemoveItems(selected);
                }
                else
                {
                    PromptRemoveItems(new[] { item });
                }
            };
            ProgramItemViewModel.OnRequestMoveToTab = (item, tabId) =>
            {
                var selected = GetSelectedItems?.Invoke();
                if (selected != null && selected.Contains(item) && selected.Count > 1)
                {
                    MoveItemsToTab(selected, tabId);
                }
                else
                {
                    MoveItemsToTab(new[] { item }, tabId);
                }
            };

            ToggleThemeCommand = new RelayCommand(() => IsDarkMode = !IsDarkMode);
            DismissMessageCommand = new RelayCommand(() => InAppMessage = null);
            ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);

            LaunchToolCommand = new RelayCommand(param =>
            {
                if (param is TechnicalToolModel tool)
                {
                    if (!_toolLaunchService.LaunchTool(tool, out string error))
                    {
                        ShowMessage(error);
                    }
                }
            });

            RequestSettingsCommand = new RelayCommand(() => RequestUnlock(() => OpenSettings(0)));
            RequestThemeCommand = new RelayCommand(() => RequestUnlock(() => OpenSettings(1)));
            RequestFontCommand = new RelayCommand(() => RequestUnlock(() => OpenSettings(1)));
            RequestEditModeCommand = new RelayCommand(() =>
            {
                if (IsEditMode)
                {
                    IsEditMode = false;
                }
                else
                {
                    RequestUnlock(() => IsEditMode = true);
                }
            });

            ExitEditModeCommand = new RelayCommand(() => IsEditMode = false);
            CloseModalCommand = new RelayCommand(CloseModal);

            SelectTabCommand = new RelayCommand(param =>
            {
                if (param is TabModel tab)
                {
                    SelectedTab = tab;
                }
            });

            LaunchItemCommand = new RelayCommand(param =>
            {
                if (param is ProgramItemViewModel item)
                {
                    if (!_launchService.LaunchItem(item, out string error))
                    {
                        ShowMessage(error);
                    }
                }
            });

            RunAsAdminCommand = new RelayCommand(param =>
            {
                if (param is ProgramItemViewModel item)
                {
                    if (!_launchService.RunAsAdmin(item, out string error))
                    {
                        ShowMessage(error);
                    }
                }
            });

            OpenFileLocationCommand = new RelayCommand(param =>
            {
                if (param is ProgramItemViewModel item)
                {
                    if (!_launchService.OpenFileLocation(item, out string error))
                    {
                        ShowMessage(error);
                    }
                }
            });

            ShowPropertiesCommand = new RelayCommand(param =>
            {
                if (param is ProgramItemViewModel item)
                {
                    if (!_launchService.ShowProperties(item, out string error))
                    {
                        ShowMessage(error);
                    }
                }
            });

            AddProgramCommand = new RelayCommand(() =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Add Program",
                    Filter = "Programs (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All Files (*.*)|*.*",
                    Multiselect = true
                };
                if (ofd.ShowDialog() == true)
                {
                    HandleDropFiles(ofd.FileNames);
                }
            });

            AddWebsiteCommand = new RelayCommand(() =>
            {
                WebsiteName = string.Empty;
                WebsiteUrl = "https://";
                WebsiteError = string.Empty;
                ModalTitle = "Add Website";
                CurrentDialog = DialogType.AddWebsite;
            });

            AddFolderCommand = new RelayCommand(() =>
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Select Folder to Add";
                    fbd.ShowNewFolderButton = false;
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        HandleDropFiles(new[] { fbd.SelectedPath });
                    }
                }
            });

            SubmitAddWebsiteCommand = new RelayCommand(() =>
            {
                string url = (WebsiteUrl ?? string.Empty).Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    WebsiteError = "URL must start with http:// or https://";
                    return;
                }

                string name = (WebsiteName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = url;
                }

                int nextOrder = AllItems.Where(i => i.TabId == SelectedTab.Id).Select(i => i.Order).DefaultIfEmpty(-1).Max() + 1;
                var model = new ProgramItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TabId = SelectedTab.Id,
                    Order = nextOrder,
                    Name = name,
                    Kind = "url",
                    Target = url,
                    Arguments = string.Empty,
                    WorkingDirectory = string.Empty,
                    Icon = new IconConfig { Source = "auto", Path = null, Index = 0 }
                };

                var vm = new ProgramItemViewModel(model, SelectedTab.Name);
                vm.IsEditMode = IsEditMode;
                AllItems.Add(vm);
                UpdateFilteredItems();
                SaveConfig();
                CloseModal();
            });

            SubmitEditDetailsCommand = new RelayCommand(() =>
            {
                if (EditDetailsItem == null) return;

                string newName = (EditItemName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    EditDetailsError = "Name cannot be empty.";
                    return;
                }

                EditDetailsItem.Name = newName;
                EditDetailsItem.Model.Target = PathHelper.CollapsePath(EditItemTarget);
                EditDetailsItem.Model.Arguments = (EditItemArgs ?? string.Empty).Trim();
                EditDetailsItem.Model.WorkingDirectory = PathHelper.CollapsePath(EditItemWorkDir);

                EditDetailsItem.CheckTargetExists();
                EditDetailsItem.LoadIcon(256);

                SaveConfig();
                CloseModal();
            });

            BrowseTargetCommand = new RelayCommand(() =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Target File",
                    Filter = "Programs (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All Files (*.*)|*.*"
                };
                if (ofd.ShowDialog() == true)
                {
                    EditItemTarget = ofd.FileName;
                    if (string.IsNullOrEmpty(EditItemWorkDir))
                    {
                        EditItemWorkDir = Path.GetDirectoryName(ofd.FileName);
                    }
                }
            });

            BrowseWorkDirCommand = new RelayCommand(() =>
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Select Working Directory";
                    fbd.ShowNewFolderButton = false;
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        EditItemWorkDir = fbd.SelectedPath;
                    }
                }
            });

            SubmitConfirmDeleteCommand = new RelayCommand(SubmitConfirmDelete);

            SelectSettingsPageCommand = new RelayCommand(param =>
            {
                if (param is int idx) SettingsPageIndex = idx;
                else if (param is string s && int.TryParse(s, out int parsed)) SettingsPageIndex = parsed;
            });

            BrowseLogoCommand = new RelayCommand(BrowseLogo);
            ClearLogoCommand = new RelayCommand(ClearLogo);

            SelectAccentColorCommand = new RelayCommand(param =>
            {
                if (param is string hex)
                {
                    SelectedAccentColor = hex;
                    if (_configService.Current?.Appearance != null)
                    {
                        _configService.Current.Appearance.Accent = hex;
                        ApplyAppearance(_configService.Current.Appearance);
                        SaveConfig();
                    }
                }
            });

            ApplyCustomColorsCommand = new RelayCommand(ApplyCustomColors);
            ResetAppearanceDefaultsCommand = new RelayCommand(ResetAppearanceDefaults);
            BrowseWallpaperCommand = new RelayCommand(BrowseWallpaper);
            ClearWallpaperCommand = new RelayCommand(ClearWallpaper);
            ResetFontScaleCommand = new RelayCommand(() => FontScale = 1.0);
            OpenGithubCommand = new RelayCommand(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/HanazonoArchive") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Warn($"Failed to open GitHub profile: {ex.Message}");
                }
            });

            MoveTabUpCommand = new RelayCommand(param => MoveTabUp(param as TabModel ?? SelectedSettingsTab));
            MoveTabDownCommand = new RelayCommand(param => MoveTabDown(param as TabModel ?? SelectedSettingsTab));
            AddTabCommand = new RelayCommand(AddTab);
            DeleteTabCommand = new RelayCommand(param => PromptDeleteTab(param as TabModel ?? SelectedSettingsTab));
            OpenTabIconPickerCommand = new RelayCommand(param => OpenTabIconPicker(param ?? SelectedSettingsTab));
            SelectTabIconCommand = new RelayCommand(param => SelectTabIcon(param as string));
            SubmitDeleteTabMoveCommand = new RelayCommand(SubmitDeleteTabMove);
            SubmitDeleteTabAllCommand = new RelayCommand(SubmitDeleteTabAll);

            ExportBackupCommand = new RelayCommand(ExportBackup);
            ImportBackupCommand = new RelayCommand(ImportBackup);
            OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
            OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

            _isLoadingConfig = true;
            try
            {
                LoadConfig();
                LoadTools();
                LoadItems();
            }
            finally
            {
                _isLoadingConfig = false;
            }
            StartSystemInfoMonitoring();
        }

        private void LoadTools()
        {
            TechnicalTools.Clear();
            foreach (var tool in _toolLaunchService.GetDefaultTools())
            {
                TechnicalTools.Add(tool);
            }
        }

        private void LoadConfig()
        {
            var config = _configService.Load();

            ShopName = config.Branding?.ShopName ?? "PisoNet";
            ShopLogoPath = config.Branding?.Logo;
            LoadShopLogo(ShopLogoPath);

            IsDarkMode = (config.Appearance?.Theme ?? "dark").Equals("dark", StringComparison.OrdinalIgnoreCase);
            IconSize = config.Appearance?.IconSize > 0 ? config.Appearance.IconSize : 96;

            StartWithWindows = _startupService.IsRunAtStartup();

            SelectedAccentColor = config.Appearance?.Accent ?? "#0284C7";
            SelectedFontFamily = config.Appearance?.FontFamily ?? "Segoe UI";
            FontScale = config.Appearance?.FontScale > 0 ? config.Appearance.FontScale : 1.0;

            if (config.Appearance?.CustomColors != null)
            {
                CustomBgColor = config.Appearance.CustomColors.Background;
                CustomSurfaceColor = config.Appearance.CustomColors.Surface;
                CustomTextColor = config.Appearance.CustomColors.Text;
                CustomAccentColor = config.Appearance.CustomColors.Accent ?? config.Appearance.Accent;
            }

            Tabs.Clear();
            if (config.Tabs != null && config.Tabs.Count > 0)
            {
                foreach (var tab in config.Tabs)
                {
                    Tabs.Add(tab);
                }
            }
            else
            {
                var defaultTabs = AppConfig.CreateDefault().Tabs;
                foreach (var tab in defaultTabs)
                {
                    Tabs.Add(tab);
                }
            }

            SelectedTab = Tabs.FirstOrDefault();
            SelectedSettingsTab = Tabs.FirstOrDefault();
            ProgramItemViewModel.AllTabs = Tabs;

            // Wallpaper
            WallpaperPath = config.Branding?.Wallpaper;
            LoadWallpaper(WallpaperPath);

            // Apply live appearance tokens
            ApplyAppearance(config.Appearance);

            CloseCommand = new RelayCommand(() => System.Windows.Application.Current.MainWindow?.Close());

            // First-time setup per user request: ask for new password immediately before everything else
            if (config.MustChangePassword)
            {
                ModalTitle = "First-Time Setup: Set Administrator Password";
                CurrentDialog = DialogType.ChangePassword;
            }
        }

        public void LoadItems()
        {
            AllItems.Clear();
            var config = _configService.Current;
            if (config?.Items != null)
            {
                foreach (var item in config.Items)
                {
                    string tabName = Tabs.FirstOrDefault(t => t.Id == item.TabId)?.Name ?? string.Empty;
                    var vm = new ProgramItemViewModel(item, tabName);
                    vm.IsEditMode = IsEditMode;
                    AllItems.Add(vm);
                }
            }

            UpdateFilteredItems();
        }

        public void UpdateFilteredItems()
        {
            FilteredItems.Clear();

            if (IsSearching)
            {
                string query = SearchQuery.Trim();
                var matches = AllItems
                    .Where(i => i.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(i => i.TabId)
                    .ThenBy(i => i.Order);

                foreach (var match in matches)
                {
                    FilteredItems.Add(match);
                }
            }
            else if (SelectedTab != null)
            {
                var tabItems = AllItems
                    .Where(i => i.TabId == SelectedTab.Id)
                    .OrderBy(i => i.Order);

                foreach (var item in tabItems)
                {
                    FilteredItems.Add(item);
                }
            }

            OnPropertyChanged(nameof(HasNoItems));
            OnPropertyChanged(nameof(EmptyStateMessage));
        }

        public void CheckAllShortcuts()
        {
            foreach (var item in AllItems)
            {
                item.CheckTargetExists();
            }
        }

        private void LoadWallpaper(string wallpaperPath)
        {
            if (string.IsNullOrWhiteSpace(wallpaperPath))
            {
                WallpaperSource = null;
                return;
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(wallpaperPath);
                if (!Path.IsPathRooted(expanded))
                {
                    expanded = Path.Combine(_configService.DataDirectory, expanded);
                }

                if (File.Exists(expanded))
                {
                    byte[] bytes = File.ReadAllBytes(expanded);
                    using (var ms = new MemoryStream(bytes))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;

                        // SPEC section 13: Decode wallpaper at screen size to conserve memory
                        int screenWidth = 1920;
                        try
                        {
                            screenWidth = Math.Max(1920, (int)SystemParameters.PrimaryScreenWidth);
                        }
                        catch { }
                        bmp.DecodePixelWidth = screenWidth;

                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        WallpaperSource = bmp;
                    }
                }
                else
                {
                    WallpaperSource = null;
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to load wallpaper: {ex.Message}");
                WallpaperSource = null;
            }
        }

        private void LoadShopLogo(string logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                ShopLogoSource = null;
                return;
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(logoPath);
                if (!Path.IsPathRooted(expanded))
                {
                    expanded = Path.Combine(_configService.DataDirectory, expanded);
                }

                if (File.Exists(expanded))
                {
                    byte[] bytes = File.ReadAllBytes(expanded);
                    using (var ms = new MemoryStream(bytes))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        ShopLogoSource = bmp;
                    }
                }
                else
                {
                    ShopLogoSource = null;
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to load shop logo: {ex.Message}");
                ShopLogoSource = null;
            }
        }

        #region Password & Unlock Flow

        public void RequestUnlock(Action onUnlocked)
        {
            if (IsSessionUnlocked)
            {
                onUnlocked?.Invoke();
                return;
            }

            _pendingUnlockAction = onUnlocked;
            PasswordError = string.Empty;
            ModalTitle = "Authentication Required";
            CurrentDialog = DialogType.Password;
            OnFocusPasswordInput?.Invoke();
        }

        public void ProcessPassword(string password)
        {
            var config = _configService.Current;
            string storedHash = config?.PasswordHash ?? PasswordService.DefaultPasswordHash;

            if (_passwordService.VerifyPassword(password, storedHash))
            {
                PasswordError = string.Empty;

                // SPEC section 8: Default password is admin. On first successful unlock the user must set a new password before continuing.
                bool isDefaultAdmin = string.Equals(_passwordService.ComputeHash(password), PasswordService.DefaultPasswordHash, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(_passwordService.ComputeHash(password.ToLowerInvariant()), PasswordService.DefaultPasswordHash, StringComparison.OrdinalIgnoreCase);
                bool mustChange = (config != null && config.MustChangePassword) || isDefaultAdmin;

                if (mustChange)
                {
                    NewPasswordError = string.Empty;
                    ModalTitle = "Change Password Required";
                    CurrentDialog = DialogType.ChangePassword;
                    OnFocusNewPasswordInput?.Invoke();
                }
                else
                {
                    IsSessionUnlocked = true;
                    var action = _pendingUnlockAction;
                    _pendingUnlockAction = null;
                    CloseModal();
                    action?.Invoke();
                }
            }
            else
            {
                PasswordError = "Incorrect password. Please try again.";
            }
        }

        public void ProcessNewPassword(string newPassword, string confirmPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                NewPasswordError = "Password cannot be empty.";
                return;
            }

            if (newPassword.Length < 4)
            {
                NewPasswordError = "Password must be at least 4 characters.";
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                NewPasswordError = "Passwords do not match.";
                return;
            }

            // Save new password hash atomically
            string newHash = _passwordService.ComputeHash(newPassword);
            if (_configService.Current != null)
            {
                _configService.Current.PasswordHash = newHash;
                _configService.Current.MustChangePassword = false;
                _configService.Save(_configService.Current);
                LoggerService.Instance.Info("Password successfully changed.");
                OnPropertyChanged(nameof(MustChangePassword));
            }

            IsSessionUnlocked = true;
            var action = _pendingUnlockAction;
            _pendingUnlockAction = null;
            CloseModal();
            action?.Invoke();
        }

        public void OpenSettings(int pageIndex)
        {
            SettingsPageIndex = Math.Max(0, Math.Min(5, pageIndex));
            ModalTitle = "Settings";
            CurrentDialog = DialogType.SettingsHost;
        }

        public void CloseModal()
        {
            if (_configService.Current != null && _configService.Current.MustChangePassword && CurrentDialog == DialogType.ChangePassword)
            {
                NewPasswordError = "You must set an administrator password before continuing.";
                return;
            }

            if (CurrentDialog == DialogType.IconPicker)
            {
                _iconPickerTarget = null;
                OpenSettings(2);
                return;
            }

            if (CurrentDialog == DialogType.TabDeletePrompt)
            {
                _pendingDeleteTab = null;
                OpenSettings(2);
                return;
            }

            CurrentDialog = DialogType.None;
            PasswordError = string.Empty;
            NewPasswordError = string.Empty;
            WebsiteError = string.Empty;
            EditDetailsError = string.Empty;
            _pendingDeleteItems = null;
            _pendingDeleteTab = null;
            _iconPickerTarget = null;
            _pendingUnlockAction = null;
        }
        #endregion

        #region Edit Mode Operations (Reorder, Move, Drop, Remove, Save)

        public void SaveConfig()
        {
            if (_isLoadingConfig) return;

            var config = _configService.Current ?? _configService.Load();
            if (config != null)
            {
                config.Items = AllItems.Select(i => i.Model).ToList();
                config.Tabs = Tabs.ToList();
                if (config.Branding != null)
                {
                    config.Branding.ShopName = ShopName;
                }
                _configService.Save(config);
            }
        }

        public void OpenEditDetailsDialog(ProgramItemViewModel item)
        {
            if (item == null) return;
            EditDetailsItem = item;
            EditItemName = item.Name;
            EditItemTarget = PathHelper.ExpandPath(item.Target);
            EditItemArgs = item.Arguments ?? string.Empty;
            EditItemWorkDir = PathHelper.ExpandPath(item.WorkingDirectory);
            EditDetailsError = string.Empty;

            ModalTitle = "Edit Details";
            CurrentDialog = DialogType.EditDetails;
        }

        public void PromptRemoveItems(IEnumerable<ProgramItemViewModel> items)
        {
            if (items == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            _pendingDeleteItems = list;
            if (list.Count == 1)
            {
                ConfirmDeleteMessage = $"Are you sure you want to remove '{list[0].Name}'?";
            }
            else
            {
                ConfirmDeleteMessage = $"Are you sure you want to remove these {list.Count} programs?";
            }

            ModalTitle = "Confirm Removal";
            CurrentDialog = DialogType.ConfirmDelete;
        }

        public void SubmitConfirmDelete()
        {
            if (_pendingDeleteItems != null && _pendingDeleteItems.Count > 0)
            {
                foreach (var item in _pendingDeleteItems)
                {
                    AllItems.Remove(item);
                    try
                    {
                        string iconPath = Path.Combine(_configService.AssetsDirectory, "icons", $"{item.Id}.png");
                        if (File.Exists(iconPath))
                        {
                            File.Delete(iconPath);
                        }
                    }
                    catch { }
                }

                _pendingDeleteItems = null;
                UpdateFilteredItems();
                SaveConfig();
            }

            CloseModal();
        }

        public void ReorderItem(ProgramItemViewModel source, int targetIndex)
        {
            if (source == null || SelectedTab == null) return;

            var currentTabItems = AllItems.Where(i => i.TabId == SelectedTab.Id).OrderBy(i => i.Order).ToList();
            int oldIndex = currentTabItems.IndexOf(source);
            if (oldIndex < 0) return;

            targetIndex = Math.Max(0, Math.Min(targetIndex, currentTabItems.Count - 1));
            if (oldIndex == targetIndex) return;

            currentTabItems.RemoveAt(oldIndex);
            currentTabItems.Insert(targetIndex, source);

            for (int i = 0; i < currentTabItems.Count; i++)
            {
                currentTabItems[i].Order = i;
            }

            UpdateFilteredItems();
            SaveConfig();
        }

        public void MoveItemsToTab(IEnumerable<ProgramItemViewModel> items, string targetTabId)
        {
            if (items == null || string.IsNullOrEmpty(targetTabId)) return;
            var targetTab = Tabs.FirstOrDefault(t => t.Id == targetTabId);
            if (targetTab == null) return;

            int nextOrder = AllItems.Where(i => i.TabId == targetTabId).Select(i => i.Order).DefaultIfEmpty(-1).Max() + 1;

            foreach (var item in items)
            {
                item.TabId = targetTabId;
                item.TabName = targetTab.Name;
                item.Order = nextOrder++;
            }

            UpdateFilteredItems();
            SaveConfig();
        }

        public void HandleDropFiles(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0 || SelectedTab == null) return;

            var invalidFiles = new List<string>();
            var addedItems = new List<ProgramItemViewModel>();

            foreach (var rawPath in filePaths)
            {
                string path = rawPath?.Trim();
                if (string.IsNullOrEmpty(path)) continue;

                bool isDir = Directory.Exists(path);
                bool isFile = File.Exists(path);

                if (!isDir && !isFile)
                {
                    invalidFiles.Add($"{path} (File or directory not found)");
                    continue;
                }

                string ext = isDir ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
                string kind;

                if (isDir)
                {
                    kind = "folder";
                }
                else if (ext == ".exe")
                {
                    kind = "exe";
                }
                else if (ext == ".lnk")
                {
                    kind = "lnk";
                }
                else if (ext == ".bat")
                {
                    kind = "bat";
                }
                else if (ext == ".cmd")
                {
                    kind = "cmd";
                }
                else if (ext == ".url")
                {
                    kind = "url";
                }
                else
                {
                    invalidFiles.Add($"{Path.GetFileName(path)} (Unsupported file type '{ext}')");
                    continue;
                }

                string id = Guid.NewGuid().ToString("N");
                string name;
                string target = PathHelper.CollapsePath(path);
                string workDir = isDir ? string.Empty : PathHelper.CollapsePath(Path.GetDirectoryName(path) ?? string.Empty);

                if (isDir)
                {
                    name = new DirectoryInfo(path).Name;
                }
                else if (kind == "url")
                {
                    name = Path.GetFileNameWithoutExtension(path);
                    try
                    {
                        var lines = File.ReadAllLines(path);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                            {
                                target = line.Substring(4).Trim();
                                break;
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    name = Path.GetFileNameWithoutExtension(path);
                }

                int nextOrder = AllItems.Where(i => i.TabId == SelectedTab.Id).Select(i => i.Order).DefaultIfEmpty(-1).Max() + 1;

                var model = new ProgramItem
                {
                    Id = id,
                    TabId = SelectedTab.Id,
                    Order = nextOrder,
                    Name = name,
                    Kind = kind,
                    Target = target,
                    Arguments = string.Empty,
                    WorkingDirectory = workDir,
                    Icon = new IconConfig { Source = "auto", Path = null, Index = 0 }
                };

                var vm = new ProgramItemViewModel(model, SelectedTab.Name);
                vm.IsEditMode = IsEditMode;
                AllItems.Add(vm);
                addedItems.Add(vm);
            }

            if (addedItems.Count > 0)
            {
                UpdateFilteredItems();
                SaveConfig();
            }

            if (invalidFiles.Count > 0)
            {
                string msg = "The following items could not be added:\n• " + string.Join("\n• ", invalidFiles) +
                             "\n\nSupported types: .exe, .lnk, .bat, .cmd, .url, and folders.";
                ShowMessage(msg);
            }
        }

        #endregion

        #region System Info & Theme

        private void StartSystemInfoMonitoring()
        {
            _systemInfoService.StartMonitoring(snapshot =>
            {
                if (Application.Current == null) return;

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SystemInfo.ComputerName = snapshot.ComputerName;
                    SystemInfo.CpuName = snapshot.CpuName;
                    SystemInfo.OsVersion = snapshot.OsVersion;
                    SystemInfo.CoresAndThreads = snapshot.CoresAndThreads;
                    SystemInfo.RamDisplay = snapshot.RamDisplay;
                    SystemInfo.NetworkName = snapshot.NetworkName;
                    SystemInfo.NetworkThroughput = snapshot.NetworkThroughput;

                    SystemInfo.Disks.Clear();
                    foreach (var disk in snapshot.Disks)
                    {
                        SystemInfo.Disks.Add(disk);
                    }

                    SystemInfo.Gpus.Clear();
                    foreach (var gpu in snapshot.Gpus)
                    {
                        SystemInfo.Gpus.Add(gpu);
                    }
                });
            });
        }

        public void StopMonitoring()
        {
            _systemInfoService.StopMonitoring();
        }

        private System.Threading.Timer _messageDismissTimer;

        public void ShowMessage(string message)
        {
            InAppMessage = message;
            _messageDismissTimer?.Dispose();
            if (!string.IsNullOrEmpty(message))
            {
                _messageDismissTimer = new System.Threading.Timer(_ =>
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        InAppMessage = null;
                    });
                }, null, 4000, System.Threading.Timeout.Infinite);
            }
        }

        private void ApplyTheme(bool darkMode)
        {
            string themeUri = darkMode ? "Resources/Themes/DarkTheme.xaml" : "Resources/Themes/LightTheme.xaml";
            var newTheme = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) };

            var appResources = Application.Current.Resources;
            ResourceDictionary oldTheme = null;
            foreach (var dict in appResources.MergedDictionaries)
            {
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("DarkTheme.xaml") ||
                     dict.Source.OriginalString.Contains("LightTheme.xaml")))
                {
                    oldTheme = dict;
                    break;
                }
            }

            if (oldTheme != null)
            {
                appResources.MergedDictionaries.Remove(oldTheme);
            }
            appResources.MergedDictionaries.Insert(0, newTheme);

            if (_configService.Current != null && _configService.Current.Appearance != null)
            {
                _configService.Current.Appearance.Theme = darkMode ? "dark" : "light";
                _configService.Save(_configService.Current);
            }

            // Re-apply accent and custom colors on top of newly merged theme dictionary
            ApplyAppearance(_configService.Current?.Appearance);
        }

        #endregion

        #region Phase 6 Settings Operations (Branding, Appearance, Tabs, Security, Backup, About)

        public void BrowseLogo()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Shop Logo",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string ext = Path.GetExtension(ofd.FileName);
                    string destDir = _configService.AssetsDirectory;
                    Directory.CreateDirectory(destDir);

                    try
                    {
                        foreach (var old in Directory.GetFiles(destDir, "logo.*"))
                        {
                            File.Delete(old);
                        }
                    }
                    catch { }

                    string destFile = Path.Combine(destDir, $"logo{ext}");
                    File.Copy(ofd.FileName, destFile, true);

                    ShopLogoPath = PathHelper.CollapsePath(destFile);
                    LoadShopLogo(destFile);

                    if (_configService.Current?.Branding != null)
                    {
                        _configService.Current.Branding.Logo = ShopLogoPath;
                        SaveConfig();
                    }
                }
                catch (Exception ex)
                {
                    ShowMessage($"Failed to set logo: {ex.Message}");
                }
            }
        }

        public void ClearLogo()
        {
            ShopLogoPath = null;
            ShopLogoSource = null;
            try
            {
                string destDir = _configService.AssetsDirectory;
                if (Directory.Exists(destDir))
                {
                    foreach (var old in Directory.GetFiles(destDir, "logo.*"))
                    {
                        File.Delete(old);
                    }
                }
            }
            catch { }

            if (_configService.Current?.Branding != null)
            {
                _configService.Current.Branding.Logo = null;
                SaveConfig();
            }
        }

        public void BrowseWallpaper()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Wallpaper Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string ext = Path.GetExtension(ofd.FileName);
                    string destDir = _configService.AssetsDirectory;
                    Directory.CreateDirectory(destDir);

                    try
                    {
                        foreach (var old in Directory.GetFiles(destDir, "wallpaper.*"))
                        {
                            File.Delete(old);
                        }
                    }
                    catch { }

                    string destFile = Path.Combine(destDir, $"wallpaper{ext}");
                    File.Copy(ofd.FileName, destFile, true);

                    WallpaperPath = PathHelper.CollapsePath(destFile);
                    LoadWallpaper(destFile);

                    if (_configService.Current?.Branding != null)
                    {
                        _configService.Current.Branding.Wallpaper = WallpaperPath;
                        SaveConfig();
                    }
                }
                catch (Exception ex)
                {
                    ShowMessage($"Failed to set wallpaper: {ex.Message}");
                }
            }
        }

        public void ClearWallpaper()
        {
            WallpaperPath = null;
            WallpaperSource = null;
            try
            {
                string destDir = _configService.AssetsDirectory;
                if (Directory.Exists(destDir))
                {
                    foreach (var old in Directory.GetFiles(destDir, "wallpaper.*"))
                    {
                        File.Delete(old);
                    }
                }
            }
            catch { }

            if (_configService.Current?.Branding != null)
            {
                _configService.Current.Branding.Wallpaper = null;
                SaveConfig();
            }
        }

        public void ApplyCustomColors()
        {
            var config = _configService.Current;
            if (config == null) return;

            if (config.Appearance.CustomColors == null)
            {
                config.Appearance.CustomColors = new CustomColorsConfig();
            }

            config.Appearance.CustomColors.Background = string.IsNullOrWhiteSpace(CustomBgColor) ? null : CustomBgColor.Trim();
            config.Appearance.CustomColors.Surface = string.IsNullOrWhiteSpace(CustomSurfaceColor) ? null : CustomSurfaceColor.Trim();
            config.Appearance.CustomColors.Text = string.IsNullOrWhiteSpace(CustomTextColor) ? null : CustomTextColor.Trim();
            config.Appearance.CustomColors.Accent = string.IsNullOrWhiteSpace(CustomAccentColor) ? null : CustomAccentColor.Trim();

            ApplyAppearance(config.Appearance);
            SaveConfig();
        }

        public void ResetAppearanceDefaults()
        {
            var config = _configService.Current;
            if (config == null) return;

            config.Appearance.CustomColors = null;
            config.Appearance.Accent = "#0284C7";
            SelectedAccentColor = "#0284C7";
            CustomBgColor = null;
            CustomSurfaceColor = null;
            CustomTextColor = null;
            CustomAccentColor = null;

            ApplyTheme(IsDarkMode);
            ApplyAppearance(config.Appearance);
            SaveConfig();
        }

        public void ApplyAppearance(AppearanceConfig appearance)
        {
            if (appearance == null) return;

            var res = Application.Current?.Resources;
            if (res == null) return;

            // 1. Accent color
            string accentHex = !string.IsNullOrEmpty(appearance.CustomColors?.Accent)
                ? appearance.CustomColors.Accent
                : (!string.IsNullOrEmpty(appearance.Accent) ? appearance.Accent : "#0284C7");

            try
            {
                if (ColorConverter.ConvertFromString(accentHex) is Color accentColor)
                {
                    var accentBrush = new SolidColorBrush(accentColor);
                    accentBrush.Freeze();
                    res["Brush.Accent"] = accentBrush;

                    byte r = (byte)Math.Max(0, accentColor.R - 25);
                    byte g = (byte)Math.Max(0, accentColor.G - 25);
                    byte b = (byte)Math.Max(0, accentColor.B - 25);
                    var hoverBrush = new SolidColorBrush(Color.FromArgb(accentColor.A, r, g, b));
                    hoverBrush.Freeze();
                    res["Brush.AccentHover"] = hoverBrush;
                }
            }
            catch { }

            // 2. Custom Background, Surface, Text
            if (appearance.CustomColors != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(appearance.CustomColors.Background) &&
                        ColorConverter.ConvertFromString(appearance.CustomColors.Background) is Color bgColor)
                    {
                        var bgBrush = new SolidColorBrush(bgColor);
                        bgBrush.Freeze();
                        res["Brush.Background"] = bgBrush;
                    }

                    if (!string.IsNullOrEmpty(appearance.CustomColors.Surface) &&
                        ColorConverter.ConvertFromString(appearance.CustomColors.Surface) is Color surfaceColor)
                    {
                        var surfaceBrush = new SolidColorBrush(surfaceColor);
                        surfaceBrush.Freeze();
                        res["Brush.Surface"] = surfaceBrush;

                        byte hr = (byte)Math.Min(255, surfaceColor.R + 15);
                        byte hg = (byte)Math.Min(255, surfaceColor.G + 15);
                        byte hb = (byte)Math.Min(255, surfaceColor.B + 15);
                        var hoverBrush = new SolidColorBrush(Color.FromArgb(surfaceColor.A, hr, hg, hb));
                        hoverBrush.Freeze();
                        res["Brush.SurfaceHover"] = hoverBrush;
                    }

                    if (!string.IsNullOrEmpty(appearance.CustomColors.Text) &&
                        ColorConverter.ConvertFromString(appearance.CustomColors.Text) is Color textColor)
                    {
                        var textBrush = new SolidColorBrush(textColor);
                        textBrush.Freeze();
                        res["Brush.TextPrimary"] = textBrush;
                    }
                }
                catch { }
            }

            // 3. Font Family
            string fontFamilyName = !string.IsNullOrEmpty(appearance.FontFamily) ? appearance.FontFamily : "Segoe UI";
            try
            {
                CurrentFontFamily = new FontFamily(fontFamilyName);
            }
            catch { }

            // 4. Font Scale
            double scale = appearance.FontScale > 0 ? appearance.FontScale : 1.0;
            _fontScale = scale;
            OnPropertyChanged(nameof(FontScale));
            OnPropertyChanged(nameof(FontScaleDisplay));
            OnPropertyChanged(nameof(RootFontSize));
        }

        public void MoveTabUp(TabModel tab)
        {
            if (tab == null) return;
            int idx = Tabs.IndexOf(tab);
            if (idx > 0)
            {
                Tabs.Move(idx, idx - 1);
                SelectedSettingsTab = tab;
                SaveConfig();
            }
        }

        public void MoveTabDown(TabModel tab)
        {
            if (tab == null) return;
            int idx = Tabs.IndexOf(tab);
            if (idx >= 0 && idx < Tabs.Count - 1)
            {
                Tabs.Move(idx, idx + 1);
                SelectedSettingsTab = tab;
                SaveConfig();
            }
        }

        public void AddTab()
        {
            string name = (AddTabName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return;

            var newTab = new TabModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Icon = string.IsNullOrEmpty(AddTabIcon) ? "folder" : AddTabIcon
            };

            Tabs.Add(newTab);
            SelectedSettingsTab = newTab;
            AddTabName = string.Empty;
            AddTabIcon = "folder";
            ProgramItemViewModel.AllTabs = Tabs;
            SaveConfig();
        }

        public void PromptDeleteTab(TabModel tab)
        {
            if (tab == null) return;

            if (Tabs.Count <= 1)
            {
                ShowMessage("Cannot delete the last remaining tab. At least one tab is required.");
                return;
            }

            int count = AllItems.Count(i => i.TabId == tab.Id);
            if (count == 0)
            {
                Tabs.Remove(tab);
                if (SelectedTab == tab)
                {
                    SelectedTab = Tabs.FirstOrDefault();
                }
                SelectedSettingsTab = Tabs.FirstOrDefault();
                ProgramItemViewModel.AllTabs = Tabs;
                SaveConfig();
                return;
            }

            _pendingDeleteTab = tab;
            PendingDeleteTabItemCount = count;
            PendingDeleteTabName = tab.Name;

            AvailableMoveTargetTabs.Clear();
            foreach (var t in Tabs)
            {
                if (t.Id != tab.Id)
                {
                    AvailableMoveTargetTabs.Add(t);
                }
            }
            SelectedMoveTargetTab = AvailableMoveTargetTabs.FirstOrDefault();

            ModalTitle = "Delete Tab";
            CurrentDialog = DialogType.TabDeletePrompt;
        }

        public void SubmitDeleteTabMove()
        {
            if (_pendingDeleteTab == null || SelectedMoveTargetTab == null) return;

            var itemsToMove = AllItems.Where(i => i.TabId == _pendingDeleteTab.Id).ToList();
            MoveItemsToTab(itemsToMove, SelectedMoveTargetTab.Id);

            Tabs.Remove(_pendingDeleteTab);
            if (SelectedTab == _pendingDeleteTab)
            {
                SelectedTab = SelectedMoveTargetTab;
            }
            SelectedSettingsTab = SelectedMoveTargetTab;
            ProgramItemViewModel.AllTabs = Tabs;

            _pendingDeleteTab = null;
            SaveConfig();
            OpenSettings(2);
        }

        public void SubmitDeleteTabAll()
        {
            if (_pendingDeleteTab == null) return;

            var itemsToDelete = AllItems.Where(i => i.TabId == _pendingDeleteTab.Id).ToList();
            foreach (var item in itemsToDelete)
            {
                AllItems.Remove(item);
                try
                {
                    string iconPath = Path.Combine(_configService.AssetsDirectory, "icons", $"{item.Id}.png");
                    if (File.Exists(iconPath))
                    {
                        File.Delete(iconPath);
                    }
                }
                catch { }
            }

            Tabs.Remove(_pendingDeleteTab);
            if (SelectedTab == _pendingDeleteTab)
            {
                SelectedTab = Tabs.FirstOrDefault();
            }
            SelectedSettingsTab = Tabs.FirstOrDefault();
            ProgramItemViewModel.AllTabs = Tabs;

            _pendingDeleteTab = null;
            UpdateFilteredItems();
            SaveConfig();
            OpenSettings(2);
        }

        public void OpenTabIconPicker(object target)
        {
            _iconPickerTarget = target;
            ModalTitle = "Choose Tab Icon";
            CurrentDialog = DialogType.IconPicker;
        }

        public void SelectTabIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return;

            if (_iconPickerTarget is TabModel tab)
            {
                tab.Icon = iconName;
                SaveConfig();
            }
            else if (_iconPickerTarget is string s && s == "addTab")
            {
                AddTabIcon = iconName;
            }

            _iconPickerTarget = null;
            OpenSettings(2);
        }

        public void ProcessSecurityPasswordChange(string newPassword, string confirmPassword)
        {
            SecurityError = null;
            SecuritySuccess = null;

            if (string.IsNullOrEmpty(newPassword))
            {
                SecurityError = "New password cannot be empty.";
                return;
            }

            if (newPassword.Length < 4)
            {
                SecurityError = "New password must be at least 4 characters.";
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                SecurityError = "New passwords do not match.";
                return;
            }

            var config = _configService.Current;
            string newHash = _passwordService.ComputeHash(newPassword);
            if (config != null)
            {
                config.PasswordHash = newHash;
                config.MustChangePassword = false;
                _configService.Save(config);
                LoggerService.Instance.Info("Administrator password updated via Settings.");
            }

            SecuritySuccess = "Password successfully updated.";
        }

        public void ExportBackup()
        {
            BackupStatusMessage = null;
            BackupErrorMessage = null;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Configuration Backup",
                FileName = $"ChromaticMenu_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                Filter = "ZIP Archive (*.zip)|*.zip"
            };

            if (sfd.ShowDialog() == true)
            {
                if (_exportImportService.ExportBackup(sfd.FileName, out string error))
                {
                    BackupStatusMessage = $"Configuration exported successfully to:\n{sfd.FileName}";
                }
                else
                {
                    BackupErrorMessage = error;
                }
            }
        }

        public void ImportBackup()
        {
            BackupStatusMessage = null;
            BackupErrorMessage = null;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Configuration Backup",
                Filter = "ZIP Archive (*.zip)|*.zip"
            };

            if (ofd.ShowDialog() == true)
            {
                if (_exportImportService.ImportBackup(ofd.FileName, out int brokenCount, out string error))
                {
                    LoadConfig();
                    LoadItems();
                    BackupStatusMessage = $"Backup imported successfully!\n{brokenCount} program(s) have broken paths on this computer.";
                }
                else
                {
                    BackupErrorMessage = error;
                }
            }
        }

        public void OpenConfigFolder()
        {
            try
            {
                string dir = _configService.DataDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
            catch (Exception ex)
            {
                ShowMessage($"Failed to open folder: {ex.Message}");
            }
        }

        public void OpenLogFolder()
        {
            try
            {
                string dir = Path.Combine(_configService.DataDirectory, "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
            catch (Exception ex)
            {
                ShowMessage($"Failed to open folder: {ex.Message}");
            }
        }

        #endregion
    }
}
