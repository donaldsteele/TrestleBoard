using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CoreModel = TrestleBoard.Core.Model;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The "Adjust…" panel behind the one big Fix photo button (PLAN.md §9): three large sliders that
/// apply live, plus turn and reset. Each drag lands as one undo step because
/// <c>SetImageRecipeCommand</c> merges its burst.
/// </summary>
public sealed class PhotoAdjustWindow : Window
{
    /// <summary>
    /// The most any one edge may be trimmed. Half the picture is already a drastic crop, and a
    /// slider that can reach 100% can leave the user with nothing on the page and no idea why.
    /// </summary>
    private const double MaxTrimFraction = 0.45;

    private readonly PhotoController _photos;
    private readonly string _blockId;
    private readonly Slider _brightness;
    private readonly Slider _contrast;
    private readonly Slider _saturation;
    private readonly Slider _trimLeft;
    private readonly Slider _trimTop;
    private readonly Slider _trimRight;
    private readonly Slider _trimBottom;
    private readonly CheckBox _autoLevels;
    private bool _suppress;

    public PhotoAdjustWindow(PhotoController photos, string blockId, bool startOnTrim = false)
    {
        _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        _blockId = blockId;

        Title = "Adjust the picture";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        CoreModel.ImageFrame? frame = photos.GetPhoto(blockId);
        _brightness = BuildSlider("Brighter or darker", frame?.Recipe.Brightness ?? 0f);
        _contrast = BuildSlider("More or less contrast", frame?.Recipe.Contrast ?? 0f);
        _saturation = BuildSlider("More or less colour", frame?.Recipe.Saturation ?? 0f);

        // M18: "Trim the edges…". Four named edges rather than a freeform drag-crop — dragging a
        // corner handle over a picture is exactly the fine-motor task PLAN.md §6 exists to avoid,
        // and every one of these is reachable with the arrow keys.
        CoreModel.RectPt? crop = frame?.Recipe.CropNormalized;
        _trimLeft = BuildTrimSlider("Trim from the left", crop?.X ?? 0f);
        _trimTop = BuildTrimSlider("Trim from the top", crop?.Y ?? 0f);
        _trimRight = BuildTrimSlider("Trim from the right", crop is { } r ? 1f - r.X - r.Width : 0f);
        _trimBottom = BuildTrimSlider("Trim from the bottom", crop is { } b ? 1f - b.Y - b.Height : 0f);

        _autoLevels = new CheckBox
        {
            Content = "Brighten and balance it automatically",
            FontSize = 16,
            MinHeight = 44,
            IsChecked = frame?.Recipe.AutoLevels ?? false,
        };
        Avalonia.Automation.AutomationProperties.SetName(
            _autoLevels, "Brighten and balance the picture automatically");
        _autoLevels.IsCheckedChanged += (_, _) =>
        {
            if (!_suppress)
            {
                _photos.SetAutoLevels(_blockId, _autoLevels.IsChecked == true);
            }
        };

        var turn = new Button { Content = "Turn a quarter", FontSize = 18, MinHeight = 44, MinWidth = 180 };
        turn.Click += (_, _) => _photos.Rotate(_blockId, 1);
        Avalonia.Automation.AutomationProperties.SetName(turn, "Turn the picture a quarter turn");

        var reset = new Button { Content = "Start over", FontSize = 18, MinHeight = 44, MinWidth = 140 };
        reset.Click += (_, _) =>
        {
            _photos.ResetPhoto(_blockId);
            LoadFromDocument();
        };
        Avalonia.Automation.AutomationProperties.SetName(reset, "Undo all changes to this picture");

        var done = new Button
        {
            Content = "Done",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsDefault = true,
        };
        done.Click += (_, _) => Close();
        Avalonia.Automation.AutomationProperties.SetName(done, "Done, close this window");

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Drag a slider and watch the page. Nothing here changes the original "
                        + "picture — you can always press Start over.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                },
                Labelled("Brighter or darker", _brightness),
                Labelled("More or less contrast", _contrast),
                Labelled("More or less colour", _saturation),
                _autoLevels,
                new TextBlock
                {
                    Text = "Trim the edges",
                    FontSize = 17,
                    FontWeight = FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 8, 0, 0),
                },
                new TextBlock
                {
                    Text = "These take the edges off the picture on the page. The picture in your "
                        + "file is never changed, so you can put an edge back at any time.",
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                },
                Labelled("Trim from the left", _trimLeft),
                Labelled("Trim from the top", _trimTop),
                Labelled("Trim from the right", _trimRight),
                Labelled("Trim from the bottom", _trimBottom),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { turn, reset },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { done },
                },
            },
        };

        // "Trim the edges…" and "Adjust the picture…" open the same window; the difference is where
        // the keyboard lands, so the command the user chose is the one they are standing in.
        Opened += (_, _) =>
        {
            if (startOnTrim)
            {
                _trimLeft.Focus();
            }
        };
    }

    private static StackPanel Labelled(string label, Slider slider) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 16 },
            slider,
        },
    };

    /// <summary>
    /// One edge control, in fractions of the picture. Zero is "leave this edge alone", which is
    /// where all four start, so an untouched picture is untouched (M18).
    /// </summary>
    private Slider BuildTrimSlider(string name, float value)
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = MaxTrimFraction,
            Value = Math.Clamp(value, 0f, (float)MaxTrimFraction),
            Width = 420,
            MinHeight = 44,
            TickFrequency = 0.05,
            SmallChange = 0.01,
            LargeChange = 0.05,
            IsSnapToTickEnabled = false,
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, name);
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_suppress)
            {
                ApplyTrim();
            }
        };
        return slider;
    }

    private Slider BuildSlider(string name, float value)
    {
        var slider = new Slider
        {
            Minimum = -1,
            Maximum = 1,
            Value = value,
            Width = 420,
            MinHeight = 44,
            TickFrequency = 0.1,
            SmallChange = 0.05,
            LargeChange = 0.2,
            IsSnapToTickEnabled = false,
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, name);
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_suppress)
            {
                Apply();
            }
        };
        return slider;
    }

    private void Apply() => _photos.SetAdjustments(
        _blockId,
        (float)_brightness.Value,
        (float)_contrast.Value,
        (float)_saturation.Value);

    /// <summary>
    /// The four edges as one crop rectangle. The controller clamps it, so two edges dragged past
    /// each other cannot produce a picture of negative width.
    /// </summary>
    private void ApplyTrim() => _photos.SetCrop(
        _blockId,
        new Imaging.NormalizedRect(
            (float)_trimLeft.Value,
            (float)_trimTop.Value,
            (float)Math.Max(0.05, 1d - _trimLeft.Value - _trimRight.Value),
            (float)Math.Max(0.05, 1d - _trimTop.Value - _trimBottom.Value)));

    private void LoadFromDocument()
    {
        if (_photos.GetPhoto(_blockId) is not { } frame)
        {
            return;
        }

        _suppress = true;
        _brightness.Value = frame.Recipe.Brightness;
        _contrast.Value = frame.Recipe.Contrast;
        _saturation.Value = frame.Recipe.Saturation;
        _autoLevels.IsChecked = frame.Recipe.AutoLevels;

        CoreModel.RectPt? crop = frame.Recipe.CropNormalized;
        _trimLeft.Value = crop?.X ?? 0f;
        _trimTop.Value = crop?.Y ?? 0f;
        _trimRight.Value = crop is { } r ? Math.Clamp(1d - r.X - r.Width, 0d, MaxTrimFraction) : 0d;
        _trimBottom.Value = crop is { } b ? Math.Clamp(1d - b.Y - b.Height, 0d, MaxTrimFraction) : 0d;
        _suppress = false;
    }
}
