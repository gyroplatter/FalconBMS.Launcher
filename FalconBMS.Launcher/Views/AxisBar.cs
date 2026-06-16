using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FalconBMS.Launcher.Views;

/// <summary>
/// Lightweight axis bar renderer used anywhere we need live axis feedback.
/// 
/// Important:
/// - This control only draws the current value.
/// - It does not poll DirectInput.
/// - It does not detect movement.
/// - It does not know anything about assignment rules.
/// 
/// Keeping this visual-only lets the assignment window and the keymapping grid share
/// one fast rendering path without duplicating ProgressBar templates everywhere.
/// </summary>
public sealed class AxisBar : Control
{
    private const double DefaultHeight = 14.0;

    private double _lastInvalidatedValue = 0.5;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                0.5,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnValueChanged,
                CoerceUnitInterval));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowDetentsProperty =
        DependencyProperty.Register(
            nameof(ShowDetents),
            typeof(bool),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IdleDetentFractionProperty =
        DependencyProperty.Register(
            nameof(IdleDetentFraction),
            typeof(double),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                CoerceUnitInterval));

    public static readonly DependencyProperty AfterburnerDetentFractionProperty =
        DependencyProperty.Register(
            nameof(AfterburnerDetentFraction),
            typeof(double),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                CoerceUnitInterval));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(
                "",
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UpdateThresholdProperty =
        DependencyProperty.Register(
            nameof(UpdateThreshold),
            typeof(double),
            typeof(AxisBar),
            new FrameworkPropertyMetadata(0.003));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool ShowDetents
    {
        get => (bool)GetValue(ShowDetentsProperty);
        set => SetValue(ShowDetentsProperty, value);
    }

    public double IdleDetentFraction
    {
        get => (double)GetValue(IdleDetentFractionProperty);
        set => SetValue(IdleDetentFractionProperty, value);
    }

    public double AfterburnerDetentFraction
    {
        get => (double)GetValue(AfterburnerDetentFractionProperty);
        set => SetValue(AfterburnerDetentFractionProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? "");
    }

    /// <summary>
    /// Small visual redraw threshold used to ignore tiny joystick jitter.
    /// The view model may still receive frequent polling updates, but the bar does
    /// not need to repaint for every single 1-count DirectInput wobble.
    /// </summary>
    public double UpdateThreshold
    {
        get => (double)GetValue(UpdateThresholdProperty);
        set => SetValue(UpdateThresholdProperty, value);
    }

    static AxisBar()
    {
        FocusableProperty.OverrideMetadata(
            typeof(AxisBar),
            new FrameworkPropertyMetadata(false));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsInfinity(constraint.Width)
            ? 100.0
            : constraint.Width;

        double height = double.IsInfinity(constraint.Height)
            ? DefaultHeight
            : Math.Max(DefaultHeight, constraint.Height);

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = RenderSize.Width;
        double height = RenderSize.Height;

        if (width <= 0 || height <= 0)
            return;

        Brush backgroundBrush = GetResourceBrush("AppWindowBackgroundBrush", Brushes.Transparent);
        Brush borderBrush = GetResourceBrush("AppBorderBrush", Brushes.Gray);
        Brush fillBrush = GetResourceBrush("AppAccentBrush", Brushes.DodgerBlue);
        Brush textOnEmptyBrush = GetResourceBrush("AppForegroundBrush", Brushes.Black);
        Brush textOnFillBrush = Brushes.White;

        var outerRect = new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1));
        var innerRect = new Rect(1.0, 1.0, Math.Max(0, width - 2), Math.Max(0, height - 2));

        drawingContext.DrawRoundedRectangle(
            backgroundBrush,
            new Pen(borderBrush, 1),
            outerRect,
            3,
            3);

        // The fill is clipped to the rounded bar shape so a full-right value does not bleed outside the border.
        if (IsActive)
        {
            double fillWidth = Math.Max(0, innerRect.Width * Value);

            drawingContext.PushClip(new RectangleGeometry(outerRect, 3, 3));
            drawingContext.DrawRectangle(
                fillBrush,
                null,
                new Rect(innerRect.X, innerRect.Y, fillWidth, innerRect.Height));
            drawingContext.Pop();
        }

        if (ShowDetents)
        {
            DrawDetentMarker(drawingContext, Brushes.Red, IdleDetentFraction, width, height);
            DrawDetentMarker(drawingContext, Brushes.LimeGreen, AfterburnerDetentFraction, width, height);
        }

        double fillWidthForText = IsActive
            ? Math.Max(0, innerRect.Width * Value)
            : 0.0;

                DrawText(
                    drawingContext,
                    textOnEmptyBrush,
                    textOnFillBrush,
                    width,
                    height,
                    innerRect.X,
                    fillWidthForText);
    }

    private static object CoerceUnitInterval(DependencyObject dependencyObject, object baseValue)
    {
        double value = baseValue is double numericValue ? numericValue : 0.0;

        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;

        return Math.Max(0.0, Math.Min(1.0, value));
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var axisBar = (AxisBar)dependencyObject;
        double newValue = (double)e.NewValue;

        // Avoid repainting the axis bar for tiny jitter-only changes.
        // The stored dependency property still updates; this only suppresses needless visual invalidation.
        if (Math.Abs(newValue - axisBar._lastInvalidatedValue) < axisBar.UpdateThreshold)
            return;

        axisBar._lastInvalidatedValue = newValue;
        axisBar.InvalidateVisual();
    }

    private static void DrawDetentMarker(
        DrawingContext drawingContext,
        Brush markerBrush,
        double fraction,
        double width,
        double height)
    {
        double x = Math.Max(1.0, Math.Min(width - 2.0, width * fraction));

        drawingContext.DrawLine(
            new Pen(markerBrush, 2),
            new Point(x, 1),
            new Point(x, Math.Max(1, height - 1)));
    }

    private void DrawText(
        DrawingContext drawingContext,
        Brush textOnEmptyBrush,
        Brush textOnFillBrush,
        double width,
        double height,
        double fillX,
        double fillWidth)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double fontSize = FontSize > 0 ? FontSize : 11.0;

        var typeface = new Typeface(
            FontFamily,
            FontStyle,
            FontWeight,
            FontStretch);

        var emptyText = new FormattedText(
            Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textOnEmptyBrush,
            pixelsPerDip);

        var fillText = new FormattedText(
            Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textOnFillBrush,
            pixelsPerDip);

        double textX = Math.Max(
            2,
            (width - emptyText.Width) / 2.0);

        double textY = Math.Max(
            0,
            (height - emptyText.Height) / 2.0);

        var fullClip = new Rect(
            0,
            0,
            width,
            height);

        var filledClip = new Rect(
            fillX,
            0,
            Math.Max(0, fillWidth),
            height);

        drawingContext.PushClip(new RectangleGeometry(fullClip));

        drawingContext.DrawText(
            emptyText,
            new Point(textX, textY));

        if (filledClip.Width > 0)
        {
            drawingContext.PushClip(new RectangleGeometry(filledClip));

            drawingContext.DrawText(
                fillText,
                new Point(textX, textY));

            drawingContext.Pop();
        }

        drawingContext.Pop();
    }

    private Brush GetResourceBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }
}