using System.Globalization;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using TrestleBoard.Editing;
using SizePt = TrestleBoard.Core.Model.SizePt;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "The paper and the margins…" (PLAN.md §11 M97).
///
/// <para><b>Five fields with eight readers and no writer.</b> `PageMaster.Size` and the four
/// margins are read by the layout engine, the snap engine, the renderer and three placement paths,
/// and until this window nothing in the application could change any of them. US Letter was
/// hardwired by a property default and the string "A4" did not appear anywhere in the source.</para>
///
/// <para><b>Paper is named, margins are typed.</b> There are three papers a lodge newsletter is
/// ever printed on and naming them is plainer than four numbers; margins are a genuine measurement
/// with no small set of right answers, so they are four boxes in inches — the same shape, and the
/// same validation, as M94's position window.</para>
///
/// <para><b>Landscape is a tick box, not a fourth paper.</b> It is the same sheet turned round, and
/// listing six papers to say three would be the app making the user do arithmetic.</para>
/// </summary>
public sealed class PageSetupDialog : Window
{
    private const float PointsPerInch = 72f;

    /// <summary>
    /// The least paper the app will accept. Below this there is no room for a margin, let alone a
    /// frame, and every downstream calculation starts returning negative widths.
    /// </summary>
    private const float MinimumMarginSumInches = 0.5f;

    private readonly ComboBox _paper;
    private readonly CheckBox _landscape;
    private readonly TextBox _left;
    private readonly TextBox _top;
    private readonly TextBox _right;
    private readonly TextBox _bottom;
    private readonly TextBlock _warning;

    public PageSetupDialog(SizePt size, float leftPt, float topPt, float rightPt, float bottomPt)
    {
        Title = "The paper and the margins";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        bool landscape = size.Width > size.Height;
        SizePt upright = landscape ? new SizePt(size.Height, size.Width) : size;

        _paper = new ComboBox { FontSize = 17, MinHeight = 44, Width = 300 };
        foreach ((string name, SizePt _) in PageFlowController.Papers)
        {
            _paper.Items.Add(name);
        }

        // Nothing is chosen when the newsletter is on a paper the app does not offer — which a file
        // written by hand legitimately can be. Showing the nearest would be untrue about their file.
        _paper.SelectedIndex = -1;
        for (int i = 0; i < PageFlowController.Papers.Count; i++)
        {
            SizePt candidate = PageFlowController.Papers[i].Size;
            if (Math.Abs(candidate.Width - upright.Width) < 1f
                && Math.Abs(candidate.Height - upright.Height) < 1f)
            {
                _paper.SelectedIndex = i;
                break;
            }
        }

        Avalonia.Automation.AutomationProperties.SetName(_paper, "What size paper");

        _landscape = new CheckBox
        {
            Content = "Turn the paper on its side (landscape)",
            FontSize = 17,
            MinHeight = 44,
            IsChecked = landscape,
        };
        Avalonia.Automation.AutomationProperties.SetName(
            _landscape, "Turn the paper on its side, landscape");

        _left = Measurement(leftPt, "Margin on the left, in inches");
        _top = Measurement(topPt, "Margin at the top, in inches");
        _right = Measurement(rightPt, "Margin on the right, in inches");
        _bottom = Measurement(bottomPt, "Margin at the bottom, in inches");

        _warning = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            IsVisible = false,
        }.Token(TextBlock.ForegroundProperty, Tokens.Warning);

        var apply = new Button
        {
            Content = "Use this paper",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Use this paper");
        apply.Click += (_, _) =>
        {
            if (!Read())
            {
                return;
            }

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

        var margins = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto"),
            RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto,8,Auto"),
        };
        AddRow(margins, 0, "Left", _left);
        AddRow(margins, 2, "Top", _top);
        AddRow(margins, 4, "Right", _right);
        AddRow(margins, 6, "Bottom", _bottom);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "What is this newsletter printed on?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _paper,
                _landscape,
                new TextBlock
                {
                    Text = "How much white space round the edges, in inches?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                margins,
                new TextBlock
                {
                    Text = "This changes every page. If anything ends up hanging off the paper it "
                        + "stays where you put it — nothing is moved for you — and you can press "
                        + "Ctrl+Z to put the paper back.",
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
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

    /// <summary>The chosen paper, turned on its side if that was ticked.</summary>
    public SizePt Size { get; private set; }

    public float LeftPt { get; private set; }

    public float TopPt { get; private set; }

    public float RightPt { get; private set; }

    public float BottomPt { get; private set; }

    private static string Inches(float points) =>
        (points / PointsPerInch).ToString("0.##", CultureInfo.CurrentCulture);

    private static void AddRow(Grid grid, int row, string label, Control box)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 17,
            Width = 70,
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
            Width = 110,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }

    private bool Read()
    {
        if (_paper.SelectedIndex < 0)
        {
            Warn("⚠ Please choose what size paper this newsletter is printed on.");
            return false;
        }

        if (Number(_left, "the left margin") is not { } left
            || Number(_top, "the top margin") is not { } top
            || Number(_right, "the right margin") is not { } right
            || Number(_bottom, "the bottom margin") is not { } bottom)
        {
            return false;
        }

        SizePt paper = PageFlowController.Papers[_paper.SelectedIndex].Size;
        SizePt sized = _landscape.IsChecked == true
            ? new SizePt(paper.Height, paper.Width)
            : paper;

        // Margins that meet in the middle leave no room to write in, and every width calculation
        // downstream starts returning a negative number. Refused with the arithmetic spelled out.
        float acrossInches = sized.Width / PointsPerInch;
        float downInches = sized.Height / PointsPerInch;
        if (left + right > acrossInches - MinimumMarginSumInches
            || top + bottom > downInches - MinimumMarginSumInches)
        {
            Warn("⚠ Those margins leave no room to write in. On this paper the left and right "
                + $"together have to be under {Round(acrossInches - MinimumMarginSumInches)} inches, "
                + $"and the top and bottom under {Round(downInches - MinimumMarginSumInches)}.");
            return false;
        }

        Size = sized;
        LeftPt = left * PointsPerInch;
        TopPt = top * PointsPerInch;
        RightPt = right * PointsPerInch;
        BottomPt = bottom * PointsPerInch;
        _warning.IsVisible = false;
        return true;
    }

    private static string Round(float inches) =>
        inches.ToString("0.#", CultureInfo.CurrentCulture);

    private float? Number(TextBox box, string which)
    {
        if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value))
        {
            Warn($"⚠ “{box.Text}” is not a measurement. Please type {which} as a number, "
                + "like 0.75 for three quarters of an inch.");
            box.Focus();
            return null;
        }

        if (value < 0f)
        {
            Warn($"⚠ {char.ToUpper(which[0], CultureInfo.CurrentCulture)}{which[1..]} "
                + "cannot be less than nothing.");
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
