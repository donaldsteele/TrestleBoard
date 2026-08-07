using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CoreModel = TrestleBoard.Core.Model;
using TrestleBoard.Editing;
using TrestleBoard.App.Theme;

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

    /// <summary>
    /// Where the undo stack stood when this window opened, so "Cancel" can put the picture back
    /// exactly as it was (M27, review §14.3).
    ///
    /// <para>Every slider here applies to the document as it moves — that is the point of the
    /// window, the user watches the page change — but until now the only way out was "Done", which
    /// kept everything. There was no Cancel, no <c>IsCancel</c>, and so Esc did nothing at all: a
    /// window with no way back, in an app for people who are learning what the sliders do by
    /// moving them. "Start over" was the nearest thing and it is not the same promise; it resets
    /// the picture to the original file, discarding adjustments made before this window opened.
    /// </para>
    /// </summary>
    private readonly int _undoMark;

    public PhotoAdjustWindow(PhotoController photos, string blockId, bool startOnTrim = false)
    {
        _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        _blockId = blockId;
        _undoMark = photos.UndoDepth;

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

        // M27: which way it turns, said out loud. "Turn a quarter" left the user to press it and
        // find out, and four presses to undo a wrong guess is four undo steps (review §14.4).
        var turn = new Button { Content = "Turn it right ↻", FontSize = 18, MinHeight = 44, MinWidth = 180 };
        turn.Action();
        turn.Click += (_, _) => _photos.Rotate(_blockId, 1);
        Avalonia.Automation.AutomationProperties.SetName(
            turn, "Turn the picture a quarter turn to the right, clockwise");

        var reset = new Button { Content = "Start over", FontSize = 18, MinHeight = 44, MinWidth = 140 };
        reset.Action();
        reset.Click += (_, _) =>
        {
            _photos.ResetPhoto(_blockId);
            LoadFromDocument();
        };
        Avalonia.Automation.AutomationProperties.SetName(reset, "Undo all changes to this picture");

        // M22: re-centring an already-sized crop is a separate step from resizing one — opening it
        // from here, rather than only from the action panel, keeps the two discoverable together.
        var position = new Button { Content = "Position…", FontSize = 18, MinHeight = 44, MinWidth = 140 };
        position.Action();
        position.Click += async (_, _) =>
        {
            var window = new PositionPhotoWindow(_photos, _blockId);
            await window.ShowDialog(this);
            LoadFromDocument();
        };
        Avalonia.Automation.AutomationProperties.SetName(position, "Position the picture in its frame");

        var done = new Button
        {
            Content = "Done",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsDefault = true,
        };
        done.Click += (_, _) => Close();
        Avalonia.Automation.AutomationProperties.SetName(done, "Done, keep these changes");

        var cancel = new Button
        {
            Content = "Cancel — change nothing",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsCancel = true,
        };
        cancel.Action();
        cancel.Click += (_, _) => CancelAndClose();
        Avalonia.Automation.AutomationProperties.SetName(
            cancel, "Cancel, put the picture back as it was before this window opened");

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
                    FontSize = 16,
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
                    Children = { turn, reset, position },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, done },
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

    /// <summary>
    /// Puts the document back to where it stood when this window opened, then closes.
    ///
    /// <para>Unwinding the real undo stack rather than restoring a remembered recipe: the sliders
    /// commit through the same commands as everything else, so the undo stack already IS the record
    /// of what this window did, and replaying a snapshot would be a second, parallel notion of
    /// "before" that could disagree with it.</para>
    /// </summary>
    private void CancelAndClose()
    {
        _photos.UndoBackTo(_undoMark);
        Close();
    }

}
