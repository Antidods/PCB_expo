using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace PcbExpo.App;

internal static class UiTheme
{
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public static void Apply(Application app)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
        {
            Accent = Color.Parse("#6418D8"), RegionColor = Color.Parse("#EEF0F4")
        };
        app.Styles.Add(fluent);
        Add(s => s.OfType<Window>(),
            (Window.IconProperty, AppBranding.Icon), (TemplatedControl.FontFamilyProperty, new FontFamily("Segoe UI")),
            (TemplatedControl.FontSizeProperty, 13d), (TemplatedControl.ForegroundProperty, Brush("#1E2433")),
            (TemplatedControl.BackgroundProperty, Brush("#EEF0F4")));
        Add(s => s.OfType<Button>(), (Layoutable.MinHeightProperty, 28d),
            (TemplatedControl.FontSizeProperty, 12.5d), (TemplatedControl.PaddingProperty, new Thickness(8, 4)),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
            (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        ButtonStyle(null, "#FFFFFF", "#F7F8FA", "#EEF0F4", "#1E2433", "#C3C9D4");
        ButtonStyle("primary", "#6418D8", "#5412B8", "#460E9C", "#FFFFFF", "#6418D8");
        Add(s => s.OfType<Button>().Class("primary"), (Layoutable.MinHeightProperty, 38d),
            (TemplatedControl.FontSizeProperty, 14d), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        ButtonStyle("toolbar", "Transparent", "#F0F1F5", "#E6E9EE", "#1E2433", "Transparent");
        Add(s => s.OfType<Button>().Class("toolbar"), (Layoutable.MinHeightProperty, 30d),
            (TemplatedControl.PaddingProperty, new Thickness(7, 4)));
        ButtonStyle("acc", "#F1EAFE", "#E8DCFC", "#DDCDF9", "#4E10AC", "#B99BF3");
        ButtonStyle("chip", "#FFFFFF", "#F1EAFE", "#E8DCFC", "#596174", "#C3C9D4");
        Add(s => s.OfType<Button>().Class("chip"), (Layoutable.MinHeightProperty, 26d),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(13)), (TemplatedControl.FontSizeProperty, 12d),
            (TemplatedControl.PaddingProperty, new Thickness(9, 3)));
        Add(s => s.OfType<Button>().Class("chip").Class("on"),
            (TemplatedControl.BackgroundProperty, Brush("#F1EAFE")), (TemplatedControl.ForegroundProperty, Brush("#4E10AC")),
            (TemplatedControl.BorderBrushProperty, Brush("#B99BF3")), (TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        ButtonStyle("link", "Transparent", "#F1EAFE", "#E8DCFC", "#4E10AC", "Transparent");
        ButtonStyle("expander-header", "Transparent", "#F7F8FA", "#EEF0F4", "#10173A", "Transparent");
        Add(s => s.OfType<Button>().Class("link"), (TemplatedControl.PaddingProperty, new Thickness(0, 3)),
            (ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        Add(s => s.OfType<TextBox>(), (Layoutable.MinHeightProperty, 28d),
            (TemplatedControl.PaddingProperty, new Thickness(8, 4)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
            (TemplatedControl.BorderBrushProperty, Brush("#C3C9D4")));
        Add(s => s.OfType<TextBox>().Class("num"), (TextBox.TextAlignmentProperty, TextAlignment.Right),
            (TemplatedControl.FontFeaturesProperty, FontFeatureCollection.Parse("tnum")));
        Add(s => s.OfType<TextBox>().Class(":focus"), (TemplatedControl.BorderBrushProperty, Brush("#6418D8")));
        Add(s => s.OfType<TextBox>().Class(":disabled"), (TemplatedControl.BackgroundProperty, Brush("#F3F4F6")),
            (TemplatedControl.ForegroundProperty, Brush("#8A90A0")));
        Add(s => s.OfType<ComboBox>(), (Layoutable.MinHeightProperty, 28d),
            (TemplatedControl.PaddingProperty, new Thickness(8, 4)),
            (TemplatedControl.BackgroundProperty, Brushes.White),
            (TemplatedControl.BorderBrushProperty, Brush("#C3C9D4")),
            (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        Add(s => s.OfType<ComboBox>().Template().OfType<Border>().Name("Background"),
            (Border.BackgroundProperty, Brushes.White), (Border.BorderBrushProperty, Brush("#C3C9D4")));
        Add(s => s.OfType<CheckBox>(), (Layoutable.MinHeightProperty, 26d),
            (TemplatedControl.FontSizeProperty, 12d));
        Add(s => s.OfType<TextBlock>().Class("caption"), (TextBlock.FontSizeProperty, 12d),
            (TextBlock.ForegroundProperty, Brush("#596174")));
        Add(s => s.OfType<TextBlock>().Class("h"), (TextBlock.FontSizeProperty, 13d),
            (TextBlock.FontWeightProperty, FontWeight.SemiBold), (TextBlock.ForegroundProperty, Brush("#10173A")));
        Add(s => s.OfType<Border>().Class("panel"), (Border.BackgroundProperty, Brushes.White),
            (Border.BorderBrushProperty, Brush("#DDE1E8")), (Border.BorderThicknessProperty, new Thickness(1)));
        Add(s => s.OfType<Border>().Class("note"), (Border.BackgroundProperty, Brush("#F3F5F9")),
            (Border.BorderBrushProperty, Brush("#E1E6EF")), (Border.BorderThicknessProperty, new Thickness(1)),
            (Border.CornerRadiusProperty, new CornerRadius(6)), (Border.PaddingProperty, new Thickness(10, 8)));
        Add(s => s.OfType<Border>().Class("alert"), (Border.BackgroundProperty, Brush("#F3F5F9")),
            (Border.BorderBrushProperty, Brush("#E1E6EF")), (Border.BorderThicknessProperty, new Thickness(1)),
            (Border.CornerRadiusProperty, new CornerRadius(6)), (Border.PaddingProperty, new Thickness(10, 8)));
        foreach (var cls in new[] { "note", "alert" })
        {
            Add(s => s.OfType<Border>().Class(cls).Class("warn"),
                (Border.BackgroundProperty, Brush("#FFF5E0")), (Border.BorderBrushProperty, Brush("#EFC56A")));
            Add(s => s.OfType<Border>().Class(cls).Class("err"),
                (Border.BackgroundProperty, Brush("#FDEDEC")), (Border.BorderBrushProperty, Brush("#F0B4AF")));
            Add(s => s.OfType<Border>().Class(cls).Class("ok"),
                (Border.BackgroundProperty, Brush("#EAF6EE")), (Border.BorderBrushProperty, Brush("#A9D8B8")));
            Add(s => s.OfType<Border>().Class(cls).Class("warn").Descendant().OfType<TextBlock>(),
                (TextBlock.ForegroundProperty, Brush("#5E3B00")));
        }
        Add(s => s.OfType<Expander>(), (Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
            (TemplatedControl.FontSizeProperty, 13d), (TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (TemplatedControl.BorderBrushProperty, Brush("#E6E9EE")),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
            (TemplatedControl.TemplateProperty, new FuncControlTemplate<Expander>((owner, _) =>
            {
                var panel = new StackPanel();
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), ColumnSpacing = 6 };
                var arrow = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M0,0 L5,5 L0,10"), Stroke = Brush("#596174"), StrokeThickness = 1.5,
                    Width = 6, Height = 10, VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = RelativePoint.Center
                };
                arrow.Bind(Visual.RenderTransformProperty, new Avalonia.Data.Binding(nameof(Expander.IsExpanded))
                {
                    Source = owner,
                    Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, RotateTransform>(expanded => new RotateTransform(expanded ? 90 : 0))
                });
                header.Children.Add(arrow);
                var title = new ContentPresenter { FontWeight = FontWeight.SemiBold, Foreground = Brush("#10173A") };
                title.Bind(ContentPresenter.ContentProperty, owner.GetObservable(HeaderedContentControl.HeaderProperty));
                Grid.SetColumn(title, 1); header.Children.Add(title);
                var button = new Button { Content = header, Classes = { "expander-header" }, MinHeight = 34,
                    Padding = new Thickness(0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => owner.SetCurrentValue(Expander.IsExpandedProperty, !owner.IsExpanded);
                panel.Children.Add(button);
                var content = new ContentPresenter();
                content.Bind(ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
                content.Bind(Visual.IsVisibleProperty, owner.GetObservable(Expander.IsExpandedProperty));
                panel.Children.Add(content);
                return new Border { Child = panel, BorderBrush = Brush("#E6E9EE"), BorderThickness = new Thickness(0, 0, 0, 1) };
            })));
        Add(s => s.OfType<Button>().Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, Brush("#F3F4F6")),
            (ContentPresenter.ForegroundProperty, Brush("#8A90A0")),
            (ContentPresenter.BorderBrushProperty, Brush("#DDE1E8")));

        void Add(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, object Value)[] values)
        {
            var style = new Style(selector);
            foreach (var (property, value) in values) style.Setters.Add(new Setter(property, value));
            app.Styles.Add(style);
        }
        void ButtonStyle(string? cls, string normal, string hover, string pressed, string text, string border)
        {
            Selector ButtonSelector(Selector? s) => cls is null ? s.OfType<Button>() : s.OfType<Button>().Class(cls);
            Add(ButtonSelector, (TemplatedControl.BackgroundProperty, Brush(normal)),
                (TemplatedControl.ForegroundProperty, Brush(text)), (TemplatedControl.BorderBrushProperty, Brush(border)));
            // Ресурсы Style в Avalonia доступны и другим контролам. Состояния задаём только совпавшему шаблону.
            foreach (var (state, background) in new[] { (":pointerover", hover), (":pressed", pressed) })
            {
                Add(s => ButtonSelector(s).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                    (ContentPresenter.BackgroundProperty, Brush(background)),
                    (ContentPresenter.ForegroundProperty, Brush(text)), (ContentPresenter.BorderBrushProperty, Brush(border)));
            }
        }
    }

    public static TextBlock Caption(string text) => new()
    {
        Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Heading(string text) => new()
    {
        Text = text, Classes = { "h" }, TextWrapping = TextWrapping.Wrap
    };

    public static Border Note(Control content, string? kind = null)
    {
        if (kind == "warn")
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
            row.Children.Add(new TextBlock { Text = "⚠", Foreground = Brush("#B86E00"), FontSize = 15 });
            Grid.SetColumn(content, 1); row.Children.Add(content); content = row;
        }
        var border = new Border { Child = content, Classes = { "note" } };
        if (kind is not null) border.Classes.Add(kind);
        return border;
    }

    public static void FitToScreen(Window window)
    {
        window.Opened += (_, _) =>
        {
            if (window.Screens.ScreenFromWindow(window) is not { } screen) return;
            var area = screen.WorkingArea;
            window.Width = Math.Min(window.Width, area.Width / screen.Scaling);
            window.Height = Math.Min(window.Height, area.Height / screen.Scaling);
        };
    }

    public static Border Footer(Control content) => new()
    {
        Child = content, Padding = new Thickness(18, 12), Background = Brush("#F7F8FA"),
        BorderBrush = Brush("#DDE1E8"), BorderThickness = new Thickness(0, 1, 0, 0)
    };

    public static void Ellipsis(ComboBox box)
    {
        box.ItemTemplate = new FuncDataTemplate<object>((value, _) => new TextBlock
        {
            Text = value?.ToString(), TextTrimming = TextTrimming.CharacterEllipsis,
            [ToolTip.TipProperty] = value?.ToString()
        });
        box.SelectionChanged += (_, _) => ToolTip.SetTip(box, box.SelectedItem?.ToString());
    }

    public static Control SummaryView(ExportSummary summary, bool warnings = true)
    {
        var panel = new StackPanel { Spacing = 14 };
        if (warnings)
            foreach (var warning in summary.Warnings)
                panel.Children.Add(Note(Caption(warning), "warn"));
        foreach (var group in summary.Groups)
        {
            var section = new StackPanel { Spacing = 7 };
            section.Children.Add(Heading(group.Title));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*"), RowSpacing = 3, ColumnSpacing = 8 };
            foreach (var field in group.Fields)
            {
                var row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = Caption(field.Label);
                var value = new TextBlock { Text = field.Value, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                ToolTip.SetTip(value, field.Value);
                if (field.Label == "Шаблон")
                {
                    value.TextWrapping = TextWrapping.NoWrap;
                    value.TextTrimming = TextTrimming.CharacterEllipsis;
                }
                Grid.SetRow(label, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1);
                grid.Children.Add(label); grid.Children.Add(value);
            }
            section.Children.Add(grid);
            panel.Children.Add(section);
        }
        panel.Children.Add(Caption(summary.Footnote));
        return panel;
    }
}
