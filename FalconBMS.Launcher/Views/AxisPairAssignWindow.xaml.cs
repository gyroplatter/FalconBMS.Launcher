using FalconBMS.Launcher.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FalconBMS.Launcher.Views;

/// <summary>
/// Dedicated window for assigning one physical X/Y control as two BMS axes.
/// </summary>
public partial class AxisPairAssignWindow : Window
{
    private AxisPairAssignViewModel? _attachedViewModel;

    public AxisPairAssignWindow()
    {
        InitializeComponent();

        Loaded += AxisPairAssignWindow_Loaded;
        Closed += AxisPairAssignWindow_Closed;
        DataContextChanged += AxisPairAssignWindow_DataContextChanged;
    }

    private void AxisPairAssignWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AttachToViewModel(DataContext as AxisPairAssignViewModel);

        if (DataContext is AxisPairAssignViewModel viewModel)
            viewModel.Start();

        DrawAxisPairPlot();
    }

    private void AxisPairAssignWindow_Closed(object? sender, EventArgs e)
    {
        DetachFromViewModel();

        if (DataContext is AxisPairAssignViewModel viewModel)
            viewModel.Dispose();
    }

    private void AxisPairAssignWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel();
        AttachToViewModel(e.NewValue as AxisPairAssignViewModel);
        DrawAxisPairPlot();
    }

    private void AttachToViewModel(AxisPairAssignViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_attachedViewModel, viewModel))
            return;

        _attachedViewModel = viewModel;
        _attachedViewModel.PropertyChanged += AxisPairViewModel_PropertyChanged;
    }

    private void DetachFromViewModel()
    {
        if (_attachedViewModel is null)
            return;

        _attachedViewModel.PropertyChanged -= AxisPairViewModel_PropertyChanged;
        _attachedViewModel = null;
    }

    private void AxisPairViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AxisPairAssignViewModel.RawX) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.RawY) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.OutputX) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.OutputY) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.DeadzoneRadius) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.IsMappingPrimary) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.IsMappingSecondary))
        {
            DrawAxisPairPlot();
        }
    }

    private void AxisPairPlotCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawAxisPairPlot();
    }

    private void DrawAxisPairPlot()
    {
        if (AxisPairPlotCanvas is null)
            return;

        AxisPairPlotCanvas.Children.Clear();

        if (DataContext is not AxisPairAssignViewModel viewModel)
            return;

        double actualWidth = AxisPairPlotCanvas.ActualWidth;
        double actualHeight = AxisPairPlotCanvas.ActualHeight;

        if (actualWidth <= 0 || actualHeight <= 0)
            return;

        // Use more of the existing canvas without changing the popup window size.
        // The labels still sit outside the plot square, but with less reserved padding.
        double horizontalLabelSpace = 48;
        double verticalLabelSpace = 38;
        double availableWidth = Math.Max(0, actualWidth - horizontalLabelSpace * 2);
        double availableHeight = Math.Max(0, actualHeight - verticalLabelSpace * 2);
        double size = Math.Max(10, Math.Min(availableWidth, availableHeight));

        double left = (actualWidth - size) / 2.0;
        double top = (actualHeight - size) / 2.0;
        double right = left + size;
        double bottom = top + size;
        double centerX = left + size / 2.0;
        double centerY = top + size / 2.0;

        Brush mutedBrush = FindBrush("AppMutedForegroundBrush", Brushes.LightGray);
        Brush accentBrush = FindBrush("AppAccentBrush", Brushes.CornflowerBlue);
        Brush plotBackgroundBrush = FindBrush("AppSurfaceAltBrush", Brushes.Transparent);

        // Main plot square.
        var border = new Border
        {
            Width = size,
            Height = size,
            Background = plotBackgroundBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(5)
        };

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        AxisPairPlotCanvas.Children.Add(border);

        // Mapping highlight band.
        if (viewModel.IsMappingPrimary)
        {
            // Primary capture mode: highlight the vertical/Y axis band.
            double bandWidth = size * 0.16;

            var primaryBand = new Rectangle
            {
                Width = bandWidth,
                Height = size,
                Fill = CloneBrushWithOpacity(accentBrush, 0.18)
            };

            Canvas.SetLeft(primaryBand, left + (size - bandWidth) / 2.0);
            Canvas.SetTop(primaryBand, top);
            AxisPairPlotCanvas.Children.Add(primaryBand);
        }

        if (viewModel.IsMappingSecondary)
        {
            // Secondary capture mode: highlight the horizontal/X axis band.
            double bandHeight = size * 0.16;

            var secondaryBand = new Rectangle
            {
                Width = size,
                Height = bandHeight,
                Fill = CloneBrushWithOpacity(accentBrush, 0.18)
            };

            Canvas.SetLeft(secondaryBand, left);
            Canvas.SetTop(secondaryBand, top + (size - bandHeight) / 2.0);
            AxisPairPlotCanvas.Children.Add(secondaryBand);
        }

        // Grid lines.
        for (int i = 1; i < 4; i++)
        {
            double x = left + size * i / 4.0;
            AddLine(x, top, x, bottom, CloneBrushWithOpacity(mutedBrush, 0.35), 0.7);

            double y = top + size * i / 4.0;
            AddLine(left, y, right, y, CloneBrushWithOpacity(mutedBrush, 0.35), 0.7);
        }

        // Center lines.
        AddLine(centerX, top, centerX, bottom, CloneBrushWithOpacity(mutedBrush, 0.85), 1.2);
        AddLine(left, centerY, right, centerY, CloneBrushWithOpacity(mutedBrush, 0.85), 1.2);

        // Deadzone circle.
        double deadzoneRadius = Clamp01(viewModel.DeadzoneRadius);

        if (deadzoneRadius > 0.001)
        {
            double pixelRadius = deadzoneRadius * size / 2.0;

            var deadzone = new Ellipse
            {
                Width = pixelRadius * 2,
                Height = pixelRadius * 2,
                Fill = CloneBrushWithOpacity(mutedBrush, 0.24),
                Stroke = CloneBrushWithOpacity(accentBrush, 0.75),
                StrokeThickness = 1.2
            };

            Canvas.SetLeft(deadzone, centerX - pixelRadius);
            Canvas.SetTop(deadzone, centerY - pixelRadius);
            AxisPairPlotCanvas.Children.Add(deadzone);
        }

        AddLabel("Up", centerX, top - 24, accentBrush, 18, FontWeights.Bold);
        AddLabel("Down", centerX, bottom + 16, accentBrush, 18, FontWeights.Bold);
        AddLabel("Left", left - 42, centerY - 10, accentBrush, 18, FontWeights.Bold);
        AddLabel("Right", right + 42, centerY - 10, accentBrush, 18, FontWeights.Bold);

        Point rawPoint = GetPlotPoint(left, top, size, viewModel.RawX, viewModel.RawY);
        Point outputPoint = GetPlotPoint(left, top, size, viewModel.OutputX, viewModel.OutputY);

        // Connector from actual physical position to in-game output position.
        AddLine(
            rawPoint.X,
            rawPoint.Y,
            outputPoint.X,
            outputPoint.Y,
            CloneBrushWithOpacity(mutedBrush, 0.65),
            1.0);

        // Actual position: larger, high-contrast filled circle.
        var actualPositionBrush = FindBrush("AppForegroundBrush", Brushes.Black);

        var rawMarker = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = actualPositionBrush,
            Stroke = CloneBrushWithOpacity(accentBrush, 0.85),
            StrokeThickness = 1.2
        };

        Canvas.SetLeft(rawMarker, rawPoint.X - 5.5);
        Canvas.SetTop(rawMarker, rawPoint.Y - 5.5);
        AxisPairPlotCanvas.Children.Add(rawMarker);

        // In-game position: plus marker.
        AddLine(outputPoint.X - 8, outputPoint.Y, outputPoint.X + 8, outputPoint.Y, accentBrush, 1.8);
        AddLine(outputPoint.X, outputPoint.Y - 8, outputPoint.X, outputPoint.Y + 8, accentBrush, 1.8);
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        AxisPairPlotCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
            SnapsToDevicePixels = true
        });
    }

    private void AddLabel(
        string text,
        double centerX,
        double top,
        Brush brush,
        double fontSize,
        FontWeight fontWeight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextAlignment = TextAlignment.Center
        };

        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2.0);
        Canvas.SetTop(label, top);
        AxisPairPlotCanvas.Children.Add(label);
    }

    private static Point GetPlotPoint(double left, double top, double size, double x, double y)
    {
        double clampedX = Math.Max(-1.0, Math.Min(1.0, x));
        double clampedY = Math.Max(-1.0, Math.Min(1.0, y));

        double px = left + (clampedX + 1.0) * 0.5 * size;

        // WPF Y coordinates grow downward. Positive normalized Y should draw upward.
        double py = top + (1.0 - (clampedY + 1.0) * 0.5) * size;

        return new Point(px, py);
    }

    private Brush FindBrush(string resourceKey, Brush fallback)
    {
        object resource = TryFindResource(resourceKey);

        if (resource is Brush brush)
            return brush;

        return fallback;
    }

    private static Brush CloneBrushWithOpacity(Brush source, double opacity)
    {
        Brush clone = source.Clone();
        clone.Opacity = opacity;
        return clone;
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        return Math.Max(0, Math.Min(1, value));
    }
}