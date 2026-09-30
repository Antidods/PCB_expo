using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PcbExpo.App;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "О программе PCB Expo";
        Width = 640; Height = 540; MinWidth = 500; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
        panel.Children.Add(new Image { Source = AppBranding.Logo, Height = 100, Stretch = Stretch.Uniform });
        panel.Children.Add(new TextBlock { Text = $"PCB Expo {AppVersion.Current}", FontSize = 22, FontWeight = FontWeight.Bold });
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
            await window.ShowDialog(this);
        };
        panel.Children.Add(license);
        panel.Children.Add(new TextBlock { Text = "Сведения о сторонних библиотеках: THIRD_PARTY_NOTICES.md и каталог licenses в пакете приложения.", TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = new ScrollViewer { Content = panel };
    }
}
