using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using GlyphPath = Avalonia.Controls.Shapes.Path;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace TrestleBoard.App.Theme;

/// <summary>
/// A glyph and its label, together, as one control (PLAN.md §6, §11 M16).
///
/// <para><b>This type exists so that "an icon never appears without its label" is a single assertion
/// instead of a thing every reviewer has to remember.</b> There is no way to construct an
/// <see cref="IconText"/> without label text, and <c>IconTests.AnIconIsNeverTheOnlyContent</c> walks
/// every window looking for a <see cref="Path"/> in a button that has no <see cref="TextBlock"/>
/// beside it. §6 grants no size exemption for this: the audience is elderly, several of them use
/// this application a few times a month, and a picture they have to decode is a picture that
/// costs them time.</para>
///
/// <para><b>Why a <see cref="ContentControl"/>, and why the fill binds to <c>Foreground</c> rather
/// than to a palette token.</b> A <c>DynamicResource</c> fill would paint a dark glyph on the navy
/// primary button, and it would not follow hover, press or disabled either. Deriving from
/// <see cref="ContentControl"/> gives this control an inherited <c>Foreground</c>, which the parent
/// button drives; the glyph binds to that and is correct in every state without being told about
/// any of them. Standalone icons — ones not inside a button — set <c>Foreground</c> from a token.</para>
///
/// <para><b>The layout is a <see cref="DockPanel"/> and must stay one.</b> A horizontal
/// <see cref="StackPanel"/> measures its children with infinite width, so the label would never
/// wrap and the clipping defect fixed on 2026-07-27 would come straight back.</para>
/// </summary>
internal sealed class IconText : ContentControl
{
    /// <summary>
    /// The glyph box, in device-independent pixels before the UI scale. Vector geometry needs no
    /// per-scale handling: the chrome already sits inside the LayoutTransformControls that carry
    /// the 100–200% scale, so this box grows with the text it sits beside.
    /// </summary>
    private const double GlyphSize = 22;

    /// <param name="glyphKey">
    /// A key from <c>Theme/Icons.axaml</c>, or null for a text-only offer — which is most of them,
    /// deliberately (see <see cref="ActionIcons"/>).
    /// </param>
    /// <param name="label">What the control says. Never empty; that is the whole point of the type.</param>
    /// <param name="labelSize">Point size of the label.</param>
    /// <param name="secondary">
    /// A second, quieter line under the label — the keyboard shortcut in the action panel. It goes on
    /// its own line rather than into the label because "Wrap text around this  (Ctrl+Shift+W)" is
    /// thirty-seven characters, and the gesture is what pushed the button to two lines.
    /// </param>
    /// <param name="mutedSecondary">
    /// Whether the second line takes the muted token. False on an accent-filled button, where
    /// muted-on-accent is 1.66:1 — the live-tree contrast walk caught exactly that on the day this
    /// type was written. There, the second line inherits the button's own foreground and the size
    /// difference alone carries the hierarchy, which is the same argument the palette makes for
    /// Chrome.Muted collapsing to white in High Contrast.
    /// </param>
    internal IconText(
        string? glyphKey,
        string label,
        double labelSize,
        string? secondary = null,
        bool mutedSecondary = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = new TextBlock
        {
            Text = label,
            FontSize = labelSize,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Label);

        if (secondary is { Length: > 0 })
        {
            Secondary = new TextBlock
            {
                Text = secondary,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            };

            if (mutedSecondary)
            {
                Secondary.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);
            }

            lines.Children.Add(Secondary);
        }

        var dock = new DockPanel { LastChildFill = true };

        if (glyphKey is { Length: > 0 })
        {
            Glyph = new GlyphPath
            {
                Stretch = Stretch.Uniform,
                Width = GlyphSize,
                Height = GlyphSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };

            Glyph.Token(GlyphPath.DataProperty, glyphKey);

            // The glyph is the same colour as the words next to it, in every state the button has.
            Glyph[!Shape.FillProperty] = new Binding(nameof(Foreground))
            {
                Source = this,
                Mode = BindingMode.OneWay,
            };

            DockPanel.SetDock(Glyph, Dock.Left);
            dock.Children.Add(Glyph);
        }

        dock.Children.Add(lines);
        Content = dock;
    }

    /// <summary>The words. Never null — see the type's summary.</summary>
    internal TextBlock Label { get; }

    /// <summary>The quieter second line, if this offer has one.</summary>
    internal TextBlock? Secondary { get; }

    /// <summary>The glyph, if this offer has one. Most do not.</summary>
    internal GlyphPath? Glyph { get; }
}
