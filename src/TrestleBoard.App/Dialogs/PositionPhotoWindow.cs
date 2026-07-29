using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing;
using TrestleBoard.Imaging;
using CoreModel = TrestleBoard.Core.Model;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// M22: "Position the picture in its frame…" — separates re-centring the already-sized crop from
/// resizing it. A pannable preview of the WHOLE decoded picture sits behind a crop-window overlay
/// locked to the frame's current aspect ratio and size; only the overlay's centre moves. Nothing
/// commits until Apply, and that commit is one undo step — <see cref="PhotoController.SetPosition"/>.
///
/// <para>Pan reaches every user two ways: drag the picture with the pointer, or the arrow keys —
/// PLAN.md M22 calls arrow-key pan the one control that must never be cut, since it is the only
/// keyboard path to recentring. Zoom is a stepped ladder (<see cref="ZoomLadder"/>), not a slider,
/// for the same fine-motor reason M14's font-size stepper is a stepper.</para>
/// </summary>
public sealed class PositionPhotoWindow : Window
{
    private const double ViewportSize = 420;
    private const float PanStep = 0.02f;

    private readonly PhotoController _photos;
    private readonly string _blockId;
    private readonly DecodedImage? _decoded;
    private readonly Bitmap? _preview;
    private readonly Image _image;
    private readonly Avalonia.Controls.Canvas _imageHost;
    private readonly Border _overlay;
    private readonly Panel _stage;
    private readonly TextBlock _zoomLabel;
    private readonly TextBlock _status;
    private float _centerX = 0.5f;
    private float _centerY = 0.5f;
    private float _zoom = ZoomLadder.Smallest;
    private bool _dragging;
    private Point _dragStart;
    private float _dragStartCenterX;
    private float _dragStartCenterY;

    public bool Applied { get; private set; }

    public PositionPhotoWindow(PhotoController photos, string blockId)
    {
        _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        _blockId = blockId;

        Title = "Position the picture";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        CoreModel.ImageFrame? frame = photos.GetPhoto(blockId);
        CoreModel.RectPt? crop = frame?.Recipe.CropNormalized;
        if (crop is { } c)
        {
            _centerX = c.X + (c.Width / 2f);
            _centerY = c.Y + (c.Height / 2f);
        }

        _decoded = photos.GetDecodedImage(blockId);
        _preview = _decoded is null ? null : ToBitmap(_decoded.Bitmap);

        _image = new Image
        {
            Source = _preview,
            Stretch = Stretch.Fill,
        };

        _imageHost = new Avalonia.Controls.Canvas
        {
            Width = ViewportSize,
            Height = ViewportSize,
            Children = { _image },
        };

        var viewport = new Border
        {
            Width = ViewportSize,
            Height = ViewportSize,
            ClipToBounds = true,
            Child = _imageHost,
        }.Token(Border.BorderBrushProperty, Tokens.ChromeBorder);
        viewport.BorderThickness = new Thickness(1);

        // The crop-window frame is purely visual — it never has to be the hit-test target, because
        // the picture behind it fills this whole stage at every zoom level, so the stage itself can
        // own drag/keyboard panning without needing a background of its own.
        _overlay = new Border
        {
            BorderThickness = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.Token(Border.BorderBrushProperty, Tokens.Focus);

        var stage = new Panel
        {
            Width = ViewportSize,
            Height = ViewportSize,
            Focusable = true,
            Children = { viewport, _overlay },
        }.Token(Panel.BackgroundProperty, Tokens.ChromeBackground);
        stage.KeyDown += OnOverlayKeyDown;
        stage.PointerPressed += OnPointerPressed;
        stage.PointerMoved += OnPointerMoved;
        stage.PointerReleased += OnPointerReleased;
        _stage = stage;

        var zoomIn = new Button { Content = "Zoom in", FontSize = 16, MinHeight = 44, MinWidth = 110 };
        var zoomOut = new Button { Content = "Zoom out", FontSize = 16, MinHeight = 44, MinWidth = 110 };
        _zoomLabel = new TextBlock { FontSize = 16, VerticalAlignment = VerticalAlignment.Center, MinWidth = 60 };
        zoomIn.Click += (_, _) => StepZoom(+1);
        zoomOut.Click += (_, _) => StepZoom(-1);
        AutomationProperties.SetName(zoomIn, "Zoom in, for finer panning");
        AutomationProperties.SetName(zoomOut, "Zoom out");

        _status = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Text = _decoded is null
                ? "That picture could not be read, so it cannot be repositioned."
                : "Drag the picture, or use the arrow keys, to slide it inside the blue crop window. "
                    + "The crop stays the same size — this only moves it.",
        };

        var apply = new Button
        {
            Content = "Apply",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsDefault = true,
            IsEnabled = _decoded is not null,
        };
        apply.Click += (_, _) => ApplyAndClose();
        AutomationProperties.SetName(apply, "Apply this position");

        var cancel = new Button { Content = "Cancel", FontSize = 18, MinHeight = 44, MinWidth = 120, IsCancel = true };
        cancel.Click += (_, _) => Close();
        AutomationProperties.SetName(cancel, "Cancel, leave the picture where it was");

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                _status,
                stage,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { zoomOut, _zoomLabel, zoomIn },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, apply },
                },
            },
        };

        RefreshPreview();
        Opened += (_, _) => _stage.Focus();
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Pan(-PanStep / _zoom, 0f); e.Handled = true; break;
            case Key.Right: Pan(PanStep / _zoom, 0f); e.Handled = true; break;
            case Key.Up: Pan(0f, -PanStep / _zoom); e.Handled = true; break;
            case Key.Down: Pan(0f, PanStep / _zoom); e.Handled = true; break;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _dragStart = e.GetPosition(_stage);
        _dragStartCenterX = _centerX;
        _dragStartCenterY = _centerY;
        e.Pointer.Capture(_stage);
        _stage.Focus();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _decoded is null)
        {
            return;
        }

        Point now = e.GetPosition(_stage);
        double dispWidth = ViewportSize * _zoom;
        double dispHeight = dispWidth / _decoded.Aspect;
        float dx = (float)((now.X - _dragStart.X) / dispWidth);
        float dy = (float)((now.Y - _dragStart.Y) / dispHeight);
        SetCenter(_dragStartCenterX - dx, _dragStartCenterY - dy);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void Pan(float dx, float dy) => SetCenter(_centerX + dx, _centerY + dy);

    private void SetCenter(float x, float y)
    {
        _centerX = Math.Clamp(x, 0f, 1f);
        _centerY = Math.Clamp(y, 0f, 1f);
        RefreshPreview();
    }

    private void StepZoom(int direction)
    {
        if (ZoomLadder.Step(_zoom, direction) is { } next)
        {
            _zoom = next;
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        _zoomLabel.Text = ZoomLadder.Label(_zoom);

        if (_decoded is null)
        {
            return;
        }

        double dispWidth = ViewportSize * _zoom;
        double dispHeight = dispWidth / _decoded.Aspect;
        _image.Width = dispWidth;
        _image.Height = dispHeight;
        Avalonia.Controls.Canvas.SetLeft(_image, (ViewportSize / 2) - (_centerX * dispWidth));
        Avalonia.Controls.Canvas.SetTop(_image, (ViewportSize / 2) - (_centerY * dispHeight));

        NormalizedRect? proposal = _photos.ProposePosition(_blockId, _centerX, _centerY);
        if (proposal is { } crop)
        {
            _overlay.Width = crop.Width * dispWidth;
            _overlay.Height = crop.Height * dispHeight;
        }

        AutomationProperties.SetName(
            _stage,
            $"Crop window, {PercentAcross()} across, {PercentDown()} down, zoom {ZoomLadder.Label(_zoom)}");
    }

    private string PercentAcross() => $"{(int)MathF.Round(_centerX * 100f)}%";

    private string PercentDown() => $"{(int)MathF.Round(_centerY * 100f)}%";

    private void ApplyAndClose()
    {
        if (_photos.ProposePosition(_blockId, _centerX, _centerY) is { } crop)
        {
            _photos.SetPosition(_blockId, crop);
            Applied = true;
        }

        Close();
    }

    private static Bitmap ToBitmap(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("PNG encode failed for the position preview.");
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
