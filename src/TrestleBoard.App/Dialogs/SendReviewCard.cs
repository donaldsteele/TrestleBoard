using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user said before sending.</summary>
internal enum SendReviewChoice
{
    /// <summary>Send it now. The default answer, and the one a closed window means.</summary>
    SendItAnyway,

    /// <summary>Look at the findings first. Nothing is sent.</summary>
    LookFirst,
}

/// <summary>
/// The last look before the newsletter goes to sixty people (PLAN.md §11 M82).
///
/// <para><b>Offered, never a gate.</b> M51 built the review and hung it on a command of its own,
/// beside "Send it" rather than inside it — so the moment it was most wanted was the one moment it
/// did not appear. It appears now, and "Send it anyway" is the default, is never disabled, and is
/// what closing the window means. A refusal to send is a telephone call, and the app has no
/// business deciding that a newsletter with an unfilled picture frame must not go out.</para>
///
/// <para><b>It says how many, not what.</b> The findings themselves are the review window's to
/// show; this card's job is to let somebody who already knows about them press one button and
/// carry on.</para>
/// </summary>
internal sealed class SendReviewCard : Window
{
    internal SendReviewCard(int questions)
    {
        Title = "Before you send it";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Before you send it");

        var send = new Button
        {
            Content = "Send it anyway",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
            IsCancel = true,
        };
        send.Primary();
        send.Click += (_, _) => Close();
        AutomationProperties.SetName(send, "Send it anyway, without looking first");

        var look = new Button
        {
            Content = "Look at them first",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
        };
        look.Action();
        look.Click += (_, _) =>
        {
            Choice = SendReviewChoice.LookFirst;
            Close();
        };
        AutomationProperties.SetName(look, "Look at the things worth checking first");

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = questions == 1 ? "One thing to look at first" : $"{questions} things to look at first",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "TrestleBoard noticed a few things while looking over the newsletter. "
                        + "You can look at them now, or send it as it is — it is your newsletter.",
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 490,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { look, send },
                },
            },
        };
    }

    /// <summary>What they chose. Closing the window means <see cref="SendReviewChoice.SendItAnyway"/>.</summary>
    internal SendReviewChoice Choice { get; private set; } = SendReviewChoice.SendItAnyway;

    /// <summary>For the tests, which cannot click.</summary>
    internal void ChooseForTest(SendReviewChoice choice) => Choice = choice;
}
