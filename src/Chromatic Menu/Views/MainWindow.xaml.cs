using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ChromaticMenu.Controls;
using ChromaticMenu.Models;
using ChromaticMenu.Native;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Views
{
    public partial class MainWindow : Window
    {
        private uint _activateMessageId;
        private HwndSource _hwndSource;
        private readonly MainViewModel _viewModel;

        // Drag-and-drop state
        private Point _dragStartPoint;
        private bool _isDragging;
        private DragAdorner _dragAdorner;
        private InsertionMarkerAdorner _insertionMarkerAdorner;
        private AdornerLayer _adornerLayer;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _viewModel.OnFocusPasswordInput = FocusPasswordBox;
            _viewModel.OnFocusNewPasswordInput = FocusNewPasswordBox;
            MainViewModel.GetSelectedItems = () => ProgramGridListBox.SelectedItems.OfType<ProgramItemViewModel>().ToList();

            PreviewKeyDown += MainWindow_PreviewKeyDown;
            SourceInitialized += MainWindow_SourceInitialized;
            Activated += MainWindow_Activated;
            Closing += MainWindow_Closing;

            Loaded += (s, e) =>
            {
                if (_viewModel.IsChangePasswordDialogVisible)
                {
                    FocusNewPasswordBox();
                }
            };
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            // SPEC section 9: Check that the target exists on window activation
            _viewModel?.CheckAllShortcuts();
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);

            _activateMessageId = NativeMethods.RegisterWindowMessage("CHROMATIC_MENU_ACTIVATE");
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Ensure any unsaved state is persisted before closing
            _viewModel?.SaveConfig();
            // Do NOT cancel closing per SPEC section 5. Clean up background timers.
            _viewModel?.StopMonitoring();
        }

        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
                return IntPtr.Zero;
            }

            if (_activateMessageId != 0 && msg == _activateMessageId)
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Maximized;
                }

                Show();
                Activate();
                NativeMethods.SetForegroundWindow(hwnd);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new NativeMethods.MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var rcWork = monitorInfo.rcWork;
                    var rcMonitor = monitorInfo.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(rcWork.Left - rcMonitor.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWork.Top - rcMonitor.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWork.Right - rcWork.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWork.Bottom - rcWork.Top);
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // SPEC section 5: Alt+F4 only minimizes
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
                WindowState = WindowState.Minimized;
                return;
            }

            // Modal dialog keyboard navigation (Esc to cancel, Enter to submit)
            if (_viewModel.IsModalOpen)
            {
                if (e.Key == Key.Escape)
                {
                    // If mandatory first-time password change is required, do not allow cancelling with Esc
                    if (_viewModel.MustChangePassword && _viewModel.IsChangePasswordDialogVisible)
                    {
                        e.Handled = true;
                        return;
                    }

                    e.Handled = true;
                    _viewModel.CloseModal();
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (_viewModel.IsPasswordDialogVisible)
                    {
                        SubmitPassword_Click(sender, e);
                    }
                    else if (_viewModel.IsChangePasswordDialogVisible)
                    {
                        SubmitNewPassword_Click(sender, e);
                    }
                    else if (_viewModel.IsAddWebsiteDialogVisible)
                    {
                        _viewModel.SubmitAddWebsiteCommand.Execute(null);
                    }
                    else if (_viewModel.IsEditDetailsDialogVisible)
                    {
                        _viewModel.SubmitEditDetailsCommand.Execute(null);
                    }
                    else if (_viewModel.IsConfirmDeleteDialogVisible)
                    {
                        _viewModel.SubmitConfirmDeleteCommand.Execute(null);
                    }
                    else if (_viewModel.IsMessageDialogVisible)
                    {
                        _viewModel.CloseModal();
                    }
                }
                return;
            }

            // F2 for inline rename in Edit Mode per SPEC section 8
            if (_viewModel.IsEditMode && e.Key == Key.F2)
            {
                if (ProgramGridListBox.SelectedItem is ProgramItemViewModel vm)
                {
                    e.Handled = true;
                    vm.StartRenameCommand.Execute(null);
                    return;
                }
            }

            // Delete key to remove items with confirmation in Edit Mode per SPEC section 8
            if (_viewModel.IsEditMode && e.Key == Key.Delete)
            {
                var selected = ProgramGridListBox.SelectedItems.OfType<ProgramItemViewModel>().ToList();
                if (selected.Count > 0)
                {
                    e.Handled = true;
                    _viewModel.PromptRemoveItems(selected);
                    return;
                }
            }

            // SPEC section 8 & 10: Clear search on Esc
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(_viewModel.SearchQuery))
            {
                e.Handled = true;
                _viewModel.SearchQuery = string.Empty;
                return;
            }
        }

        #region Drag and Drop (Reordering & Tab Move & Explorer Drop)

        private void ProgramGridListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(ProgramGridListBox);

            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item == null)
            {
                // Deselect when clicking on empty space in the ListBox
                ProgramGridListBox.SelectedItem = null;
                ProgramGridListBox.SelectedItems.Clear();
            }
            else if (item.DataContext is ProgramItemViewModel vm)
            {
                if (!_viewModel.IsEditMode)
                {
                    ProgramGridListBox.SelectedItem = vm;
                    if (e.ClickCount == 2)
                    {
                        _viewModel.LaunchItemCommand.Execute(vm);
                    }
                }
            }
        }

        private void BodyGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item == null)
            {
                ProgramGridListBox.SelectedItem = null;
                ProgramGridListBox.SelectedItems.Clear();
            }
        }

        private void ProgramGridListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !_viewModel.IsEditMode || _isDragging)
                return;

            Point currentPoint = e.GetPosition(ProgramGridListBox);
            Vector diff = _dragStartPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (item != null && item.DataContext is ProgramItemViewModel vm)
                {
                    StartDrag(item, vm);
                }
            }
        }

        private void StartDrag(ListBoxItem item, ProgramItemViewModel vm)
        {
            _isDragging = true;
            _adornerLayer = AdornerLayer.GetAdornerLayer(ProgramGridListBox);

            if (_adornerLayer != null)
            {
                _dragAdorner = new DragAdorner(ProgramGridListBox, item);
                _insertionMarkerAdorner = new InsertionMarkerAdorner(ProgramGridListBox);
                _adornerLayer.Add(_dragAdorner);
                _adornerLayer.Add(_insertionMarkerAdorner);
            }

            var selectedItems = ProgramGridListBox.SelectedItems.OfType<ProgramItemViewModel>().ToList();
            if (!selectedItems.Contains(vm))
            {
                selectedItems = new List<ProgramItemViewModel> { vm };
            }

            var data = new DataObject("ChromaticItems", selectedItems);
            data.SetData("SourceItem", vm);

            try
            {
                DragDrop.DoDragDrop(item, data, DragDropEffects.Move);
            }
            finally
            {
                CleanupDrag();
            }
        }

        private void CleanupDrag()
        {
            if (_adornerLayer != null)
            {
                if (_dragAdorner != null)
                {
                    _adornerLayer.Remove(_dragAdorner);
                    _dragAdorner = null;
                }
                if (_insertionMarkerAdorner != null)
                {
                    _adornerLayer.Remove(_insertionMarkerAdorner);
                    _insertionMarkerAdorner = null;
                }
                _adornerLayer = null;
            }
            _isDragging = false;
        }

        private void ProgramGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent("ChromaticItems"))
            {
                Point pos = e.GetPosition(ProgramGridListBox);
                _dragAdorner?.UpdatePosition(pos);

                var targetItem = FindItemAtPosition(pos);
                if (targetItem != null && targetItem.DataContext is ProgramItemViewModel)
                {
                    Point itemPos = targetItem.TranslatePoint(new Point(0, 0), ProgramGridListBox);
                    Rect targetRect = new Rect(itemPos, targetItem.RenderSize);
                    bool isAfter = (pos.X > itemPos.X + targetItem.ActualWidth / 2);

                    _insertionMarkerAdorner?.UpdateMarker(targetRect, isAfter);
                }
                else
                {
                    _insertionMarkerAdorner?.ClearMarker();
                }

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void ProgramGrid_Drop(object sender, DragEventArgs e)
        {
            CleanupDrag();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                _viewModel.HandleDropFiles(files);
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent("ChromaticItems"))
            {
                var sourceItem = e.Data.GetData("SourceItem") as ProgramItemViewModel;
                if (sourceItem != null)
                {
                    Point pos = e.GetPosition(ProgramGridListBox);
                    var targetItem = FindItemAtPosition(pos);
                    if (targetItem != null && targetItem.DataContext is ProgramItemViewModel targetVm)
                    {
                        int targetIndex = _viewModel.FilteredItems.IndexOf(targetVm);
                        if (targetIndex >= 0)
                        {
                            Point itemPos = targetItem.TranslatePoint(new Point(0, 0), ProgramGridListBox);
                            bool isAfter = (pos.X > itemPos.X + targetItem.ActualWidth / 2);
                            if (isAfter)
                            {
                                targetIndex++;
                            }
                            _viewModel.ReorderItem(sourceItem, targetIndex);
                        }
                    }
                }
                e.Handled = true;
            }
        }

        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ChromaticItems"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            CleanupDrag();

            if (e.Data.GetDataPresent("ChromaticItems"))
            {
                var items = e.Data.GetData("ChromaticItems") as List<ProgramItemViewModel>;
                var frameworkElement = sender as FrameworkElement;
                var targetTab = frameworkElement?.DataContext as TabModel;

                if (items != null && targetTab != null)
                {
                    _viewModel.MoveItemsToTab(items, targetTab.Id);
                    _viewModel.SelectedTab = targetTab;
                    e.Handled = true;
                }
            }
        }

        private ListBoxItem FindItemAtPosition(Point point)
        {
            var hitTestResult = VisualTreeHelper.HitTest(ProgramGridListBox, point);
            if (hitTestResult?.VisualHit != null)
            {
                return FindAncestor<ListBoxItem>(hitTestResult.VisualHit);
            }
            return null;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        #endregion

        #region Inline Rename Events

        private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ProgramItemViewModel vm)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    vm.CommitRenameCommand.Execute(null);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    vm.CancelRenameCommand.Execute(null);
                }
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ProgramItemViewModel vm)
            {
                if (vm.IsEditingName)
                {
                    vm.CommitRenameCommand.Execute(null);
                }
            }
        }

        #endregion

        private void ProgramGridListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // If the user right-clicked on a card item, allow its ContextMenu to open
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null)
            {
                return;
            }

            // Only allow background context menu in Edit Mode
            if (!_viewModel.IsEditMode)
            {
                e.Handled = true;
            }
        }

        private void ProgramCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Context menu is allowed in both Edit Mode and Non-Edit Mode
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.DataContext is ProgramItemViewModel vm)
            {
                ProgramGridListBox.SelectedItem = vm;
            }
        }

        private void ProgramCard_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Select the right-clicked card item so it highlights and sets context menu target
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.DataContext is ProgramItemViewModel vm)
            {
                ProgramGridListBox.SelectedItem = vm;
            }
        }

        private void ProgramGridListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Clicks in non-edit mode select/highlight, double-click or Enter launches
        }

        private void ProgramGridListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_viewModel.IsEditMode)
            {
                if (ProgramGridListBox.SelectedItem is ProgramItemViewModel vm)
                {
                    e.Handled = true;
                    _viewModel.LaunchItemCommand.Execute(vm);
                }
            }
        }

        private void FocusPasswordBox()
        {
            Dispatcher.InvokeAsync(() =>
            {
                ModalPasswordBox.Password = string.Empty;
                ModalPasswordBox.Focus();
            }, DispatcherPriority.Input);
        }

        private void FocusNewPasswordBox()
        {
            Dispatcher.InvokeAsync(() =>
            {
                NewPasswordBox.Password = string.Empty;
                ConfirmPasswordBox.Password = string.Empty;
                NewPasswordBox.Focus();
            }, DispatcherPriority.Input);
        }

        private void SubmitPassword_Click(object sender, RoutedEventArgs e)
        {
            string password = ModalPasswordBox.Password;
            _viewModel.ProcessPassword(password);

            // Clear box if successfully unlocked
            if (!_viewModel.IsPasswordDialogVisible)
            {
                ModalPasswordBox.Password = string.Empty;
            }
        }

        private void SubmitNewPassword_Click(object sender, RoutedEventArgs e)
        {
            string newPass = NewPasswordBox.Password;
            string confirmPass = ConfirmPasswordBox.Password;
            _viewModel.ProcessNewPassword(newPass, confirmPass);

            if (!_viewModel.IsChangePasswordDialogVisible)
            {
                NewPasswordBox.Password = string.Empty;
                ConfirmPasswordBox.Password = string.Empty;
            }
        }

        private void SubmitSecurityPassword_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ProcessSecurityPasswordChange(
                SecNewPasswordBox.Password,
                SecConfirmPasswordBox.Password);

            if (string.IsNullOrEmpty(_viewModel?.SecurityError))
            {
                SecNewPasswordBox.Password = string.Empty;
                SecConfirmPasswordBox.Password = string.Empty;
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
