using System.Globalization;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using RectPt = TrestleBoard.Core.Model.RectPt;
using SizePt = TrestleBoard.Core.Model.SizePt;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Size and place it exactly…" (PLAN.md §11 M94).
///
/// <para><b>This is the accessible route to geometry.</b> Until now a frame could be placed only by
/// dragging it with the mouse or pressing an arrow key a point at a time. A long, precise drag is
/// exactly the fine-motor task §6 exists to avoid, and somebody with a tremor could not put a box
/// where last month's issue had it. Four numbers can be typed, checked, and got right.</para>
///
/// <para><b>Inches, not points.</b> The model is in points because that is what the layout engine
/// and the PDF speak, and the committee measures in inches because that is what a ruler says. The
/// conversion happens here, at the edge, and nowhere else.</para>
///
/// <para><b>A bad number is refused with a sentence, not swallowed.</b> Typing a word where a
/// measurement goes is an ordinary slip; the window says which box is wrong and keeps everything
/// else the user typed.</para>
/// </summary>
public sealed class PositionAndSizeDialog : Window
{
    /// <summary>Points to the inch — the definition, not a preference.</summary>
    private const float PointsPerInch = 72f;

    /// <summary>
    /// The smallest frame worth having. Below this a box is invisible and unclickable, and the
    /// user's next telephone call is "it has disappeared".
    /// </summary>
    private const float MinimumInches = 0.1f;

    private readonly TextBox _left;
    private readonly TextBox _top;
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly TextBlock _warning;

    public PositionAndSizeDialog(RectPt current, SizePt page)
    {
        Title = "Size and place it exactly";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _left = Measurement(current.X, "How far in from the left, in inches");
        _top = Measurement(current.Y, "How far down from the top, in inches");
        _width = Measurement(current.Width, "How wide, in inches");
        _height = Measurement(current.Height, "How tall, in inches");

        _warning = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            IsVisible = false,
        }.Token(TextBlock.ForegroundProperty, Tokens.Warning);

        var apply = new Button
        {
            Content = "Put it there",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Put it there");
        apply.Click += (_, _) =>
        {
            if (Read() is not { } wanted)
            {
                return;
            }

            Wanted = wanted;
            Confirmed = true;
            Close();
        };

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsCancel = true,
        };
        cancel.Action();
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel, change nothing");
        cancel.Click += (_, _) => Close();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto"),
            RowDefinitions = new RowDefinitions("Auto,10,Auto,10,Auto,10,Auto"),
        };
        AddRow(grid, 0, "How far in from the left", _left);
        AddRow(grid, 2, "How far down from the top", _top);
        AddRow(grid, 4, "How wide", _width);
        AddRow(grid, 6, "How tall", _height);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Where should it go, and how big should it be?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                new TextBlock
                {
                    Text = $"All four are in inches. This page is "
                        + $"{Inches(page.Width)} by {Inches(page.Height)} inches.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                grid,
                _warning,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, apply },
                },
            },
        };
    }

    /// <summary>False when the window was cancelled — nothing is applied then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>What the user typed, in points, once they pressed the button.</summary>
    public RectPt Wanted { get; private set; }

    private static string Inches(float points) =>
        (points / PointsPerInch).ToString("0.##", CultureInfo.CurrentCulture);

    private static void AddRow(Grid grid, int row, string label, Control box)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);

        Grid.SetRow(box, row);
        Grid.SetColumn(box, 2);

        grid.Children.Add(text);
        grid.Children.Add(box);
    }

    private static TextBox Measurement(float points, string automationName)
    {
        var box = new TextBox
        {
            Text = Inches(points),
            FontSize = 18,
            MinHeight = 44,
            Width = 120,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }

    /// <summary>
    /// The four boxes as one rectangle in points, or null when one of them cannot be read — in
    /// which case the window has already said which, and stays open with everything else intact.
    /// </summary>
    private RectPt? Read()
    {
        if (Number(_left, "how far in from the left") is not { } x
            || Number(_top, "how far down from the top") is not { } y
            || Number(_width, "how wide") is not { } width
            || Number(_height, "how tall") is not { } height)
        {
            return null;
        }

        if (width < MinimumInches || height < MinimumInches)
        {
            Warn($"A box has to be at least {MinimumInches.ToString("0.##", CultureInfo.CurrentCulture)} "
                + "of an inch across, or there would be nothing left to see or take hold of.");
            return null;
        }

        _warning.IsVisible = false;
        return new RectPt(
            x * PointsPerInch, y * PointsPerInch, width * PointsPerInch, height * PointsPerInch);
    }

    private float? Number(TextBox box, string which)
    {
        // The user's own decimal separator, because the number in the box was written with it.
        if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value))
        {
            Warn($"⚠ “{box.Text}” is not a measurement. Please type {which} as a number, "
                + "like 1.5 for an inch and a half.");
            box.Focus();
            return null;
        }

        if (value < 0f)
        {
            Warn($"⚠ The measurement for {which} cannot be less than nothing.");
            box.Focus();
            return null;
        }

        return value;
    }

    private void Warn(string message)
    {
        _warning.Text = message;
        _warning.IsVisible = true;
    }
}
