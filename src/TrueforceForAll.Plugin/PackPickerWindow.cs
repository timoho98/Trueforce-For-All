// Modal dialog used by the "Export pack..." flow: lets the user pick which
// game presets and car presets to bundle into a .tfpack zip. Two side-by-side
// CheckBox lists with Select all / Select none controls; OK gates on at least
// one item being selected.
//
// Built in code (not XAML) because the dialog is single-use and the plugin's
// XAML build pipeline doesn't auto-pick up new .xaml files in this folder
// without a corresponding .csproj change. A few dozen lines of WPF here is
// cheaper than wiring a whole new compiled-XAML resource.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrueforceForAll.Plugin
{
    internal sealed class PackPickerWindow : Window
    {
        public List<string> SelectedPresetNames { get; private set; } = new List<string>();
        public List<CarPresetEntry> SelectedCarPresets { get; private set; } = new List<CarPresetEntry>();

        private readonly List<CheckBox> _presetChecks = new List<CheckBox>();
        private readonly List<(CheckBox Cb, CarPresetEntry Entry)> _carChecks = new List<(CheckBox, CarPresetEntry)>();

        public PackPickerWindow(List<string> presets, List<CarPresetEntry> cars, bool exportMode)
        {
            Title = exportMode ? "Export pack" : "Import pack";
            Width = 760;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 560;
            MinHeight = 360;

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Pick the presets to bundle into the pack. Recipients can import the whole pack at once.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var twoCol = new Grid();
            twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(twoCol, 1);
            root.Children.Add(twoCol);

            twoCol.Children.Add(BuildPresetPanel(presets));
            var carPanel = BuildCarPanel(cars);
            Grid.SetColumn(carPanel, 1);
            twoCol.Children.Add(carPanel);

            // OK / Cancel row.
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var ok = new Button { Content = exportMode ? "Export..." : "Import", Width = 110, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            Grid.SetRow(btnRow, 2);
            root.Children.Add(btnRow);

            ok.Click += (s, e) =>
            {
                SelectedPresetNames = new List<string>();
                foreach (var cb in _presetChecks)
                    if (cb.IsChecked == true) SelectedPresetNames.Add((string)cb.Tag);

                SelectedCarPresets = new List<CarPresetEntry>();
                foreach (var (cb, entry) in _carChecks)
                    if (cb.IsChecked == true) SelectedCarPresets.Add(entry);

                if (SelectedPresetNames.Count == 0 && SelectedCarPresets.Count == 0)
                {
                    MessageBox.Show(this, "Pick at least one preset or car preset to include.",
                                    "Trueforce", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                DialogResult = true;
            };

            Content = root;
        }

        private UIElement BuildPresetPanel(List<string> presets)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };

            var header = BuildPanelHeader($"Game presets ({presets.Count})");
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            var toggleRow = BuildToggleRow(
                onAll:  () => { foreach (var cb in _presetChecks) cb.IsChecked = true;  },
                onNone: () => { foreach (var cb in _presetChecks) cb.IsChecked = false; });
            DockPanel.SetDock(toggleRow, Dock.Top);
            panel.Children.Add(toggleRow);

            var listSp = new StackPanel();
            if (presets.Count == 0)
            {
                listSp.Children.Add(new TextBlock
                {
                    Text = "No game presets in your library yet.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                    Margin = new Thickness(4, 6, 0, 0),
                });
            }
            else
            {
                foreach (var name in presets)
                {
                    var cb = new CheckBox { Content = name, Tag = name, Margin = new Thickness(2, 3, 2, 3), IsChecked = true };
                    _presetChecks.Add(cb);
                    listSp.Children.Add(cb);
                }
            }
            var sv = new ScrollViewer
            {
                Content = listSp,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 4),
            };
            var border = new Border
            {
                Child = sv,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                BorderThickness = new Thickness(1),
            };
            panel.Children.Add(border);
            return panel;
        }

        private UIElement BuildCarPanel(List<CarPresetEntry> cars)
        {
            var panel = new DockPanel { Margin = new Thickness(6, 0, 0, 0) };

            var header = BuildPanelHeader($"Car presets ({cars.Count})");
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            var toggleRow = BuildToggleRow(
                onAll:  () => { foreach (var pair in _carChecks) pair.Cb.IsChecked = true;  },
                onNone: () => { foreach (var pair in _carChecks) pair.Cb.IsChecked = false; });
            DockPanel.SetDock(toggleRow, Dock.Top);
            panel.Children.Add(toggleRow);

            var listSp = new StackPanel();
            if (cars.Count == 0)
            {
                listSp.Children.Add(new TextBlock
                {
                    Text = "No car presets saved yet.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                    Margin = new Thickness(4, 6, 0, 0),
                });
            }
            else
            {
                foreach (var entry in cars)
                {
                    string label = $"{entry.CarId} — {entry.PresetName}";
                    if (!string.IsNullOrEmpty(entry.GameName)) label += $"  ({entry.GameName})";
                    if (entry.IsBuiltin) label += "  [built-in]";
                    var cb = new CheckBox { Content = label, Margin = new Thickness(2, 3, 2, 3), IsChecked = true };
                    _carChecks.Add((cb, entry));
                    listSp.Children.Add(cb);
                }
            }
            var sv = new ScrollViewer
            {
                Content = listSp,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 4),
            };
            var border = new Border
            {
                Child = sv,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                BorderThickness = new Thickness(1),
            };
            panel.Children.Add(border);
            return panel;
        }

        private static UIElement BuildPanelHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 2, 6),
            };
        }

        private static UIElement BuildToggleRow(Action onAll, Action onNone)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var all  = new Button { Content = "Select all",  Width = 90, Height = 22, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
            var none = new Button { Content = "Select none", Width = 90, Height = 22, FontSize = 11 };
            all.Click  += (s, e) => onAll();
            none.Click += (s, e) => onNone();
            row.Children.Add(all);
            row.Children.Add(none);
            return row;
        }
    }
}
