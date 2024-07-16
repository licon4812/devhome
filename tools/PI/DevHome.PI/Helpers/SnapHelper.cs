﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using DevHome.Common.Extensions;
using DevHome.PI.Models;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace DevHome.PI.Helpers;

public class SnapHelper
{
    // TODO The SnapOffsetHorizontal and UnsnapGap values don't allow for different DPIs.
    private const int UnsnapGap = 9;

    // It seems the way rounded corners are implemented means that the window is really 8px
    // bigger than it seems, so we'll subtract this when we do sidecar snapping.
    private const int SnapOffsetHorizontal = 8;

    private readonly WINEVENTPROC _winPositionEventDelegate;
    private readonly WINEVENTPROC _winFocusEventDelegate;

    private HWINEVENTHOOK _positionEventHook;
    private HWINEVENTHOOK _focusEventHook;

    public SnapHelper()
    {
        _winPositionEventDelegate = new(WinPositionEventProc);
        _winFocusEventDelegate = new(WinFocusEventProc);
    }

    public void Snap()
    {
        Debug.Assert(_positionEventHook == HWINEVENTHOOK.Null, "Hook should be null");
        Debug.Assert(_focusEventHook == HWINEVENTHOOK.Null, "Hook should be null");

        _positionEventHook = WindowHelper.WatchWindowPositionEvents(_winPositionEventDelegate, (uint)TargetAppData.Instance.ProcessId);
        _focusEventHook = WindowHelper.WatchWindowFocusEvents(_winFocusEventDelegate, (uint)TargetAppData.Instance.ProcessId);

        SnapToWindow();
    }

    public void Unsnap()
    {
        var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
        Debug.Assert(barWindow != null, "BarWindow should not be null.");

        // Set a gap from the associated app window to provide positive feedback.
        PInvoke.GetWindowRect(barWindow.CurrentHwnd, out var rect);
        barWindow.UpdateBarWindowPosition(new PointInt32(rect.left + UnsnapGap, rect.top));

        if (_positionEventHook != HWINEVENTHOOK.Null)
        {
            PInvoke.UnhookWinEvent(_positionEventHook);
            _positionEventHook = HWINEVENTHOOK.Null;
        }

        if (_focusEventHook != HWINEVENTHOOK.Null)
        {
            PInvoke.UnhookWinEvent(_focusEventHook);
            _focusEventHook = HWINEVENTHOOK.Null;
        }
    }

    private void WinPositionEventProc(HWINEVENTHOOK hWinEventHook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Filter out events for non-main windows.
        if (idObject != 0 || idChild != 0)
        {
            return;
        }

        if (hwnd != TargetAppData.Instance.HWnd)
        {
            return;
        }

        if (eventType == PInvoke.EVENT_OBJECT_LOCATIONCHANGE)
        {
            var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
            Debug.Assert(barWindow != null, "BarWindow should not be null.");
            if (barWindow.IsBarSnappedToWindow())
            {
                // If the window has been maximized, un-snap the bar window and free-float it.
                if (PInvoke.IsZoomed(TargetAppData.Instance.HWnd))
                {
                    barWindow.UnsnapBarWindow();
                }
                else
                {
                    // Reposition the window to match the moved/resized/minimized/restored target window.
                    // If the target window was maximized and has now been restored, we want
                    // to resnap to it, but not do all the other work we do when we resnap
                    // to a new window.
                    SnapToWindow();
                }
            }
        }

        // If the window we're watching closes, we unsnap
        if (eventType == PInvoke.EVENT_OBJECT_DESTROY)
        {
            Unsnap();
        }
    }

    private void WinFocusEventProc(HWINEVENTHOOK hWinEventHook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd != TargetAppData.Instance.HWnd)
        {
            return;
        }

        // If we're snapped to a target window, and that window loses and then regains focus,
        // we need to bring our window to the front also, to be in-sync. Otherwise, we can
        // end up with the target in the foreground, but our window partially obscured.
        var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
        Debug.Assert(barWindow != null, "BarWindow should not be null.");
        if (barWindow.IsBarSnappedToWindow())
        {
            barWindow.ResetBarWindowOnTop();
            return;
        }
    }

    private void SnapToWindow()
    {
        var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
        Debug.Assert(barWindow != null, "BarWindow should not be null.");

        // If BarWindow is snapped to a TargetApp and BarWindow is in foreground, bring TargetApp to foreground.
        if (barWindow.CurrentHwnd == PInvoke.GetForegroundWindow())
        {
            PInvoke.SetForegroundWindow(TargetAppData.Instance.HWnd);
        }

        PInvoke.GetWindowRect(TargetAppData.Instance.HWnd, out var rect);

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;

        barWindow.UpdateBarWindowPosition(new PointInt32(rect.right - SnapOffsetHorizontal, rect.top));
        barWindow.UpdateBarWindowSize(new SizeInt32(width, height));

        // Only reset BarWindow on top, if TargetApp is in foreground.
        if (TargetAppData.Instance.HWnd == PInvoke.GetForegroundWindow())
        {
            barWindow.ResetBarWindowOnTop();
        }
    }
}
