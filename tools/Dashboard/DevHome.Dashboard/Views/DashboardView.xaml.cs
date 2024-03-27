// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DevHome.Common;
using DevHome.Common.Contracts;
using DevHome.Common.Extensions;
using DevHome.Common.Helpers;
using DevHome.Common.Services;
using DevHome.Dashboard.Controls;
using DevHome.Dashboard.Helpers;
using DevHome.Dashboard.Services;
using DevHome.Dashboard.TelemetryEvents;
using DevHome.Dashboard.ViewModels;
using DevHome.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Hosts;
using Windows.System;
using WinUIEx;
using Log = DevHome.Dashboard.Helpers.Log;

namespace DevHome.Dashboard.Views;

public partial class DashboardView : ToolPage, IDisposable
{
    public override string ShortName => "Dashboard";

    public DashboardViewModel ViewModel { get; }

    internal DashboardBannerViewModel BannerViewModel { get; }

    private readonly WidgetViewModelFactory _widgetViewModelFactory;

    public static ObservableCollection<WidgetViewModel> PinnedWidgets { get; set; }

    private readonly SemaphoreSlim _pinnedWidgetsLock = new(1, 1);

    private static Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly ILocalSettingsService _localSettingsService;
    private bool _disposedValue;

    private const string DraggedWidget = "DraggedWidget";
    private const string DraggedIndex = "DraggedIndex";

    public DashboardView()
    {
        ViewModel = Application.Current.GetService<DashboardViewModel>();
        BannerViewModel = Application.Current.GetService<DashboardBannerViewModel>();
        _widgetViewModelFactory = Application.Current.GetService<WidgetViewModelFactory>();

        this.InitializeComponent();

        PinnedWidgets = new ObservableCollection<WidgetViewModel>();
        PinnedWidgets.CollectionChanged += OnPinnedWidgetsCollectionChangedAsync;

        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _localSettingsService = Application.Current.GetService<ILocalSettingsService>();

#if DEBUG
        Loaded += AddResetButton;
#endif
    }

    private async Task<bool> SubscribeToWidgetCatalogEventsAsync()
    {
        Log.Logger()?.ReportInfo("DashboardView", "SubscribeToWidgetCatalogEvents");

        try
        {
            var widgetCatalog = await ViewModel.WidgetHostingService.GetWidgetCatalogAsync();
            if (widgetCatalog == null)
            {
                return false;
            }

            widgetCatalog!.WidgetProviderDefinitionAdded += WidgetCatalog_WidgetProviderDefinitionAdded;
            widgetCatalog!.WidgetProviderDefinitionDeleted += WidgetCatalog_WidgetProviderDefinitionDeleted;
            widgetCatalog!.WidgetDefinitionAdded += WidgetCatalog_WidgetDefinitionAdded;
            widgetCatalog!.WidgetDefinitionUpdated += WidgetCatalog_WidgetDefinitionUpdated;
            widgetCatalog!.WidgetDefinitionDeleted += WidgetCatalog_WidgetDefinitionDeleted;
        }
        catch (Exception ex)
        {
            Log.Logger()?.ReportError("DashboardView", "Exception in SubscribeToWidgetCatalogEvents:", ex);
            return false;
        }

        return true;
    }

    private async void HandleRendererUpdated(object sender, object args)
    {
        // Re-render the widgets with the new theme and renderer.
        foreach (var widget in PinnedWidgets)
        {
            await widget.RenderAsync();
        }
    }

    [RelayCommand]
    private async Task OnLoadedAsync()
    {
        Application.Current.GetService<IAdaptiveCardRenderingService>().RendererUpdated += HandleRendererUpdated;
        await InitializeDashboard();
    }

    [RelayCommand]
    private void OnUnloaded()
    {
        Application.Current.GetService<IAdaptiveCardRenderingService>().RendererUpdated -= HandleRendererUpdated;
    }

    private async Task InitializeDashboard()
    {
        LoadingWidgetsProgressRing.Visibility = Visibility.Visible;
        ViewModel.IsLoading = true;

        if (ViewModel.WidgetHostingService.CheckForWidgetServiceAsync())
        {
            ViewModel.HasWidgetService = true;
            if (await SubscribeToWidgetCatalogEventsAsync())
            {
                // Cache the widget icons before we display the widgets, since we include the icons in the widgets.
                await ViewModel.WidgetIconService.CacheAllWidgetIconsAsync();

                var isFirstDashboardRun = !(await _localSettingsService.ReadSettingAsync<bool>(WellKnownSettingsKeys.IsNotFirstDashboardRun));
                Log.Logger()?.ReportInfo("DashboardView", $"Is first dashboard run = {isFirstDashboardRun}");
                if (isFirstDashboardRun)
                {
                    await Application.Current.GetService<ILocalSettingsService>().SaveSettingAsync(WellKnownSettingsKeys.IsNotFirstDashboardRun, true);
                }

                await InitializePinnedWidgetListAsync(isFirstDashboardRun);
            }
            else
            {
                Log.Logger()?.ReportError("DashboardView", $"Catalog event subscriptions failed, show error");
                RestartDevHomeMessageStackPanel.Visibility = Visibility.Visible;
            }
        }
        else
        {
            var widgetServiceState = ViewModel.WidgetHostingService.GetWidgetServiceState();
            if (widgetServiceState == WidgetHostingService.WidgetServiceStates.HasStoreWidgetServiceNoOrBadVersion ||
                widgetServiceState == WidgetHostingService.WidgetServiceStates.HasWebExperienceNoOrBadVersion)
            {
                // Show error message that updating may help
                UpdateWidgetsMessageStackPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Log.Logger()?.ReportError("DashboardView", $"Initialization failed, WidgetServiceState unknown");
                RestartDevHomeMessageStackPanel.Visibility = Visibility.Visible;
            }
        }

        LoadingWidgetsProgressRing.Visibility = Visibility.Collapsed;
        ViewModel.IsLoading = false;
    }

    private async Task InitializePinnedWidgetListAsync(bool isFirstDashboardRun)
    {
        var hostWidgets = await GetPreviouslyPinnedWidgets();

        if ((hostWidgets == null || hostWidgets.Length == 0) && isFirstDashboardRun)
        {
            // If it's the first time the Dashboard has been displayed and we have no other widgets pinned to a
            // different version of Dev Home, pin some default widgets.
            Log.Logger()?.ReportInfo("DashboardView", $"Pin default widgets");
            await PinDefaultWidgetsAsync();
        }
        else if (hostWidgets != null)
        {
            await RestorePinnedWidgetsAsync(hostWidgets);
        }
    }

    private async Task<Widget[]> GetPreviouslyPinnedWidgets()
    {
        Log.Logger()?.ReportInfo("DashboardView", "Get widgets for current host");
        var widgetHost = await ViewModel.WidgetHostingService.GetWidgetHostAsync();
        var hostWidgets = await Task.Run(() => widgetHost?.GetWidgets());

        if (hostWidgets == null)
        {
            Log.Logger()?.ReportInfo("DashboardView", $"Found 0 widgets for this host");
            return null;
        }

        Log.Logger()?.ReportInfo("DashboardView", $"Found {hostWidgets.Length} widgets for this host");

        return hostWidgets;
    }

    private async Task RestorePinnedWidgetsAsync(Widget[] hostWidgets)
    {
        var restoredWidgetsWithPosition = new SortedDictionary<int, Widget>();
        var restoredWidgetsWithoutPosition = new SortedDictionary<int, Widget>();
        var numUnorderedWidgets = 0;

        // Widgets do not come from the host in a deterministic order, so save their order in each widget's CustomState.
        // Iterate through all the widgets and put them in order. If a widget does not have a position assigned to it,
        // append it at the end. If a position is missing, just show the next widget in order.
        foreach (var widget in hostWidgets)
        {
            try
            {
                var stateStr = await widget.GetCustomStateAsync();
                Log.Logger()?.ReportInfo("DashboardView", $"GetWidgetCustomState: {stateStr}");

                if (string.IsNullOrEmpty(stateStr))
                {
                    // If we have a widget with no state, Dev Home does not consider it a valid widget
                    // and should delete it, rather than letting it run invisibly in the background.
                    await DeleteAbandonedWidgetAsync(widget);
                    continue;
                }

                var stateObj = System.Text.Json.JsonSerializer.Deserialize(stateStr, SourceGenerationContext.Default.WidgetCustomState);
                if (stateObj.Host != WidgetHelpers.DevHomeHostName)
                {
                    // This shouldn't be able to be reached
                    Log.Logger()?.ReportError("DashboardView", $"Widget has custom state but no HostName.");
                    continue;
                }

                var position = stateObj.Position;
                if (position >= 0)
                {
                    if (!restoredWidgetsWithPosition.TryAdd(position, widget))
                    {
                        // If there was an error and a widget with this position is already there,
                        // treat this widget as unordered and put it into the unordered map.
                        restoredWidgetsWithoutPosition.Add(numUnorderedWidgets++, widget);
                    }
                }
                else
                {
                    // Widgets with no position will get the default of -1. Append these at the end.
                    restoredWidgetsWithoutPosition.Add(numUnorderedWidgets++, widget);
                }
            }
            catch (Exception ex)
            {
                Log.Logger()?.ReportError("DashboardView", $"RestorePinnedWidgets(): ", ex);
            }
        }

        // Merge the dictionaries for easier looping. restoredWidgetsWithoutPosition should be empty, so this should be fast.
        var lastOrderedKey = restoredWidgetsWithPosition.Count > 0 ? restoredWidgetsWithPosition.Last().Key : -1;
        restoredWidgetsWithoutPosition.ToList().ForEach(x => restoredWidgetsWithPosition.Add(++lastOrderedKey, x.Value));

        // Now that we've ordered the widgets, put them in their final collection.
        var finalPlace = 0;
        foreach (var orderedWidget in restoredWidgetsWithPosition)
        {
            var widget = orderedWidget.Value;
            var size = await widget.GetSizeAsync();
            await InsertWidgetInPinnedWidgetsAsync(widget, size, finalPlace++);
        }

        // Go through the newly created list of pinned widgets and update any positions that may have changed.
        // For example, if the provider for the widget at position 0 was deleted, the widget at position 1
        // should be updated to have position 0, etc.
        var updatedPlace = 0;
        foreach (var widget in PinnedWidgets)
        {
            await WidgetHelpers.SetPositionCustomStateAsync(widget.Widget, updatedPlace++);
        }
    }

    private async Task DeleteAbandonedWidgetAsync(Widget widget)
    {
        var widgetHost = await ViewModel.WidgetHostingService.GetWidgetHostAsync();

        var length = await Task.Run(() => widgetHost!.GetWidgets().Length);
        Log.Logger()?.ReportInfo("DashboardView", $"Found abandoned widget, try to delete it...");
        Log.Logger()?.ReportInfo("DashboardView", $"Before delete, {length} widgets for this host");

        await widget.DeleteAsync();

        var newWidgetList = await Task.Run(() => widgetHost.GetWidgets());
        length = (newWidgetList == null) ? 0 : newWidgetList.Length;
        Log.Logger()?.ReportInfo("DashboardView", $"After delete, {length} widgets for this host");
    }

    private async Task PinDefaultWidgetsAsync()
    {
        var catalog = await ViewModel.WidgetHostingService.GetWidgetCatalogAsync();

        if (catalog is null)
        {
            Log.Logger()?.ReportError("AddWidgetDialog", $"Trying to pin default widgets, but WidgetCatalog is null.");
            return;
        }

        var widgetDefinitions = await Task.Run(() => catalog!.GetWidgetDefinitions().OrderBy(x => x.DisplayTitle));
        foreach (var widgetDefinition in widgetDefinitions)
        {
            var id = widgetDefinition.Id;
            if (WidgetHelpers.DefaultWidgetDefinitionIds.Contains(id))
            {
                Log.Logger()?.ReportInfo("DashboardView", $"Found default widget {id}");
                await PinDefaultWidgetAsync(widgetDefinition);
            }
        }
    }

    private async Task PinDefaultWidgetAsync(WidgetDefinition defaultWidgetDefinition)
    {
        try
        {
            // Create widget
            var widgetHost = await ViewModel.WidgetHostingService.GetWidgetHostAsync();
            var size = WidgetHelpers.GetDefaultWidgetSize(defaultWidgetDefinition.GetWidgetCapabilities());
            var id = defaultWidgetDefinition.Id;
            var newWidget = await Task.Run(async () => await widgetHost?.CreateWidgetAsync(id, size));
            Log.Logger()?.ReportInfo("DashboardView", $"Created default widget {id}");

            // Set custom state on new widget.
            var position = PinnedWidgets.Count;
            var newCustomState = WidgetHelpers.CreateWidgetCustomState(position);
            Log.Logger()?.ReportDebug("DashboardView", $"SetCustomState: {newCustomState}");
            await newWidget.SetCustomStateAsync(newCustomState);

            // Put new widget on the Dashboard.
            await InsertWidgetInPinnedWidgetsAsync(newWidget, size, position);
            Log.Logger()?.ReportInfo("DashboardView", $"Inserted default widget {id} at position {position}");
        }
        catch (Exception ex)
        {
            Log.Logger()?.ReportError("AddWidgetDialog", $"PinDefaultWidget failed: ", ex);
        }
    }

    [RelayCommand]
    public async Task GoToWidgetsInStoreAsync()
    {
        if (Common.Helpers.RuntimeHelper.IsOnWindows11)
        {
            await Launcher.LaunchUriAsync(new($"ms-windows-store://pdp/?productid={WidgetHelpers.WebExperiencePackPackageId}"));
        }
        else
        {
            await Launcher.LaunchUriAsync(new($"ms-windows-store://pdp/?productid={WidgetHelpers.WidgetServiceStorePackageId}"));
        }
    }

    [RelayCommand]
    public async Task AddWidgetClickAsync()
    {
        var dialog = new AddWidgetDialog()
        {
            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app.
            XamlRoot = this.XamlRoot,
        };

        _ = await dialog.ShowAsync();

        var newWidgetDefinition = dialog.AddedWidget;

        if (newWidgetDefinition != null)
        {
            Widget newWidget;
            try
            {
                var size = WidgetHelpers.GetDefaultWidgetSize(newWidgetDefinition.GetWidgetCapabilities());
                var widgetHost = await ViewModel.WidgetHostingService.GetWidgetHostAsync();
                newWidget = await Task.Run(async () => await widgetHost?.CreateWidgetAsync(newWidgetDefinition.Id, size));

                // Set custom state on new widget.
                var position = PinnedWidgets.Count;
                var newCustomState = WidgetHelpers.CreateWidgetCustomState(position);
                Log.Logger()?.ReportDebug("DashboardView", $"SetCustomState: {newCustomState}");
                await newWidget.SetCustomStateAsync(newCustomState);

                // Put new widget on the Dashboard.
                await InsertWidgetInPinnedWidgetsAsync(newWidget, size, position);
            }
            catch (Exception ex)
            {
                Log.Logger()?.ReportWarn("AddWidgetDialog", $"Creating widget failed: ", ex);
                var mainWindow = Application.Current.GetService<WindowEx>();
                var stringResource = new StringResource("DevHome.Dashboard.pri", "DevHome.Dashboard/Resources");
                await mainWindow.ShowErrorMessageDialogAsync(
                    title: string.Empty,
                    content: stringResource.GetLocalized("CouldNotCreateWidgetError"),
                    buttonText: stringResource.GetLocalized("CloseButtonText"));
            }
        }
    }

    private async Task InsertWidgetInPinnedWidgetsAsync(Widget widget, WidgetSize size, int index)
    {
        await Task.Run(async () =>
        {
            var widgetDefinitionId = widget.DefinitionId;
            var widgetId = widget.Id;
            var widgetCatalog = await ViewModel.WidgetHostingService.GetWidgetCatalogAsync();
            var widgetDefinition = await Task.Run(() => widgetCatalog?.GetWidgetDefinition(widgetDefinitionId));

            if (widgetDefinition != null)
            {
                Log.Logger()?.ReportInfo("DashboardView", $"Insert widget in pinned widgets, id = {widgetId}, index = {index}");

                TelemetryFactory.Get<ITelemetry>().Log(
                    "Dashboard_ReportPinnedWidget",
                    LogLevel.Critical,
                    new ReportPinnedWidgetEvent(widgetDefinition.ProviderDefinition.Id, widgetDefinitionId));

                var wvm = _widgetViewModelFactory(widget, size, widgetDefinition);
                _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        PinnedWidgets.Insert(index, wvm);
                    }
                    catch (Exception ex)
                    {
                        // TODO Support concurrency in dashboard. Today concurrent async execution can cause insertion errors.
                        // https://github.com/microsoft/devhome/issues/1215
                        Log.Logger()?.ReportWarn("DashboardView", $"Couldn't insert pinned widget", ex);
                    }
                });
            }
            else
            {
                // If the widget provider was uninstalled while we weren't running, the catalog won't have the definition so delete the widget.
                Log.Logger()?.ReportInfo("DashboardView", $"No widget definition '{widgetDefinitionId}', delete widget {widgetId} with that definition");
                try
                {
                    await widget.SetCustomStateAsync(string.Empty);
                    await widget.DeleteAsync();
                }
                catch (Exception ex)
                {
                    Log.Logger()?.ReportInfo("DashboardView", $"Error deleting widget", ex);
                }
            }
        });
    }

    private void WidgetCatalog_WidgetProviderDefinitionAdded(WidgetCatalog sender, WidgetProviderDefinitionAddedEventArgs args)
        => Log.Logger()?.ReportInfo("DashboardView", $"WidgetCatalog_WidgetProviderDefinitionAdded {args.ProviderDefinition.Id}");

    private void WidgetCatalog_WidgetProviderDefinitionDeleted(WidgetCatalog sender, WidgetProviderDefinitionDeletedEventArgs args)
        => Log.Logger()?.ReportInfo("DashboardView", $"WidgetCatalog_WidgetProviderDefinitionDeleted {args.ProviderDefinitionId}");

    private async void WidgetCatalog_WidgetDefinitionAdded(WidgetCatalog sender, WidgetDefinitionAddedEventArgs args)
    {
        Log.Logger()?.ReportInfo("DashboardView", $"WidgetCatalog_WidgetDefinitionAdded {args.Definition.Id}");
        await ViewModel.WidgetIconService.AddIconsToCacheAsync(args.Definition);
    }

    private async void WidgetCatalog_WidgetDefinitionUpdated(WidgetCatalog sender, WidgetDefinitionUpdatedEventArgs args)
    {
        var updatedDefinitionId = args.Definition.Id;
        Log.Logger()?.ReportInfo("DashboardView", $"WidgetCatalog_WidgetDefinitionUpdated {updatedDefinitionId}");

        foreach (var widgetToUpdate in PinnedWidgets.Where(x => x.Widget.DefinitionId == updatedDefinitionId).ToList())
        {
            // Things in the definition that we need to update to if they have changed:
            // AllowMultiple, DisplayTitle, Capabilities (size), ThemeResource (icons)
            var oldDef = widgetToUpdate.WidgetDefinition;
            var newDef = args.Definition;

            // If we're no longer allowed to have multiple instances of this widget, delete all of them.
            if (newDef.AllowMultiple == false && oldDef.AllowMultiple == true)
            {
                _dispatcher.TryEnqueue(async () =>
                {
                    Log.Logger()?.ReportInfo("DashboardView", $"No longer allowed to have multiple of widget {newDef.Id}");
                    Log.Logger()?.ReportInfo("DashboardView", $"Delete widget {widgetToUpdate.Widget.Id}");
                    PinnedWidgets.Remove(widgetToUpdate);
                    await widgetToUpdate.Widget.DeleteAsync();
                    Log.Logger()?.ReportInfo("DashboardView", $"Deleted Widget {widgetToUpdate.Widget.Id}");
                });
            }
            else
            {
                // Changing the definition updates the DisplayTitle.
                widgetToUpdate.WidgetDefinition = newDef;

                // If the size the widget is currently set to is no longer supported by the widget, revert to its default size.
                // TODO: Need to update WidgetControl with now-valid sizes.
                // TODO: Properly compare widget capabilities.
                // https://github.com/microsoft/devhome/issues/641
                if (oldDef.GetWidgetCapabilities() != newDef.GetWidgetCapabilities())
                {
                    // TODO: handle the case where this change is made while Dev Home is not running -- how do we restore?
                    // https://github.com/microsoft/devhome/issues/641
                    if (!newDef.GetWidgetCapabilities().Any(cap => cap.Size == widgetToUpdate.WidgetSize))
                    {
                        var newDefaultSize = WidgetHelpers.GetDefaultWidgetSize(newDef.GetWidgetCapabilities());
                        widgetToUpdate.WidgetSize = newDefaultSize;
                        await widgetToUpdate.Widget.SetSizeAsync(newDefaultSize);
                    }
                }
            }

            // TODO: ThemeResource (icons) changed.
            // https://github.com/microsoft/devhome/issues/641
        }
    }

    // Remove widget(s) from the Dashboard if the provider deletes the widget definition, or the provider is uninstalled.
    private void WidgetCatalog_WidgetDefinitionDeleted(WidgetCatalog sender, WidgetDefinitionDeletedEventArgs args)
    {
        var definitionId = args.DefinitionId;
        _dispatcher.TryEnqueue(async () =>
        {
            Log.Logger()?.ReportInfo("DashboardView", $"WidgetDefinitionDeleted {definitionId}");
            foreach (var widgetToRemove in PinnedWidgets.Where(x => x.Widget.DefinitionId == definitionId).ToList())
            {
                Log.Logger()?.ReportInfo("DashboardView", $"Remove widget {widgetToRemove.Widget.Id}");
                PinnedWidgets.Remove(widgetToRemove);

                // The widget definition is gone, so delete widgets with that definition.
                await widgetToRemove.Widget.DeleteAsync();
            }
        });

        ViewModel.WidgetIconService.RemoveIconsFromCache(definitionId);
    }

    // If a widget is removed from the list, update the saved positions of the following widgets.
    // If not updated, widges pinned later may be assigned the same position as existing widgets,
    // since the saved position may be greater than the number of pinned widgets.
    // Unsubscribe from this event during drag and drop, since the drop event takes care of re-numbering.
    private async void OnPinnedWidgetsCollectionChangedAsync(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            await _pinnedWidgetsLock.WaitAsync();
            try
            {
                var removedIndex = e.OldStartingIndex;
                Log.Logger()?.ReportDebug("DashboardView", $"Removed widget at index {removedIndex}");
                for (var i = removedIndex; i < PinnedWidgets.Count; i++)
                {
                    Log.Logger()?.ReportDebug("DashboardView", $"Updatingg widget position for widget now at {i}");
                    var widgetToUpdate = PinnedWidgets.ElementAt(i);
                    await WidgetHelpers.SetPositionCustomStateAsync(widgetToUpdate.Widget, i);
                }
            }
            finally
            {
                _pinnedWidgetsLock.Release();
            }
        }
    }

    private void WidgetGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        Log.Logger()?.ReportDebug("DashboardView", $"Drag starting");

        // When drag starts, save the WidgetViewModel and the original index of the widget being dragged.
        var draggedObject = e.Items.FirstOrDefault();
        var draggedWidgetViewModel = draggedObject as WidgetViewModel;
        e.Data.Properties.Add(DraggedWidget, draggedWidgetViewModel);
        e.Data.Properties.Add(DraggedIndex, PinnedWidgets.IndexOf(draggedWidgetViewModel));
    }

    private void WidgetControl_DragOver(object sender, DragEventArgs e)
    {
        // A widget may be dropped on top of another widget, in which case the dropped widget will take the target widget's place.
        if (e.Data != null)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }
        else
        {
            // If the dragged item doesn't have a DataPackage, don't allow it to be dropped.
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        }
    }

    private async void WidgetControl_Drop(object sender, DragEventArgs e)
    {
        Log.Logger()?.ReportDebug("DashboardView", $"Drop starting");

        // If the the thing we're dragging isn't a widget, it might not have a DataPackage and we shouldn't do anything with it.
        if (e.Data == null)
        {
            return;
        }

        // When drop happens, get the original index of the widget that was dragged and dropped.
        var result = e.Data.Properties.TryGetValue(DraggedIndex, out var draggedIndexObject);
        if (!result || draggedIndexObject == null)
        {
            return;
        }

        var draggedIndex = (int)draggedIndexObject;

        // Get the index of the widget that was dropped onto -- the dragged widget will take the place of this one,
        // and this widget and all subsequent widgets will move over to the right.
        var droppedControl = sender as WidgetControl;
        var droppedIndex = WidgetGridView.Items.IndexOf(droppedControl.WidgetSource);
        Log.Logger()?.ReportInfo("DashboardView", $"Widget dragged from index {draggedIndex} to {droppedIndex}");

        // If the widget is dropped at the position it's already at, there's nothing to do.
        if (draggedIndex == droppedIndex)
        {
            return;
        }

        result = e.Data.Properties.TryGetValue(DraggedWidget, out var draggedObject);
        if (!result || draggedObject == null)
        {
            return;
        }

        var draggedWidgetViewModel = draggedObject as WidgetViewModel;

        // Remove the moved widget then insert it back in the collection at the new location. If the dropped widget was
        // moved from a lower index to a higher one, removing the moved widget before inserting it will ensure that any
        // widgets between the starting and ending indices move up to replace the removed widget. If the widget was
        // moved from a higher index to a lower one, then the order of removal and insertion doesn't matter.
        PinnedWidgets.CollectionChanged -= OnPinnedWidgetsCollectionChangedAsync;

        PinnedWidgets.RemoveAt(draggedIndex);
        var size = await draggedWidgetViewModel.Widget.GetSizeAsync();
        await InsertWidgetInPinnedWidgetsAsync(draggedWidgetViewModel.Widget, size, droppedIndex);
        await WidgetHelpers.SetPositionCustomStateAsync(draggedWidgetViewModel.Widget, droppedIndex);

        // Update the CustomState Position of any widgets that were moved.
        // The widget that has been dropped has already been updated, so don't do it again here.
        var startIndex = draggedIndex < droppedIndex ? draggedIndex : droppedIndex + 1;
        var endIndex = draggedIndex < droppedIndex ? droppedIndex : draggedIndex + 1;
        for (var i = startIndex; i < endIndex; i++)
        {
            var widgetToUpdate = PinnedWidgets.ElementAt(i);
            await WidgetHelpers.SetPositionCustomStateAsync(widgetToUpdate.Widget, i);
        }

        PinnedWidgets.CollectionChanged += OnPinnedWidgetsCollectionChangedAsync;

        Log.Logger()?.ReportDebug("DashboardView", $"Drop ended");
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        Log.Logger()?.ReportDebug("DashboardView", $"Leaving Dashboard, deactivating widgets.");

        // Deactivate widgets if we're not on the Dashboard.
        foreach (var widget in PinnedWidgets)
        {
            widget.UnsubscribeFromWidgetUpdates();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _pinnedWidgetsLock.Dispose();
            }

            _disposedValue = true;
        }
    }

#if DEBUG
    private void AddResetButton(object sender, RoutedEventArgs e)
    {
        var resetButton = new Button
        {
            Content = new SymbolIcon(Symbol.Refresh),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 4,
        };
        resetButton.Click += ResetButton_Click;
        AutomationProperties.SetName(resetButton, "ResetBannerButton");
        var parent = AddWidgetButton.Parent as StackPanel;
        var index = parent.Children.IndexOf(AddWidgetButton);
        parent.Children.Insert(index + 1, resetButton);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var roamingProperties = Windows.Storage.ApplicationData.Current.RoamingSettings.Values;
        if (roamingProperties.ContainsKey("HideDashboardBanner"))
        {
            roamingProperties.Remove("HideDashboardBanner");
        }

        BannerViewModel.ResetDashboardBanner();
    }
#endif
}
