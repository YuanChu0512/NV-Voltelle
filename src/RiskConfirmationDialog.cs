using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace MVolt.Rebuild
{
    internal sealed class RiskConfirmationDialog : Window
    {
        private static readonly Brush BackgroundBrush = Brush("#0A0D14");
        private static readonly Brush CardBrush = Brush("#141925");
        private static readonly Brush StrokeBrush = Brush("#293243");
        private static readonly Brush TextBrush = Brush("#F4F7FB");
        private static readonly Brush MutedBrush = Brush("#9AA5B7");
        private static readonly Brush AccentBrush = Brush("#7BF1C8");
        private static readonly Brush WarningBrush = Brush("#FFCC72");
        private static readonly Brush DangerBrush = Brush("#FF8795");

        private RiskConfirmationDialog(string target)
        {
            Title = VoltelleBrand.ProductName + (VoltelleLocalization.IsEnglish ? " — Confirm hardware write" : " — 确认硬件写入");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 720;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");
            ShowInTaskbar = false;
            UseLayoutRounding = true;

            Grid root = new Grid { Margin = new Thickness(26) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "NV VOLTELLE · HARDWARE WRITE",
                Foreground = AccentBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            });
            heading.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T("确认应用硬件修改"),
                Foreground = TextBrush,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 0)
            });
            heading.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T("请核对本次目标。只有点击“确认应用”后才会向驱动发送 SET。"),
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(heading);

            Border warning = new Border
            {
                Background = Brush("#2A1D14"),
                BorderBrush = Brush("#6D4A24"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 20, 0, 0)
            };
            StackPanel warningContent = new StackPanel();
            warningContent.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T(VoltelleBrand.RiskNotice),
                Foreground = WarningBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            warningContent.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T("不稳定参数可能导致花屏、程序崩溃或驱动重置。请逐步调整并自行完成稳定性测试。"),
                Foreground = Brush("#E8C99A"),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            warning.Child = warningContent;
            Grid.SetRow(warning, 1);
            root.Children.Add(warning);

            Border targetCard = new Border
            {
                Background = CardBrush,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 12, 0, 0)
            };
            StackPanel targetContent = new StackPanel();
            targetContent.Children.Add(new TextBlock { Text = VoltelleLocalization.T("本次目标"), Foreground = MutedBrush, FontSize = 10 });
            targetContent.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T(target),
                Foreground = TextBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 10),
                TextWrapping = TextWrapping.Wrap
            });
            targetContent.Children.Add(new TextBlock
            {
                Text = VoltelleLocalization.T("• 每个项目独立发送写入\n• 每次写入后执行 GET 回读\n• 失败项不会撤销成功项，后续项目继续执行"),
                Foreground = MutedBrush,
                FontSize = 11,
                LineHeight = 18
            });
            targetCard.Child = targetContent;
            Grid.SetRow(targetCard, 2);
            root.Children.Add(targetCard);

            CheckBox acknowledgement = new CheckBox
            {
                Content = VoltelleLocalization.T("我已了解风险，并确认以上目标是我希望应用的参数。"),
                Foreground = TextBrush,
                Margin = new Thickness(2, 18, 0, 0),
                FontSize = 11
            };
            Grid.SetRow(acknowledgement, 3);
            root.Children.Add(acknowledgement);

            Grid actions = new Grid { Margin = new Thickness(0, 20, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock attribution = new TextBlock
            {
                Text = VoltelleLocalization.T(VoltelleBrand.FreeNotice) + " · " + VoltelleBrand.Maker,
                Foreground = MutedBrush,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(attribution);

            Button cancel = DialogButton(VoltelleLocalization.T("取消"), false);
            cancel.Click += delegate { DialogResult = false; };
            Grid.SetColumn(cancel, 1);
            actions.Children.Add(cancel);

            Button apply = DialogButton(VoltelleLocalization.T("确认应用"), true);
            apply.Margin = new Thickness(10, 0, 0, 0);
            apply.IsEnabled = false;
            apply.Opacity = 0.45;
            apply.Click += delegate { DialogResult = true; };
            acknowledgement.Checked += delegate
            {
                apply.IsEnabled = true;
                apply.Opacity = 1.0;
            };
            acknowledgement.Unchecked += delegate
            {
                apply.IsEnabled = false;
                apply.Opacity = 0.45;
            };
            Grid.SetColumn(apply, 2);
            actions.Children.Add(apply);
            Grid.SetRow(actions, 4);
            root.Children.Add(actions);

            Content = root;
        }

        internal static bool Show(Window owner, string target)
        {
            RiskConfirmationDialog dialog = new RiskConfirmationDialog(target) { Owner = owner };
            return dialog.ShowDialog() == true;
        }

        private static Button DialogButton(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Background = primary ? DangerBrush : CardBrush,
                Foreground = primary ? Brush("#24080D") : TextBrush,
                BorderBrush = primary ? DangerBrush : StrokeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 9, 18, 9),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;
            return button;
        }

        private static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }
    }
}
