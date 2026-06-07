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

    private void AxisPairAssignWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        AttachToViewModel(DataContext as AxisPairAssignViewModel);

        if (DataContext is AxisPairAssignViewModel viewModel)
            viewModel.Start();

        DrawAllPlots();
    }

    private void AxisPairAssignWindow_Closed(
        object? sender,
        EventArgs e)
    {
        DetachFromViewModel();

        if (DataContext is AxisPairAssignViewModel viewModel)
            viewModel.Dispose();
    }

    private void AxisPairAssignWindow_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel();
        AttachToViewModel(e.NewValue as AxisPairAssignViewModel);
        DrawAllPlots();
    }

    private void AttachToViewModel(
        AxisPairAssignViewModel? viewModel)
    {
        if (viewModel is null ||
            ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        _attachedViewModel = viewModel;
        _attachedViewModel.PropertyChanged += AxisPairViewModel_PropertyChanged;
        _attachedViewModel.Primary.PropertyChanged += Primary_PropertyChanged;
        _attachedViewModel.Secondary.PropertyChanged += Secondary_PropertyChanged;
    }

    private void DetachFromViewModel()
    {
        if (_attachedViewModel is null)
            return;

        _attachedViewModel.PropertyChanged -= AxisPairViewModel_PropertyChanged;
        _attachedViewModel.Primary.PropertyChanged -= Primary_PropertyChanged;
        _attachedViewModel.Secondary.PropertyChanged -= Secondary_PropertyChanged;
        _attachedViewModel = null;
    }

    private void AxisPairViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AxisPairAssignViewModel.RawX) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.RawY) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.OutputX) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.OutputY) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.IsMappingPrimary) ||
            e.PropertyName == nameof(AxisPairAssignViewModel.IsMappingSecondary))
        {
            DrawAxisPairPlot();
            DrawResponsePlots();
        }
    }

    private void Primary_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (IsAxisTuningProperty(e.PropertyName))
        {
            DrawAxisPairPlot();
            DrawPrimaryResponsePlot();
        }
    }

    private void Secondary_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (IsAxisTuningProperty(e.PropertyName))
        {
            DrawAxisPairPlot();
            DrawSecondaryResponsePlot();
        }
    }

    private static bool IsAxisTuningProperty(string? propertyName)
    {
        return propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.Invert) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.DeadzoneCurve) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.DeadzoneStep) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.SaturationCurve) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.SaturationStep) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.CurveValue) ||
               propertyName ==
                   nameof(AxisPairAssignViewModel.AxisEditViewModel.CurveStep);
    }

    private void AxisPairPlotCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        DrawAxisPairPlot();
    }

    private void ResponsePlotCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        DrawResponsePlots();
    }

    private void DrawAllPlots()
    {
        DrawAxisPairPlot();
        DrawResponsePlots();
    }

    private void DrawResponsePlots()
    {
        DrawPrimaryResponsePlot();
        DrawSecondaryResponsePlot();
    }

    private void DrawPrimaryResponsePlot()
    {
        if (DataContext is not AxisPairAssignViewModel viewModel)
            return;

        DrawResponsePlot(
            PrimaryResponsePlotCanvas,
            viewModel.Primary,
            viewModel.RawY);
    }

    private void DrawSecondaryResponsePlot()
    {
        if (DataContext is not AxisPairAssignViewModel viewModel)
            return;

        DrawResponsePlot(
            SecondaryResponsePlotCanvas,
            viewModel.Secondary,
            viewModel.RawX);
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

        double horizontalLabelSpace = 48;
        double verticalLabelSpace = 38;

        double availableWidth = Math.Max(
            0,
            actualWidth - horizontalLabelSpace * 2);

        double availableHeight = Math.Max(
            0,
            actualHeight - verticalLabelSpace * 2);

        double size = Math.Max(
            10,
            Math.Min(availableWidth, availableHeight));

        double left = (actualWidth - size) / 2.0;
        double top = (actualHeight - size) / 2.0;
        double right = left + size;
        double bottom = top + size;
        double centerX = left + size / 2.0;
        double centerY = top + size / 2.0;

        Brush mutedBrush = FindBrush(
            "AppMutedForegroundBrush",
            Brushes.LightGray);

        Brush accentBrush = FindBrush(
            "AppAccentBrush",
            Brushes.CornflowerBlue);

        Brush plotBackgroundBrush = FindBrush(
            "AppSurfaceAltBrush",
            Brushes.Transparent);

        var border = new Border
        {
            Width = size,
            Height = size,
            Background = plotBackgroundBrush,
            BorderBrush = CloneBrushWithOpacity(accentBrush, 0.65),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(5)
        };

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        AxisPairPlotCanvas.Children.Add(border);

        if (viewModel.IsMappingPrimary)
        {
            double bandWidth = size * 0.16;

            var primaryBand = new Rectangle
            {
                Width = bandWidth,
                Height = size,
                Fill = CloneBrushWithOpacity(accentBrush, 0.18)
            };

            Canvas.SetLeft(
                primaryBand,
                left + (size - bandWidth) / 2.0);

            Canvas.SetTop(primaryBand, top);
            AxisPairPlotCanvas.Children.Add(primaryBand);
        }

        if (viewModel.IsMappingSecondary)
        {
            double bandHeight = size * 0.16;

            var secondaryBand = new Rectangle
            {
                Width = size,
                Height = bandHeight,
                Fill = CloneBrushWithOpacity(accentBrush, 0.18)
            };

            Canvas.SetLeft(secondaryBand, left);

            Canvas.SetTop(
                secondaryBand,
                top + (size - bandHeight) / 2.0);

            AxisPairPlotCanvas.Children.Add(secondaryBand);
        }

        for (int i = 1; i < 8; i++)
        {
            double x = left + size * i / 8.0;
            double y = top + size * i / 8.0;

            AddLine(
                AxisPairPlotCanvas,
                x,
                top,
                x,
                bottom,
                CloneBrushWithOpacity(mutedBrush, 0.24),
                0.7);

            AddLine(
                AxisPairPlotCanvas,
                left,
                y,
                right,
                y,
                CloneBrushWithOpacity(mutedBrush, 0.24),
                0.7);
        }

        AddLine(
            AxisPairPlotCanvas,
            centerX,
            top,
            centerX,
            bottom,
            CloneBrushWithOpacity(mutedBrush, 0.85),
            1.2);

        AddLine(
            AxisPairPlotCanvas,
            left,
            centerY,
            right,
            centerY,
            CloneBrushWithOpacity(mutedBrush, 0.85),
            1.2);

        AddLabel(
            AxisPairPlotCanvas,
            "Up",
            centerX,
            top - 27,
            accentBrush,
            18,
            FontWeights.Bold);

        AddLabel(
            AxisPairPlotCanvas,
            "Down",
            centerX,
            bottom + 7,
            accentBrush,
            18,
            FontWeights.Bold);

        AddLabel(
            AxisPairPlotCanvas,
            "Left",
            left - 32,
            centerY - 11,
            accentBrush,
            18,
            FontWeights.Bold);

        AddLabel(
            AxisPairPlotCanvas,
            "Right",
            right + 32,
            centerY - 11,
            accentBrush,
            18,
            FontWeights.Bold);

        Point rawPoint = GetPlotPoint(
            left,
            top,
            size,
            viewModel.RawX,
            viewModel.RawY);

        Point outputPoint = GetPlotPoint(
            left,
            top,
            size,
            viewModel.OutputX,
            viewModel.OutputY);

        AddLine(
            AxisPairPlotCanvas,
            rawPoint.X,
            rawPoint.Y,
            outputPoint.X,
            outputPoint.Y,
            CloneBrushWithOpacity(mutedBrush, 0.65),
            1.0);

        Brush actualPositionBrush = FindBrush(
            "AppForegroundBrush",
            Brushes.White);

        var rawMarker = new Ellipse
        {
            Width = 13,
            Height = 13,
            Fill = actualPositionBrush,
            Stroke = CloneBrushWithOpacity(accentBrush, 0.85),
            StrokeThickness = 1.2
        };

        Canvas.SetLeft(rawMarker, rawPoint.X - 6.5);
        Canvas.SetTop(rawMarker, rawPoint.Y - 6.5);
        AxisPairPlotCanvas.Children.Add(rawMarker);

        AddLine(
            AxisPairPlotCanvas,
            outputPoint.X - 9,
            outputPoint.Y,
            outputPoint.X + 9,
            outputPoint.Y,
            accentBrush,
            3.0);

        AddLine(
            AxisPairPlotCanvas,
            outputPoint.X,
            outputPoint.Y - 9,
            outputPoint.X,
            outputPoint.Y + 9,
            accentBrush,
            3.0);
    }

    private void DrawResponsePlot(
        Canvas canvas,
        AxisPairAssignViewModel.AxisEditViewModel axis,
        double currentRawValue)
    {
        if (canvas is null)
            return;

        canvas.Children.Clear();

        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        Brush mutedBrush = FindBrush(
            "AppMutedForegroundBrush",
            Brushes.LightGray);

        Brush accentBrush = FindBrush(
            "AppAccentBrush",
            Brushes.CornflowerBlue);

        Brush plotBackgroundBrush = FindBrush(
            "AppSurfaceAltBrush",
            Brushes.Transparent);

        var background = new Border
        {
            Width = width,
            Height = height,
            Background = plotBackgroundBrush,
            BorderBrush = CloneBrushWithOpacity(accentBrush, 0.55),
            BorderThickness = new Thickness(1)
        };

        Canvas.SetLeft(background, 0);
        Canvas.SetTop(background, 0);
        canvas.Children.Add(background);

        for (int i = 1; i < 8; i++)
        {
            double x = width * i / 8.0;
            double y = height * i / 8.0;

            AddLine(
                canvas,
                x,
                0,
                x,
                height,
                CloneBrushWithOpacity(mutedBrush, 0.22),
                0.7);

            AddLine(
                canvas,
                0,
                y,
                width,
                y,
                CloneBrushWithOpacity(mutedBrush, 0.22),
                0.7);
        }

        AddLine(
            canvas,
            width / 2.0,
            0,
            width / 2.0,
            height,
            CloneBrushWithOpacity(mutedBrush, 0.8),
            1.1);

        AddLine(
            canvas,
            0,
            height / 2.0,
            width,
            height / 2.0,
            CloneBrushWithOpacity(mutedBrush, 0.8),
            1.1);

        var responseLine = new Polyline
        {
            Stroke = accentBrush,
            StrokeThickness = 1.8
        };

        const int sampleCount = 160;

        for (int sample = 0; sample <= sampleCount; sample++)
        {
            double input =
                -1.0 + 2.0 * sample / sampleCount;

            double output =
                AxisPairAssignViewModel.CalculateAxisOutput(
                    input,
                    axis);

            responseLine.Points.Add(
                ResponsePoint(
                    width,
                    height,
                    input,
                    output));
        }

        canvas.Children.Add(responseLine);

        double currentOutput =
            AxisPairAssignViewModel.CalculateAxisOutput(
                currentRawValue,
                axis);

        Point currentPoint = ResponsePoint(
            width,
            height,
            currentRawValue,
            currentOutput);

        var currentMarker = new Rectangle
        {
            Width = 11,
            Height = 11,
            Fill = accentBrush,
            Stroke = FindBrush(
                "AppWindowBackgroundBrush",
                Brushes.Black),
            StrokeThickness = 1.2
        };

        Canvas.SetLeft(
            currentMarker,
            currentPoint.X - currentMarker.Width / 2.0);

        Canvas.SetTop(
            currentMarker,
            currentPoint.Y - currentMarker.Height / 2.0);

        canvas.Children.Add(currentMarker);
    }

    private static Point ResponsePoint(
        double width,
        double height,
        double input,
        double output)
    {
        double clampedInput = Math.Max(
            -1.0,
            Math.Min(1.0, input));

        double clampedOutput = Math.Max(
            -1.0,
            Math.Min(1.0, output));

        return new Point(
            (clampedInput + 1.0) * 0.5 * width,
            (1.0 - (clampedOutput + 1.0) * 0.5) * height);
    }

    private static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush stroke,
        double thickness)
    {
        canvas.Children.Add(new Line
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

    private static void AddLabel(
        Canvas canvas,
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

        label.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        Canvas.SetLeft(
            label,
            centerX - label.DesiredSize.Width / 2.0);

        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private static Point GetPlotPoint(
        double left,
        double top,
        double size,
        double x,
        double y)
    {
        double clampedX = Math.Max(
            -1.0,
            Math.Min(1.0, x));

        double clampedY = Math.Max(
            -1.0,
            Math.Min(1.0, y));

        double px =
            left + (clampedX + 1.0) * 0.5 * size;

        double py =
            top + (1.0 - (clampedY + 1.0) * 0.5) * size;

        return new Point(px, py);
    }

    private Brush FindBrush(
        string resourceKey,
        Brush fallback)
    {
        object resource = TryFindResource(resourceKey);

        return resource is Brush brush
            ? brush
            : fallback;
    }

    private static Brush CloneBrushWithOpacity(
        Brush source,
        double opacity)
    {
        Brush clone = source.Clone();
        clone.Opacity = opacity;
        return clone;
    }
}