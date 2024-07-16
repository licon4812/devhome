// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Linq;
using DevHome.Common.Extensions;
using DevHome.PI.Helpers;
using DevHome.PI.Models;
using DevHome.PI.Properties;
using DevHome.PI.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.UI.WindowManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;
using WinUIEx;

using static DevHome.PI.Helpers.CommonHelper;
using static DevHome.PI.Helpers.WindowHelper;

namespace DevHome.PI;

public partial class BarWindowVertical : WindowEx
{
    private readonly Settings _settings = Settings.Default;
    private readonly BarWindowViewModel _viewModel;

    private int _cursorPosX; // = 0;
    private int _cursorPosY; // = 0;
    private int _appWindowPosX; // = 0;
    private int _appWindowPosY; // = 0;
    private bool _isWindowMoving; // = false;
    private bool _isClosing;

    private double _dpiScale = 1.0;

    internal HWND ThisHwnd { get; private set; }

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    public BarWindowVertical(BarWindowViewModel model)
    {
        _viewModel = model;

        // The main constructor is used in all cases, including when there's no target window.
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        InitializeComponent();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void MainPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ThisHwnd = (HWND)WindowNative.GetWindowHandle(this);

        // Apply the user's chosen theme setting.
        ThemeName t = ThemeName.Themes.First(t => t.Name == _settings.CurrentTheme);
        SetRequestedTheme(t.Theme);

        RemoveThickFrameAttribute();

        // Regardless of what is set in the XAML, our initial window width is too big. Setting this to 70 (same as the XAML file)
        Width = 70;

        // Calculate the DPI scale.
        _dpiScale = GetDpiScaleForWindow(ThisHwnd);

        SetDefaultPosition();
    }

    private void SetDefaultPosition()
    {
        // If attached to an app it should show up on the monitor that the app is on
        var monitorRect = GetMonitorRectForWindow(_viewModel.ApplicationHwnd ?? TryGetParentProcessHWND() ?? ThisHwnd);
        this.Move(
            monitorRect.left + (int)(70 * _dpiScale),
            monitorRect.top + (int)(70 * _dpiScale));
    }

    private void RemoveThickFrameAttribute()
    {
        // This is a workaround for this issue
        // https://github.com/microsoft/microsoft-ui-xaml/issues/8947
        //
        // This prevents a white strip to be at the top of our window (visible in dark mode). Unfortunately this workaround removes the rounded corners from the bar.
        var style = PInvoke.GetWindowLong(ThisHwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~(int)WINDOW_STYLE.WS_THICKFRAME;
        _ = PInvoke.SetWindowLong(ThisHwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);
        PInvoke.SetWindowPos(ThisHwnd, HWND.Null, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(BarWindowViewModel.WindowPosition), StringComparison.OrdinalIgnoreCase))
        {
            this.Move(_viewModel.WindowPosition.X, _viewModel.WindowPosition.Y);
        }
        else if (string.Equals(e.PropertyName, nameof(BarWindowViewModel.RequestedWindowSize), StringComparison.OrdinalIgnoreCase))
        {
            Height = _viewModel.RequestedWindowSize.Height - 6; // 6 is a magic number that we lose due to removing the Thick Frame attribute
        }
    }

    private void WindowEx_Closed(object sender, WindowEventArgs args)
    {
        if (!_isClosing)
        {
            _isClosing = true;
            var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
            barWindow?.Close();
            _isClosing = false;
        }
    }

    internal void UpdatePositionFromHwnd(HWND hwnd)
    {
        RECT rect;
        PInvoke.GetWindowRect(hwnd, out rect);
        this.Move(rect.left, rect.top);
    }

    internal void SetRequestedTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var primaryWindow = Application.Current.GetService<PrimaryWindow>();
        Close();
        primaryWindow.ClearBarWindow();
    }

    private void Window_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).ReleasePointerCaptures();
        _isWindowMoving = false;

        // If we're occupying the same space as the target window, and we're not in medium/large mode, snap to the app
        if (!_viewModel.IsSnapped && TargetAppData.Instance.HWnd != HWND.Null)
        {
            if (DoesWindow1CoverTheRightSideOfWindow2(ThisHwnd, TargetAppData.Instance.HWnd))
            {
                _viewModel.SnapBarWindow();
            }
        }
    }

    private void Window_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint((UIElement)sender).Properties;
        if (properties.IsLeftButtonPressed)
        {
            // Moving the window causes it to unsnap
            if (_viewModel.IsSnapped)
            {
                _viewModel.UnsnapBarWindow();
            }

            _isWindowMoving = true;
            ((UIElement)sender).CapturePointer(e.Pointer);
            _appWindowPosX = AppWindow.Position.X;
            _appWindowPosY = AppWindow.Position.Y;
            PInvoke.GetCursorPos(out var pt);
            _cursorPosX = pt.X;
            _cursorPosY = pt.Y;
        }
    }

    private void Window_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isWindowMoving)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed)
            {
                PInvoke.GetCursorPos(out var pt);
                AppWindow.Move(new Windows.Graphics.PointInt32(
                    _appWindowPosX + (pt.X - _cursorPosX), _appWindowPosY + (pt.Y - _cursorPosY)));
            }

            e.Handled = true;
        }
    }
}
