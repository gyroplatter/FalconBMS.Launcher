using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FalconBMS.Launcher.Controls;

public partial class AxisBar : UserControl
{
    public AxisBar()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(AxisBar),
            new PropertyMetadata(0.0));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(AxisBar),
            new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty LeftLabelProperty =
        DependencyProperty.Register(
            nameof(LeftLabel),
            typeof(string),
            typeof(AxisBar),
            new PropertyMetadata(string.Empty));

    public string LeftLabel
    {
        get => (string)GetValue(LeftLabelProperty);
        set => SetValue(LeftLabelProperty, value);
    }

    public static readonly DependencyProperty RightLabelProperty =
        DependencyProperty.Register(
            nameof(RightLabel),
            typeof(string),
            typeof(AxisBar),
            new PropertyMetadata(string.Empty));

    public string RightLabel
    {
        get => (string)GetValue(RightLabelProperty);
        set => SetValue(RightLabelProperty, value);
    }

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(AxisBar),
            new PropertyMetadata(SystemColors.HighlightBrush));

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public static readonly DependencyProperty OverlayTextProperty =
        DependencyProperty.Register(
            nameof(OverlayText),
            typeof(string),
            typeof(AxisBar),
            new PropertyMetadata(string.Empty));

    public string OverlayText
    {
        get => (string)GetValue(OverlayTextProperty);
        set => SetValue(OverlayTextProperty, value);
    }

    public static readonly DependencyProperty OverlayVisibilityProperty =
        DependencyProperty.Register(
            nameof(OverlayVisibility),
            typeof(Visibility),
            typeof(AxisBar),
            new PropertyMetadata(Visibility.Collapsed));

    public Visibility OverlayVisibility
    {
        get => (Visibility)GetValue(OverlayVisibilityProperty);
        set => SetValue(OverlayVisibilityProperty, value);
    }

    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(
            nameof(ShowLabels),
            typeof(bool),
            typeof(AxisBar),
            new PropertyMetadata(true));

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public static readonly DependencyProperty BarWidthProperty =
        DependencyProperty.Register(
            nameof(BarWidth),
            typeof(double),
            typeof(AxisBar),
            new PropertyMetadata(260.0));

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    // ===== Detent markers =====

    public static readonly DependencyProperty ShowDetentMarkersProperty =
        DependencyProperty.Register(
            nameof(ShowDetentMarkers),
            typeof(bool),
            typeof(AxisBar),
            new PropertyMetadata(false, OnShowDetentMarkersChanged));

    public bool ShowDetentMarkers
    {
        get => (bool)GetValue(ShowDetentMarkersProperty);
        set => SetValue(ShowDetentMarkersProperty, value);
    }

    private static void OnShowDetentMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AxisBar b)
            b.SetValue(ShowDetentMarkersVisibilityProperty, (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed);
    }

    public static readonly DependencyProperty ShowDetentMarkersVisibilityProperty =
        DependencyProperty.Register(
            nameof(ShowDetentMarkersVisibility),
            typeof(Visibility),
            typeof(AxisBar),
            new PropertyMetadata(Visibility.Collapsed));

    public Visibility ShowDetentMarkersVisibility
    {
        get => (Visibility)GetValue(ShowDetentMarkersVisibilityProperty);
        private set => SetValue(ShowDetentMarkersVisibilityProperty, value);
    }

    // Fractions are in DISPLAY SPACE for throttle, which equals detent/65535 (works regardless of invert).
    public static readonly DependencyProperty IdleDetentFractionProperty =
        DependencyProperty.Register(
            nameof(IdleDetentFraction),
            typeof(double),
            typeof(AxisBar),
            new PropertyMetadata(0.0));

    public double IdleDetentFraction
    {
        get => (double)GetValue(IdleDetentFractionProperty);
        set => SetValue(IdleDetentFractionProperty, value);
    }

    public static readonly DependencyProperty AbDetentFractionProperty =
        DependencyProperty.Register(
            nameof(AbDetentFraction),
            typeof(double),
            typeof(AxisBar),
            new PropertyMetadata(1.0));

    public double AbDetentFraction
    {
        get => (double)GetValue(AbDetentFractionProperty);
        set => SetValue(AbDetentFractionProperty, value);
    }

    public static readonly DependencyProperty IdleDetentBrushProperty =
        DependencyProperty.Register(
            nameof(IdleDetentBrush),
            typeof(Brush),
            typeof(AxisBar),
            new PropertyMetadata(Brushes.Red));

    public Brush IdleDetentBrush
    {
        get => (Brush)GetValue(IdleDetentBrushProperty);
        set => SetValue(IdleDetentBrushProperty, value);
    }

    public static readonly DependencyProperty AbDetentBrushProperty =
        DependencyProperty.Register(
            nameof(AbDetentBrush),
            typeof(Brush),
            typeof(AxisBar),
            new PropertyMetadata(Brushes.LimeGreen));

    public Brush AbDetentBrush
    {
        get => (Brush)GetValue(AbDetentBrushProperty);
        set => SetValue(AbDetentBrushProperty, value);
    }
}