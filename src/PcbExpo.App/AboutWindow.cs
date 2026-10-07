using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PcbExpo.App;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "О программе PCB Expo";
        Width = 640; Height = 540; MinWidth = 500; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTheme.FitToScreen(this);
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
        var logo = new Image { Source = AppBranding.Logo, Height = 56, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        panel.Children.Add(logo);
        panel.Children.Add(new TextBlock { Text = $"PCB Expo {AppVersion.Current}", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = UiTheme.Brush("#10173A") });
        panel.Children.Add(new TextBlock
        {
            Text = "Подготовка LCD-засветки печатных плат и раскладки для CNC. Предварительная версия.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new SelectableTextBlock
        {
            Text = "Copyright © 2026 PCB Expo contributors.\nЛицензия: AGPL-3.0-or-later.\n\nПрограмма предоставляется без гарантий в пределах условий лицензии. Вы можете использовать, изменять и распространять её при соблюдении этих условий.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new SelectableTextBlock
        {
            Text = "Исходники, документация и релизы:\nhttps://github.com/Antidods/PCB_expo",
            TextWrapping = TextWrapping.Wrap
        });
        var license = new Button { Content = "Полный текст лицензии" };
        license.Click += async (_, _) =>
        {
            using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("PcbExpo.LICENSE")
                ?? throw new InvalidOperationException("Не найден встроенный текст лицензии.");
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            var window = new Window
            {
                Title = "GNU Affero General Public License v3", Width = 820, Height = 660,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer
                {
                    Content = new SelectableTextBlock
                    {
                        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(20)
                    }
                }
            };
            UiTheme.FitToScreen(window);
            await window.ShowDialog(this);
        };

        panel.Children.Add(new TextBlock { Text = "Сведения о сторонних библиотеках: THIRD_PARTY_NOTICES.md и каталог licenses в пакете приложения.", TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(new Border { Classes = { "panel" }, Child = new ScrollViewer { Content = panel } });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(license); buttons.Children.Add(close);
        var footer = UiTheme.Footer(buttons); Grid.SetRow(footer, 1); root.Children.Add(footer);
        Content = root;
    }
}
