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
    private readonly PhotoController _photos;
    private readonly string _blockId;
    private readonly Slider _brightness;
    private readonly Slider _contrast;
    private readonly Slider _saturation;
    private bool _suppress;

    public PhotoAdjustWindow(PhotoController photos, string blockId)
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
        _suppress = false;
    }
}
