// Modal dialog used at export time to collect optional sharing metadata
// (Author, Description, Version) for a preset / car preset / pack. All
// three fields are optional; the user can clear any of them and click OK
// to skip including that field. Author is pre-filled from
// TrueforceSettings.SharingAuthor and the dialog reports back the final
// values so the plugin can persist any change to the saved author.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrueforceForAll.Plugin
{
    internal sealed class PresetMetadataDialog : Window
    {
        public string Author        { get; private set; }
        public string Description   { get; private set; }
        public string AuthorVersion { get; private set; }

        public PresetMetadataDialog(string title, string subjectKind,
            string defaultAuthor, string defaultDescription, string defaultAuthorVersion)
        {
            Title = title;
            Width = 480;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            // Dark theme to match every other plugin dialog. Without this,
            // the window falls through to WPF defaults (white background,
            // black text) which reads as unstyled against SimHub's chrome.
            SettingsControl.ApplyDarkTheme(this);

            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock
            {
                Text = $"Optional info for the {subjectKind}. Leave anything blank to omit it.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            });

            sp.Children.Add(BuildLabel("Author"));
            var tbAuthor = BuildInputTextBox(defaultAuthor, multiline: false);
            sp.Children.Add(tbAuthor);

            sp.Children.Add(BuildLabel("Version"));
            var tbVersion = BuildInputTextBox(defaultAuthorVersion, multiline: false);
            sp.Children.Add(tbVersion);

            sp.Children.Add(BuildLabel("Description"));
            var tbDesc = BuildInputTextBox(defaultDescription, multiline: true);
            sp.Children.Add(tbDesc);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var ok = new Button { Content = "Save & export", Width = 130, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            sp.Children.Add(btnRow);

            ok.Click += (s, e) =>
            {
                Author        = tbAuthor.Text;
                Description   = tbDesc.Text;
                AuthorVersion = tbVersion.Text;
                DialogResult = true;
            };

            Loaded += (s, e) => { tbAuthor.Focus(); tbAuthor.SelectAll(); };

            Content = sp;
        }

        private static UIElement BuildLabel(string text)
            => new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };

        // TextBox doesn't pick up TextElement.Foreground inheritance (its
        // template hard-codes a default Foreground), so set Background +
        // Foreground + CaretBrush explicitly on every input.
        private static TextBox BuildInputTextBox(string text, bool multiline)
        {
            var tb = new TextBox
            {
                Text = text ?? "",
                Margin = new Thickness(0, 0, 0, multiline ? 12 : 10),
                Padding = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)),
            };
            if (multiline)
            {
                tb.Height = 80;
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            return tb;
        }
    }
}
