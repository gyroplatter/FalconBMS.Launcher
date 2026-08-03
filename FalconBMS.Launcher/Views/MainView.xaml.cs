using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace FalconBMS.Launcher.Views;

public partial class MainView : UserControl
{
    private Point? _communityToolDragStart;
    private ThirdPartyToolItem? _draggedCommunityTool;
    private List<ThirdPartyToolItem>? _preDragOrder;
    private bool _isDraggingCommunityTool;
    private bool _communityToolOrderChanged;

    public MainView()
    {
        InitializeComponent();

        // IMPORTANT:
        // Do NOT set DataContext here.
        // The MainWindow ContentControl + DataTemplate provides the shared MainViewModel instance.
    }

    private void Hyperlink_RequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });

        e.Handled = true;
    }

    private void CommunityToolTile_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement tile ||
            tile.DataContext is not ThirdPartyToolItem item)
        {
            return;
        }

        _communityToolDragStart =
            e.GetPosition(CommunityToolsItemsControl);

        _draggedCommunityTool =
            item;

        _isDraggingCommunityTool =
            false;

        _communityToolOrderChanged =
            false;

        if (DataContext is MainViewModel vm &&
            vm.IsEditingCommunityTools)
        {
            _preDragOrder =
                vm.ThirdPartyItems.ToList();

            Mouse.Capture(
                tile);
        }
        else
        {
            _preDragOrder =
                null;
        }
    }

    private void CommunityToolTile_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            !vm.IsEditingCommunityTools ||
            _communityToolDragStart is null ||
            _draggedCommunityTool is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPosition =
            e.GetPosition(CommunityToolsItemsControl);

        Vector movement =
            currentPosition -
            _communityToolDragStart.Value;

        if (!_isDraggingCommunityTool)
        {
            bool crossedDragThreshold =
                Math.Abs(movement.X) >=
                    SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(movement.Y) >=
                    SystemParameters.MinimumVerticalDragDistance;

            if (!crossedDragThreshold)
                return;

            _isDraggingCommunityTool =
                true;

            SetDraggedTileOpacity(
                0.55);
        }

        HitTestResult? hitResult =
            VisualTreeHelper.HitTest(
                CommunityToolsItemsControl,
                currentPosition);

        if (hitResult is null)
            return;

        DependencyObject? targetContainer =
            ItemsControl.ContainerFromElement(
                CommunityToolsItemsControl,
                hitResult.VisualHit);

        if (targetContainer is not FrameworkElement targetElement ||
            targetElement.DataContext is not ThirdPartyToolItem targetTool ||
            ReferenceEquals(targetTool, _draggedCommunityTool))
        {
            return;
        }

        int oldIndex =
            vm.ThirdPartyItems.IndexOf(
                _draggedCommunityTool);

        int targetIndex =
            vm.ThirdPartyItems.IndexOf(
                targetTool);

        if (oldIndex < 0 ||
            targetIndex < 0 ||
            oldIndex == targetIndex)
        {
            return;
        }

        Point pointerInsideTarget =
            e.GetPosition(targetElement);

        double targetMidpoint =
            targetElement.ActualWidth / 2.0;

        if (oldIndex < targetIndex &&
            pointerInsideTarget.X < targetMidpoint)
        {
            return;
        }

        if (oldIndex > targetIndex &&
            pointerInsideTarget.X > targetMidpoint)
        {
            return;
        }

        vm.MoveThirdPartyTool(
            oldIndex,
            targetIndex);

        _communityToolOrderChanged =
            true;

        SetDraggedTileOpacity(
            0.55);
    }

    private void CommunityToolTile_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            ResetCommunityToolDragState();
            return;
        }

        ThirdPartyToolItem? clickedTool =
            _draggedCommunityTool;

        bool completedDrag =
            _isDraggingCommunityTool;

        SetDraggedTileOpacity(
            1.0);

        if (Mouse.Captured is not null)
            Mouse.Capture(null);

        if (completedDrag)
        {
            if (_communityToolOrderChanged &&
                _preDragOrder is not null)
            {
                vm.CompleteThirdPartyToolReorder(
                    _preDragOrder);
            }
        }
        else if (!vm.IsEditingCommunityTools &&
                 clickedTool is not null &&
                 vm.LaunchThirdPartyCommand.CanExecute(clickedTool))
        {
            vm.LaunchThirdPartyCommand.Execute(
                clickedTool);
        }

        ResetCommunityToolDragState();

        e.Handled =
            true;
    }

    private void SetDraggedTileOpacity(
        double opacity)
    {
        if (_draggedCommunityTool is null)
            return;

        DependencyObject? container =
            CommunityToolsItemsControl
                .ItemContainerGenerator
                .ContainerFromItem(
                    _draggedCommunityTool);

        if (container is ContentPresenter presenter)
            presenter.Opacity = opacity;
    }

    private void ResetCommunityToolDragState()
    {
        _communityToolDragStart =
            null;

        _draggedCommunityTool =
            null;

        _preDragOrder =
            null;

        _isDraggingCommunityTool =
            false;

        _communityToolOrderChanged =
            false;
    }
}