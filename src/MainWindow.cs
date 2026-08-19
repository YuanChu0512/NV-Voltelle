using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MVolt.Rebuild
{
    internal sealed class MainWindow : Window
    {
        private const string PageOverview = "总览";
        private const string PageTuning = "调校";
        private const string PageVoltage = "电压与 V/F";
        private const string PagePower = "功耗通道";
        private const string PageProfiles = "配置档";
        private const string PageInterfaces = "接口状态";

        private static readonly Brush BackgroundBrush = Brush("#090B11");
        private static readonly Brush SidebarBrush = Brush("#0C0F17");
        private static readonly Brush PanelBrush = Brush("#0F131C");
        private static readonly Brush CardBrush = Brush("#141925");
        private static readonly Brush CardHoverBrush = Brush("#1A2130");
        private static readonly Brush MutedBrush = Brush("#929EB0");
        private static readonly Brush AccentBrush = Brush("#7BF1C8");
        private static readonly Brush AccentDarkBrush = Brush("#15372F");
        private static readonly Brush SecondaryAccentBrush = Brush("#918DFF");
        private static readonly Brush StrokeBrush = Brush("#273143");
        private static readonly Brush WarningBrush = Brush("#FFCC72");
        private static readonly Brush ErrorBrush = Brush("#FF8795");

        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly ConditionalWeakTable<DependencyObject, LocalizedElementState> localizedElements = new ConditionalWeakTable<DependencyObject, LocalizedElementState>();
        private readonly DispatcherTimer timer;
        private TextBlock gpuTitle;
        private TextBlock gpuSubtitle;
        private TextBlock statusText;
        private TextBlock timestampText;
        private TextBlock pageTitle;
        private TextBlock pageSubtitle;
        private ContentControl pageHost;
        private ScrollViewer pageScroll;
        private TextBlock writeModeValue;
        private TextBlock writeModeDescription;
        private Button refreshButton;
        private Button resetAllButton;
        private Button languageButton;
        private TextBox coreInput;
        private TextBox memoryInput;
        private TextBox powerInput;
        private CheckBox boostInput;
        private Button applyTuningButton;
        private bool voltageDraftInitialized;
        private string nvvddMinimumDraft = string.Empty;
        private string nvvddMaximumDraft = string.Empty;
        private string msvddMinimumDraft = string.Empty;
        private string msvddMaximumDraft = string.Empty;
        private string voltageBoostDraft = string.Empty;
        private string xbarOffsetDraft = string.Empty;
        private string sysClockOffsetDraft = string.Empty;
        private string videoClockOffsetDraft = string.Empty;
        private readonly Dictionary<uint, string> fanDutyDrafts = new Dictionary<uint, string>();
        private readonly Dictionary<uint, bool> fanManualDrafts = new Dictionary<uint, bool>();
        private string vfPointDraft = string.Empty;
        private string vfTargetDraft = string.Empty;
        private string vfRegionStartDraft = string.Empty;
        private string vfRegionEndDraft = string.Empty;
        private string vfRegionOffsetDraft = "0";
        private int? vfSelectedPointIndex;
        private readonly HashSet<int> vfSelectedPointIndices = new HashSet<int>();
        private TextBox vfPointIndexInput;
        private TextBox vfTargetFrequencyInput;
        private TextBlock vfChartSummaryText;
        private TextBlock vfStagedCountText;
        private Button applyStagedVfButton;
        private bool xocEnabled;
        private bool profileDirty;
        private readonly List<VfOffsetChange> stagedVfChanges = new List<VfOffsetChange>();
        private ProfileStore profileStore;
        private ProfileDocument profileDocument;
        private MVoltProfile pendingProfile;
        private string selectedProfileId;
        private string profileNameDraft = string.Empty;
        private string profileError;
        private TextBox profileNameInput;
        private string activePage = PageOverview;
        private IGpuBackend backend;
        private NvApiBackend nvBackend;
        private SafeWriteCoordinator tuningCoordinator;
        private GpuSnapshot snapshot;
        private bool startupBaselineCaptured;
        private readonly bool startHiddenInTray;
        private readonly bool allowHardwareWrites;
        private readonly bool forceReadOnly;
        private readonly bool uiQaMode;
        private readonly bool uiQaTrayCycle;
        private readonly bool startupAutoApplyRequested;
        private bool startupAutoApplyHandled;
        private bool minimizeToTray;
        private bool exitRequested;
        private Forms.NotifyIcon trayIcon;
        private Forms.ToolStripMenuItem trayOpenItem;
        private Forms.ToolStripMenuItem trayRefreshItem;
        private Forms.ToolStripMenuItem trayExitItem;

        private sealed class LocalizedElementState
        {
            internal bool TextInitialized;
            internal bool ContentInitialized;
            internal bool ToolTipInitialized;
            internal string Text;
            internal string Content;
            internal string ToolTip;
        }

        private bool CanInitiateWrite
        {
            get { return uiQaMode || (backend != null && backend.HardwareWritesEnabled); }
        }

        public MainWindow(bool startInTray, bool enableHardwareWrites, bool readOnly, bool enableUiQaMode, bool enableUiQaTrayCycle, bool enableStartupAutoApply)
        {
            startHiddenInTray = startInTray;
            allowHardwareWrites = enableHardwareWrites;
            forceReadOnly = readOnly;
            uiQaMode = enableUiQaMode;
            uiQaTrayCycle = enableUiQaTrayCycle;
            startupAutoApplyRequested = enableStartupAutoApply;
            minimizeToTray = startInTray;
            Title = VoltelleBrand.ProductName + " — NVIDIA GPU 调校工作台";
            Width = 1320;
            Height = 860;
            MinWidth = 1080;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Content = BuildChrome();
            InitializeTrayIcon();
            SelectPage(PageOverview);
            ApplyCurrentLanguage(true);

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { RefreshSnapshot(); };
            Loaded += delegate
            {
                InitializeBackend();
                timer.Start();
                if (startHiddenInTray) HideToTray();
                else if (uiQaTrayCycle) RunUiQaTrayCycle();
            };
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized && minimizeToTray)
                    HideToTray();
            };
            Closing += delegate(object sender, CancelEventArgs args)
            {
                if (!exitRequested && minimizeToTray)
                {
                    args.Cancel = true;
                    HideToTray();
                }
            };
            Closed += delegate
            {
                exitRequested = true;
                timer.Stop();
                if (backend != null) backend.Dispose();
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
            };
        }

        private void InitializeTrayIcon()
        {
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            trayOpenItem = new Forms.ToolStripMenuItem("打开 NV Voltelle", null, delegate { ShowFromTray(); });
            trayRefreshItem = new Forms.ToolStripMenuItem("刷新遥测", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { RefreshSnapshot(); })); });
            menu.Items.Add(trayOpenItem);
            menu.Items.Add(trayRefreshItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            trayExitItem = new Forms.ToolStripMenuItem("退出", null, delegate
            {
                exitRequested = true;
                Dispatcher.BeginInvoke(new Action(delegate { Close(); }));
            });
            menu.Items.Add(trayExitItem);
            Drawing.Icon applicationIcon = null;
            try { applicationIcon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { applicationIcon = null; }
            trayIcon = new Forms.NotifyIcon
            {
                Text = "NV Voltelle · Mozelle",
                Icon = applicationIcon ?? Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = false
            };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            UpdateTrayLanguage();
        }

        private void ApplyCurrentLanguage(bool refreshSources)
        {
            Title = VoltelleLocalization.IsEnglish
                ? VoltelleBrand.ProductName + " — NVIDIA GPU Tuning Studio"
                : VoltelleBrand.ProductName + " — NVIDIA GPU 调校工作台";
            ApplyLocalizationToTree(Content as DependencyObject, refreshSources);
            if (languageButton != null)
            {
                languageButton.Content = VoltelleLocalization.IsEnglish ? "中文" : "EN";
                languageButton.ToolTip = VoltelleLocalization.IsEnglish ? "切换到中文" : "Switch to English";
            }
            UpdateTrayLanguage();
        }

        private void UpdateTrayLanguage()
        {
            if (trayOpenItem == null) return;
            trayOpenItem.Text = VoltelleLocalization.IsEnglish ? "Open NV Voltelle" : "打开 NV Voltelle";
            trayRefreshItem.Text = VoltelleLocalization.IsEnglish ? "Refresh telemetry" : "刷新遥测";
            trayExitItem.Text = VoltelleLocalization.IsEnglish ? "Exit" : "退出";
        }

        private void ApplyLocalizationToTree(DependencyObject root, bool refreshSources)
        {
            if (root == null) return;
            LocalizeTreeNode(root, refreshSources, new HashSet<DependencyObject>());
        }

        private void LocalizeTreeNode(DependencyObject element, bool refreshSources, HashSet<DependencyObject> visited)
        {
            if (element == null || !visited.Add(element)) return;
            if (element == languageButton) return;
            LocalizeElement(element, refreshSources);
            ContentControl stringContentControl = element as ContentControl;
            if (stringContentControl != null && stringContentControl.Content is string) return;

            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                DependencyObject dependencyChild = child as DependencyObject;
                if (dependencyChild != null) LocalizeTreeNode(dependencyChild, refreshSources, visited);
            }

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(element);
                for (int index = 0; index < childCount; index++)
                    LocalizeTreeNode(VisualTreeHelper.GetChild(element, index), refreshSources, visited);
            }
            catch (Exception) { }
        }

        private void LocalizeElement(DependencyObject element, bool refreshSources)
        {
            LocalizedElementState state = localizedElements.GetOrCreateValue(element);
            TextBlock textBlock = element as TextBlock;
            if (textBlock != null)
            {
                if (!state.TextInitialized)
                {
                    state.TextInitialized = true;
                    state.Text = textBlock.Text;
                }
                else if (refreshSources && textBlock.Text != VoltelleLocalization.T(state.Text))
                {
                    state.Text = textBlock.Text;
                }
                textBlock.Text = VoltelleLocalization.T(state.Text);
            }

            ContentControl contentControl = element as ContentControl;
            if (contentControl != null && contentControl != languageButton)
            {
                string content = contentControl.Content as string;
                if (content != null)
                {
                    if (!state.ContentInitialized)
                    {
                        state.ContentInitialized = true;
                        state.Content = content;
                    }
                    else if (refreshSources && content != VoltelleLocalization.T(state.Content))
                    {
                        state.Content = content;
                    }
                    contentControl.Content = VoltelleLocalization.T(state.Content);
                }
            }

            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null && frameworkElement != languageButton)
            {
                string toolTip = frameworkElement.ToolTip as string;
                if (toolTip != null)
                {
                    if (!state.ToolTipInitialized)
                    {
                        state.ToolTipInitialized = true;
                        state.ToolTip = toolTip;
                    }
                    else if (refreshSources && toolTip != VoltelleLocalization.T(state.ToolTip))
                    {
                        state.ToolTip = toolTip;
                    }
                    frameworkElement.ToolTip = VoltelleLocalization.T(state.ToolTip);
                }
            }
        }

        private void HideToTray()
        {
            if (exitRequested || trayIcon == null) return;
            trayIcon.Visible = true;
            ShowInTaskbar = false;
            Hide();
        }

        private void ShowFromTray()
        {
            if (exitRequested) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                ShowInTaskbar = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
                if (trayIcon != null) trayIcon.Visible = false;
            }));
        }

        private void RunUiQaTrayCycle()
        {
            int phase = 0;
            DispatcherTimer trayCycle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            trayCycle.Tick += delegate
            {
                if (phase == 0)
                {
                    phase = 1;
                    HideToTray();
                    return;
                }
                if (phase == 1)
                {
                    phase = 2;
                    ShowFromTray();
                    return;
                }
                trayCycle.Stop();
                bool restored = IsVisible && ShowInTaskbar && trayIcon != null && !trayIcon.Visible;
                statusText.Text = restored
                    ? "UI 验收完成：系统托盘隐藏与恢复路径均可用。"
                    : "UI 验收失败：系统托盘恢复状态不一致。";
                statusText.Foreground = restored ? AccentBrush : ErrorBrush;
            };
            trayCycle.Start();
        }

        private UIElement BuildChrome()
        {
            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border sidebar = new Border
            {
                Background = SidebarBrush,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            sidebar.Child = BuildSidebar();
            root.Children.Add(sidebar);

            Grid main = new Grid { Margin = new Thickness(32, 25, 32, 20) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(main, 1);
            root.Children.Add(main);

            main.Children.Add(BuildHeader());

            pageScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 18, 0, 14)
            };
            pageHost = new ContentControl();
            pageScroll.Content = pageHost;
            Grid.SetRow(pageScroll, 1);
            main.Children.Add(pageScroll);

            Border footer = new Border
            {
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 12, 0, 0)
            };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock { Text = "初始化中", Foreground = AccentBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            timestampText = new TextBlock { Text = "—", Foreground = MutedBrush, FontSize = 11, Margin = new Thickness(18, 0, 0, 0) };
            footerGrid.Children.Add(statusText);
            Grid.SetColumn(timestampText, 1);
            footerGrid.Children.Add(timestampText);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            main.Children.Add(footer);
            return root;
        }

        private UIElement BuildSidebar()
        {
            Grid grid = new Grid { Margin = new Thickness(20, 24, 18, 20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid brand = new Grid();
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border mark = new Border
            {
                Width = 38,
                Height = 38,
                Background = AccentDarkBrush,
                BorderBrush = Brush("#327866"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            mark.Child = new TextBlock
            {
                Text = "NV",
                Foreground = AccentBrush,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            brand.Children.Add(mark);
            StackPanel brandText = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            brandText.Children.Add(new TextBlock { Text = "Voltelle", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            brandText.Children.Add(new TextBlock
            {
                Text = "GPU TUNING STUDIO · " + VoltelleBrand.ProductVersion,
                Foreground = AccentBrush,
                FontSize = 8,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(brandText, 1);
            brand.Children.Add(brandText);
            grid.Children.Add(brand);

            StackPanel nav = new StackPanel { Margin = new Thickness(0, 38, 0, 0) };
            AddNav(nav, PageOverview, "实时状态与硬件身份");
            AddNav(nav, PageTuning, "核心、显存、功耗与锁频");
            AddNav(nav, PageVoltage, "电压轨、Crossbar 与 V/F 曲线");
            AddNav(nav, PagePower, "逐通道功率、电流与电压");
            AddNav(nav, PageProfiles, "保存、预览与分项应用");
            AddNav(nav, PageInterfaces, "接口支持与验证状态");
            Grid.SetRow(nav, 1);
            grid.Children.Add(nav);

            Border safety = new Border
            {
                Background = CardBrush,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(15)
            };
            StackPanel safetyStack = new StackPanel();
            safetyStack.Children.Add(new TextBlock { Text = "运行模式", Foreground = MutedBrush, FontSize = 9 });
            writeModeValue = new TextBlock
            {
                Text = "初始化中",
                Foreground = AccentBrush,
                FontWeight = FontWeights.SemiBold,
                FontSize = 17,
                Margin = new Thickness(0, 5, 0, 5)
            };
            safetyStack.Children.Add(writeModeValue);
            writeModeDescription = new TextBlock
            {
                Text = "正在连接 NVAPI。",
                Foreground = MutedBrush,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15
            };
            safetyStack.Children.Add(writeModeDescription);
            safetyStack.Children.Add(new Border { Height = 1, Background = StrokeBrush, Margin = new Thickness(0, 13, 0, 11) });
            safetyStack.Children.Add(new TextBlock { Text = VoltelleBrand.FreeNotice, Foreground = SecondaryAccentBrush, FontSize = 10, FontWeight = FontWeights.SemiBold });
            safetyStack.Children.Add(new TextBlock { Text = "制作者 " + VoltelleBrand.Maker + "\nB站 @" + VoltelleBrand.BilibiliId, Foreground = MutedBrush, FontSize = 9, Margin = new Thickness(0, 4, 0, 0), LineHeight = 15 });
            safety.Child = safetyStack;
            Grid.SetRow(safety, 2);
            grid.Children.Add(safety);
            return grid;
        }

        private UIElement BuildHeader()
        {
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            pageTitle = new TextBlock { Text = PageOverview, Foreground = MutedBrush, FontSize = 11, FontWeight = FontWeights.SemiBold };
            gpuTitle = new TextBlock { Text = "正在连接 NVAPI…", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) };
            gpuSubtitle = new TextBlock { Text = "驱动与 VBIOS 信息", Foreground = MutedBrush, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            pageSubtitle = new TextBlock { Text = "", Foreground = Brush("#65768D"), FontSize = 10, Margin = new Thickness(0, 3, 0, 0) };
            left.Children.Add(pageTitle);
            left.Children.Add(gpuTitle);
            left.Children.Add(gpuSubtitle);
            left.Children.Add(pageSubtitle);
            header.Children.Add(left);

            StackPanel headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            languageButton = SecondaryButton("EN");
            languageButton.MinWidth = 58;
            languageButton.Click += delegate
            {
                VoltelleLocalization.Set(VoltelleLocalization.IsEnglish ? VoltelleLanguage.Chinese : VoltelleLanguage.English);
                ApplyCurrentLanguage(false);
            };
            headerActions.Children.Add(languageButton);
            Button trayButton = SecondaryButton("后台运行");
            trayButton.Margin = new Thickness(9, 0, 0, 0);
            trayButton.ToolTip = "隐藏主窗口并继续在系统托盘采样";
            trayButton.Click += delegate
            {
                minimizeToTray = true;
                HideToTray();
            };
            headerActions.Children.Add(trayButton);
            refreshButton = SecondaryButton("↻  刷新采样");
            refreshButton.Margin = new Thickness(9, 0, 0, 0);
            refreshButton.VerticalAlignment = VerticalAlignment.Center;
            refreshButton.Click += delegate { RefreshSnapshot(); };
            resetAllButton = SecondaryButton("一键复位");
            resetAllButton.Margin = new Thickness(9, 0, 0, 0);
            resetAllButton.Foreground = ErrorBrush;
            resetAllButton.BorderBrush = Brush("#6B303A");
            resetAllButton.Background = Brush("#26151A");
            resetAllButton.ToolTip = "立即把全部可调项目恢复为驱动默认值并执行 GET 回读";
            resetAllButton.IsEnabled = false;
            resetAllButton.Click += delegate { ResetAllAndApplyDirect(); };
            headerActions.Children.Add(resetAllButton);
            headerActions.Children.Add(refreshButton);
            Grid.SetColumn(headerActions, 1);
            header.Children.Add(headerActions);
            return header;
        }

        private void AddNav(Panel parent, string name, string description)
        {
            Button button = new Button
            {
                Tag = name,
                Style = ThemedButtonStyle(false),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(11, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = System.Windows.Input.Cursors.Hand,
                FocusVisualStyle = null
            };
            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock glyph = new TextBlock
            {
                Text = NavGlyph(name),
                Foreground = Brush("#6F7E92"),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(glyph);
            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock { Text = name, Foreground = MutedBrush, FontSize = 12, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = description, Foreground = Brush("#647187"), FontSize = 8, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            content.Children.Add(text);
            button.Content = content;
            button.Click += delegate(object sender, RoutedEventArgs args) { SelectPage((string)((Button)sender).Tag); };
            navigation[name] = button;
            parent.Children.Add(button);
        }

        private void SelectPage(string page)
        {
            activePage = page;
            foreach (KeyValuePair<string, Button> item in navigation)
            {
                bool selected = item.Key == page;
                item.Value.Background = selected ? Brush("#17272A") : Brushes.Transparent;
                item.Value.BorderBrush = selected ? Brush("#2E5C51") : Brushes.Transparent;
                item.Value.BorderThickness = new Thickness(1);
                Grid content = item.Value.Content as Grid;
                if (content != null && content.Children.Count >= 2)
                {
                    TextBlock glyph = content.Children[0] as TextBlock;
                    StackPanel stack = content.Children[1] as StackPanel;
                    if (glyph != null) glyph.Foreground = selected ? AccentBrush : Brush("#6F7E92");
                    if (stack != null && stack.Children.Count != 0)
                        ((TextBlock)stack.Children[0]).Foreground = selected ? AccentBrush : MutedBrush;
                }
            }
            pageTitle.Text = page.ToUpperInvariant();
            pageSubtitle.Text = PageDescription(page);
            profileDirty = false;
            RenderActivePage();
            ApplyLocalizationToTree(Content as DependencyObject, true);
            if (pageScroll != null) pageScroll.ScrollToTop();
        }

        private static string NavGlyph(string page)
        {
            if (page == PageOverview) return "\uE80F";
            if (page == PageTuning) return "\uE9D2";
            if (page == PageVoltage) return "\uE945";
            if (page == PagePower) return "\uE9D9";
            if (page == PageProfiles) return "\uE8A5";
            return "\uE946";
        }

        private static string PageDescription(string page)
        {
            if (page == PageTuning) return "核心、显存、功耗与 Boost Lock 的分项调校";
            if (page == PageVoltage) return "Blackwell 电压轨、Crossbar 与 RTX 50 V/F 曲线";
            if (page == PagePower) return "Power Monitor、Power Topology 与降频原因";
            if (page == PageProfiles) return "与 GPU/VBIOS 绑定的命名配置档和分项应用";
            if (page == PageInterfaces) return "42 个 QueryInterface 入口与实机验证状态";
            return "实时遥测、硬件身份与当前调校状态";
        }

        private void InitializeBackend()
        {
            try
            {
                nvBackend = new NvApiBackend(allowHardwareWrites);
                backend = nvBackend;
                tuningCoordinator = new SafeWriteCoordinator(nvBackend, nvBackend.HardwareWritesEnabled);
                UpdateWriteMode();
                RefreshSnapshot();
            }
            catch (Exception ex)
            {
                statusText.Text = "NVAPI 初始化失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
                gpuTitle.Text = "未连接 NVIDIA 驱动";
                gpuSubtitle.Text = "请检查驱动和 64 位 NVAPI";
                RenderActivePage();
                ApplyLocalizationToTree(Content as DependencyObject, true);
            }
        }

        private void UpdateWriteMode()
        {
            bool enabled = CanInitiateWrite;
            if (enabled)
            {
                if (writeModeValue != null)
                {
                    writeModeValue.Text = uiQaMode ? "UI 验收" : "写入可用";
                    writeModeValue.Foreground = ErrorBrush;
                    writeModeDescription.Text = uiQaMode ? "测试构建 · 硬件 SET 已硬禁用" : "管理员模式 · 常规应用确认，一键复位直接执行";
                }
            }
            else
            {
                if (writeModeValue != null)
                {
                    writeModeValue.Text = forceReadOnly ? "只读模式" : "写入不可用";
                    writeModeValue.Foreground = AccentBrush;
                    writeModeDescription.Text = forceReadOnly ? "由 --read-only 明确启用" : "请使用正式管理员构建";
                }
            }
        }

        private void InitializeProfileStore()
        {
            if (profileStore != null || snapshot == null) return;
            try
            {
                profileStore = new ProfileStore(snapshot.Name, snapshot.Vbios);
                profileDocument = profileStore.Load();
                pendingProfile = StartupProfilePolicy.SelectAutomaticProfile(profileDocument);
                profileError = null;
                if (profileDocument.Profiles.Count != 0)
                {
                    selectedProfileId = profileDocument.Profiles[0].Id;
                    profileNameDraft = profileDocument.Profiles[0].Name;
                }
            }
            catch (Exception ex)
            {
                profileError = ex.Message;
                profileDocument = null;
            }
        }

        private void RefreshSnapshot()
        {
            if (backend == null) return;
            refreshButton.IsEnabled = false;
            try
            {
                GpuSnapshot sampled = backend.Read();
                bool firstSample = !startupBaselineCaptured;
                if (firstSample)
                {
                    // Startup is deliberately GET-only. Saved profiles remain inert until
                    // the user explicitly loads or applies one from the Profiles page.
                    pendingProfile = null;
                    stagedVfChanges.Clear();
                    voltageDraftInitialized = false;
                    startupBaselineCaptured = true;
                }
                snapshot = sampled;
                InitializeProfileStore();
                if (resetAllButton != null) resetAllButton.IsEnabled = CanInitiateWrite;
                gpuTitle.Text = snapshot.Name;
                gpuSubtitle.Text = "驱动 " + snapshot.Driver + "  ·  " + snapshot.DriverBranch + "  ·  VBIOS " + snapshot.Vbios;
                statusText.Text = firstSample
                    ? "启动 GET 基线已读取 · 未自动应用任何保存配置；" + BuildStatus(snapshot)
                    : BuildStatus(snapshot);
                statusText.Foreground = HasErrors(snapshot) ? WarningBrush : AccentBrush;
                timestampText.Text = "最后采样 " + snapshot.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ·  2 秒刷新";
                bool preserveEditor =
                    activePage == PageTuning ||
                    activePage == PageVoltage ||
                    (activePage == PageProfiles && profileDirty);
                if (!preserveEditor) RenderActivePage();
                if (firstSample && startupAutoApplyRequested && !startupAutoApplyHandled)
                    Dispatcher.BeginInvoke(new Action(ApplyStartupProfileIfRequested));
            }
            catch (Exception ex)
            {
                if (resetAllButton != null) resetAllButton.IsEnabled = false;
                statusText.Text = "刷新失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
            finally
            {
                refreshButton.IsEnabled = true;
                ApplyLocalizationToTree(Content as DependencyObject, true);
            }
        }

        private void ApplyStartupProfileIfRequested()
        {
            if (startupAutoApplyHandled) return;
            startupAutoApplyHandled = true;
            MVoltProfile profile = StartupProfilePolicy.SelectAutomaticProfile(profileDocument, startupAutoApplyRequested);
            if (profile == null)
            {
                statusText.Text = "开机自动应用未执行：未找到已启用的启动配置档。";
                statusText.Foreground = WarningBrush;
                return;
            }
            if (nvBackend == null || !nvBackend.HardwareWritesEnabled)
            {
                statusText.Text = "开机自动应用未执行：管理员写入模式不可用。";
                statusText.Foreground = ErrorBrush;
                return;
            }
            try
            {
                BestEffortWriteResult result = nvBackend.ApplyProfileVerified(profile);
                statusText.Text = result.HasFailures
                    ? "开机配置档已部分应用：成功 " + result.SuccessfulLabels() + "；失败 " + result.FailureDetails()
                    : "开机配置档已应用并回读：" + profile.Name;
                statusText.Foreground = result.HasFailures ? WarningBrush : AccentBrush;
                voltageDraftInitialized = false;
                snapshot = backend.Read();
                RenderActivePage();
            }
            catch (Exception ex)
            {
                statusText.Text = "开机自动应用失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
        }

        private static bool HasErrors(GpuSnapshot sample)
        {
            return sample.Tuning.Errors.Count != 0 ||
                !string.IsNullOrEmpty(sample.VfError) ||
                !string.IsNullOrEmpty(sample.Xbar.Error) ||
                !string.IsNullOrEmpty(sample.SysClock.Error) ||
                !string.IsNullOrEmpty(sample.VideoClock.Error) ||
                !string.IsNullOrEmpty(sample.FanControlError) ||
                !string.IsNullOrEmpty(sample.VoltageError) ||
                !string.IsNullOrEmpty(sample.AdcError) ||
                !string.IsNullOrEmpty(sample.PowerTelemetryError);
        }

        private static string BuildStatus(GpuSnapshot sample)
        {
            List<string> parts = new List<string>();
            parts.Add(sample.Status);
            if (sample.Tuning.Errors.Count != 0) parts.Add("调校 GET: " + string.Join(" | ", new List<string>(sample.Tuning.Errors).ToArray()));
            if (!string.IsNullOrEmpty(sample.VfError)) parts.Add("V/F: " + sample.VfError);
            if (!string.IsNullOrEmpty(sample.Xbar.Error)) parts.Add("Crossbar: " + sample.Xbar.Error);
            if (!string.IsNullOrEmpty(sample.VoltageError)) parts.Add("电压轨: " + sample.VoltageError);
            if (!string.IsNullOrEmpty(sample.AdcError)) parts.Add("ADC: " + sample.AdcError);
            if (!string.IsNullOrEmpty(sample.PowerTelemetryError)) parts.Add("功耗遥测: " + sample.PowerTelemetryError);
            return string.Join("；", parts.ToArray());
        }

        private void RenderActivePage()
        {
            if (pageHost == null) return;
            if (activePage == PageTuning) pageHost.Content = BuildTuningPage();
            else if (activePage == PageVoltage) pageHost.Content = BuildVoltagePage();
            else if (activePage == PagePower) pageHost.Content = BuildPowerPage();
            else if (activePage == PageProfiles) pageHost.Content = BuildProfilesPage();
            else if (activePage == PageInterfaces) pageHost.Content = BuildInterfacesPage();
            else pageHost.Content = BuildOverviewPage();
            ApplyLocalizationToTree(pageHost, true);
        }

        private UIElement BuildOverviewPage()
        {
            StackPanel page = PageStack();
            if (snapshot == null)
            {
                page.Children.Add(EmptyState("等待第一次 NVAPI 采样…"));
                return page;
            }

            page.Children.Add(BuildHomeHero());

            UniformGrid primary = new UniformGrid { Columns = 4 };
            primary.Children.Add(Metric("整板功耗", PowerMonitorBoard(snapshot), "Power Monitor"));
            primary.Children.Add(Metric("温度", snapshot.TemperatureC.HasValue ? snapshot.TemperatureC.Value + " °C" : "—", "GPU thermal target"));
            primary.Children.Add(Metric("核心时钟", Format(snapshot.CoreClockMHz, "MHz"), "当前 graphics domain"));
            primary.Children.Add(Metric("P-State", snapshot.PState, "当前性能状态"));
            page.Children.Add(primary);

            page.Children.Add(SectionHeading("硬件身份", "PCI、总线与架构信息"));
            UniformGrid identity = new UniformGrid { Columns = 4 };
            identity.Children.Add(Metric("PCI Device", HexValue(snapshot.PciDeviceId), "Subsystem " + HexValue(snapshot.PciSubsystemId)));
            identity.Children.Add(Metric("PCI 位置", "Bus " + OptionalUInt(snapshot.BusId), "Slot " + OptionalUInt(snapshot.BusSlotId)));
            identity.Children.Add(Metric("GPU 架构", HexValue(snapshot.ArchitectureId), "Impl " + HexValue(snapshot.ArchitectureImplementationId) + " · Rev " + HexValue(snapshot.ArchitectureRevision)));
            identity.Children.Add(Metric("物理 framebuffer", snapshot.PhysicalFrameBufferKiB.HasValue ? (snapshot.PhysicalFrameBufferKiB.Value / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " MiB" : "—", "NvAPI_GPU_GetPhysicalFrameBufferSize"));
            page.Children.Add(identity);

            page.Children.Add(SectionHeading("频率与显存", "驱动实时值与当前偏移"));
            UniformGrid clocks = new UniformGrid { Columns = 4 };
            clocks.Children.Add(Metric("显存时钟", Format(snapshot.MemoryClockMHz, "MHz"), "memory domain"));
            clocks.Children.Add(Metric("视频时钟", Format(snapshot.VideoClockMHz, "MHz"), "video domain"));
            clocks.Children.Add(Metric("核心偏移", TuningValue(snapshot.Tuning.CoreOffsetMHz, "MHz"), TuningRange(snapshot.Tuning.CoreMinimumMHz, snapshot.Tuning.CoreMaximumMHz, "MHz")));
            clocks.Children.Add(Metric("显存偏移", TuningValue(snapshot.Tuning.MemoryOffsetMHz, "MHz"), TuningRange(snapshot.Tuning.MemoryMinimumMHz, snapshot.Tuning.MemoryMaximumMHz, "MHz")));
            clocks.Children.Add(Metric("Crossbar 偏移", XbarOffset(snapshot), XbarSummary(snapshot)));
            clocks.Children.Add(Metric("SYS Clock 偏移", ClockDomainOffset(snapshot.SysClock), ClockDomainSummary(snapshot.SysClock, "SYS")));
            clocks.Children.Add(Metric("Video Clock 偏移", ClockDomainOffset(snapshot.VideoClock), ClockDomainSummary(snapshot.VideoClock, "Video")));
            clocks.Children.Add(Metric("风扇控制", snapshot.Fans.Count == 0 ? "—" : snapshot.Fans.Count + " 路", snapshot.FanControlError ?? "ClientFanCoolers"));
            clocks.Children.Add(Metric("V/F 曲线", snapshot.VfPoints.Count == 0 ? "—" : snapshot.VfPoints.Count + " 点", VfSummary(snapshot)));
            clocks.Children.Add(Metric("专用显存", Format(snapshot.DedicatedMemoryMiB, "MiB"), "物理 framebuffer"));
            clocks.Children.Add(Metric("当前可用", Format(snapshot.AvailableMemoryMiB, "MiB"), "可分配显存"));
            page.Children.Add(clocks);

            page.Children.Add(SectionHeading("电气状态", "Blackwell 电压轨和驱动限制"));
            UniformGrid electrical = new UniformGrid { Columns = 4 };
            electrical.Children.Add(Metric("NVVDD 感测", RailValue(snapshot, 0), RailSummary(snapshot, 0)));
            electrical.Children.Add(Metric("MSVDD 感测", RailValue(snapshot, 1), RailSummary(snapshot, 1)));
            electrical.Children.Add(Metric("功耗上限", TuningValue(snapshot.Tuning.PowerPercent, "%"), TuningRange(snapshot.Tuning.PowerMinimumPercent, snapshot.Tuning.PowerMaximumPercent, "%")));
            electrical.Children.Add(Metric("Voltage Boost", snapshot.Voltage == null ? "—" : snapshot.Voltage.VoltageBoostPercent + "%", "VoltRails Control"));
            electrical.Children.Add(Metric("Boost Lock", snapshot.Tuning.BoostLockEnabled.HasValue ? (snapshot.Tuning.BoostLockEnabled.Value ? "已开启" : "已关闭") : "—", "PerfClientLimits domain 6"));
            electrical.Children.Add(Metric("驱动芯片功耗", TopologyPower(snapshot, true), "ClientPowerTopology ID 0"));
            electrical.Children.Add(Metric("驱动整板功耗", TopologyPower(snapshot, false), "ClientPowerTopology ID 1"));
            electrical.Children.Add(Metric("会话能量", SessionEnergy(snapshot), "相对启动时累计"));
            page.Children.Add(electrical);

            if (snapshot.PowerTelemetry != null && snapshot.PowerTelemetry.InsufficientExternalPower == true)
                page.Children.Add(Alert("检测到外接供电不足", "GetPerfDecreaseInfo 的 INSUFFICIENT_POWER 位已置位。", true));
            return page;
        }

        private UIElement BuildHomeHero()
        {
            Grid hero = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border identity = new Border
            {
                Background = PanelBrush,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 20, 22, 20),
                Margin = new Thickness(0, 0, 8, 0)
            };
            StackPanel identityStack = new StackPanel();
            StackPanel badges = new StackPanel { Orientation = Orientation.Horizontal };
            badges.Children.Add(Pill("NV VOLTELLE", AccentDarkBrush, AccentBrush));
            badges.Children.Add(Pill("LIVE · " + (snapshot.PState ?? "—"), Brush("#24213D"), SecondaryAccentBrush));
            identityStack.Children.Add(badges);
            identityStack.Children.Add(new TextBlock
            {
                Text = snapshot.Name,
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 17, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            identityStack.Children.Add(new TextBlock
            {
                Text = "驱动 " + snapshot.Driver + "  ·  VBIOS " + snapshot.Vbios,
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 7, 0, 0)
            });
            identityStack.Children.Add(new Border { Height = 1, Background = StrokeBrush, Margin = new Thickness(0, 18, 0, 14) });
            Grid attribution = new Grid();
            attribution.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            attribution.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            attribution.Children.Add(new TextBlock
            {
                Text = "制作者  " + VoltelleBrand.Maker + "   ·   B站 @" + VoltelleBrand.BilibiliId,
                Foreground = Brush("#CDD5E2"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            Border freeBadge = Pill(VoltelleBrand.FreeNotice, Brush("#202D29"), AccentBrush);
            Grid.SetColumn(freeBadge, 1);
            attribution.Children.Add(freeBadge);
            identityStack.Children.Add(attribution);
            identity.Child = identityStack;
            hero.Children.Add(identity);

            Border risk = new Border
            {
                Background = Brush("#251A16"),
                BorderBrush = Brush("#68432A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Margin = new Thickness(8, 0, 0, 0)
            };
            StackPanel riskStack = new StackPanel();
            riskStack.Children.Add(new TextBlock { Text = "使用前请确认", Foreground = WarningBrush, FontSize = 10, FontWeight = FontWeights.SemiBold });
            riskStack.Children.Add(new TextBlock
            {
                Text = VoltelleBrand.RiskNotice,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            riskStack.Children.Add(new TextBlock
            {
                Text = "建议一次只调整一个变量，小步推进，并在负载下完成稳定性验证。常规调校会在应用前确认；一键复位直接执行。",
                Foreground = Brush("#D2B69C"),
                FontSize = 10,
                Margin = new Thickness(0, 11, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            });
            riskStack.Children.Add(new TextBlock
            {
                Text = "当前验证范围：仅限 GeForce RTX 5070 Ti 及以上的桌面端与移动端显卡。其他型号尚未验证。",
                Foreground = WarningBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            });
            risk.Child = riskStack;
            Grid.SetColumn(risk, 1);
            hero.Children.Add(risk);
            return hero;
        }

        private static Border Pill(string text, Brush background, Brush foreground)
        {
            Border pill = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 7, 0)
            };
            pill.Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 9, FontWeight = FontWeights.SemiBold };
            return pill;
        }

        private UIElement BuildTuningPage()
        {
            StackPanel page = PageStack();
            bool writesEnabled = CanInitiateWrite;
            page.Children.Add(Alert(
                writesEnabled ? "分项写入可用" : "当前运行保持只读",
                writesEnabled
                    ? "点击应用后会先显示确认。核心、显存、功耗和 Boost Lock 分别写入并回读；某项失败不会撤销成功项，也不会阻止后续项目。"
                    : "当前仅显示驱动读数，应用按钮不可用，也不会调用 SET。",
                !writesEnabled));
            if (pendingProfile != null)
            {
                page.Children.Add(Alert(
                    "已载入配置档目标 · " + pendingProfile.Name,
                    "已启用的核心、显存和功耗字段会显示为配置档目标；Boost Lock 不属于 mvolt.profile.v1，继续显示当前驱动值。",
                    false));
            }
            bool coreAvailable = snapshot != null && snapshot.Tuning.CoreOffsetMHz.HasValue && snapshot.Tuning.CoreMinimumMHz.HasValue && snapshot.Tuning.CoreMaximumMHz.HasValue;
            bool memoryAvailable = snapshot != null && snapshot.Tuning.MemoryOffsetMHz.HasValue && snapshot.Tuning.MemoryMinimumMHz.HasValue && snapshot.Tuning.MemoryMaximumMHz.HasValue;
            bool powerAvailable = snapshot != null && snapshot.Tuning.PowerPercent.HasValue && snapshot.Tuning.PowerMinimumPercent.HasValue && snapshot.Tuning.PowerMaximumPercent.HasValue;
            bool boostAvailable = snapshot != null && snapshot.Tuning.BoostLockEnabled.HasValue;

            Grid controls = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int? coreTarget = snapshot == null ? null : snapshot.Tuning.CoreOffsetMHz;
            int? memoryTarget = snapshot == null ? null : snapshot.Tuning.MemoryOffsetMHz;
            int? powerTarget = snapshot == null ? null : snapshot.Tuning.PowerPercent;
            if (pendingProfile != null)
            {
                if (pendingProfile.Controls.Core.Enabled) coreTarget = pendingProfile.Controls.Core.OffsetMHz;
                if (pendingProfile.Controls.Memory.Enabled) memoryTarget = pendingProfile.Controls.Memory.OffsetMHz;
                if (pendingProfile.Controls.Power.Enabled) powerTarget = pendingProfile.Controls.Power.Percent;
            }
            coreInput = NumericControl(controls, 0, 0, "核心频率偏移", "MHz", coreTarget, snapshot == null ? null : snapshot.Tuning.CoreMinimumMHz, snapshot == null ? null : snapshot.Tuning.CoreMaximumMHz, coreAvailable);
            memoryInput = NumericControl(controls, 1, 0, "显存频率偏移", "MHz", memoryTarget, snapshot == null ? null : snapshot.Tuning.MemoryMinimumMHz, snapshot == null ? null : snapshot.Tuning.MemoryMaximumMHz, memoryAvailable);
            powerInput = NumericControl(controls, 0, 1, "功耗上限", "%", powerTarget, snapshot == null ? null : snapshot.Tuning.PowerMinimumPercent, snapshot == null ? null : snapshot.Tuning.PowerMaximumPercent, powerAvailable);

            Border boostCard = CardShell();
            boostCard.Margin = new Thickness(7, 7, 0, 0);
            StackPanel boostStack = new StackPanel();
            boostStack.Children.Add(new TextBlock { Text = "Boost Lock", Foreground = MutedBrush, FontSize = 11 });
            boostInput = new CheckBox
            {
                Content = "锁定手动 Boost 电压域",
                IsChecked = snapshot != null && snapshot.Tuning.BoostLockEnabled == true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 17, 0, 12),
                FontSize = 13
            };
            boostStack.Children.Add(boostInput);
            boostStack.Children.Add(new TextBlock
            {
                Text = "PerfClientLimits domain 6 使用 1,500,000 µV 控制哨兵；该数值不是工作电压。",
                Foreground = Brush("#667891"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
            boostCard.Child = boostStack;
            ApplyUnavailableOverlay(boostCard, boostAvailable, "Boost Lock 接口或状态不可用");
            Grid.SetColumn(boostCard, 1);
            Grid.SetRow(boostCard, 1);
            controls.Children.Add(boostCard);
            page.Children.Add(controls);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Button reset = SecondaryButton("恢复当前读数");
            reset.Click += delegate { pendingProfile = null; RenderActivePage(); };
            actions.Children.Add(reset);
            applyTuningButton = PrimaryButton("应用并验证");
            applyTuningButton.Margin = new Thickness(10, 0, 0, 0);
            applyTuningButton.IsEnabled = writesEnabled && coreAvailable && memoryAvailable && powerAvailable && boostAvailable;
            applyTuningButton.Click += delegate { ApplyTuning(); };
            actions.Children.Add(applyTuningButton);
            page.Children.Add(actions);

            page.Children.Add(SectionHeading("高级分项写入", "常规应用先确认；一键复位直接执行。每项写入后独立回读，失败不回退成功项"));
            UniformGrid pending = new UniformGrid { Columns = 3 };
            pending.Children.Add(Metric("Voltage Boost", snapshot == null || snapshot.Voltage == null ? "—" : snapshot.Voltage.VoltageBoostPercent + "%", "目标范围 0..100% · 写后回读"));
            pending.Children.Add(Metric("Crossbar", snapshot == null ? "—" : XbarOffset(snapshot), "独立 ClockDomains 控制与回读验证"));
            pending.Children.Add(Metric("V/F 点控制", snapshot == null ? "—" : snapshot.VfPoints.Count + " 点", "单 bit mask · 逐点写入 · 失败继续"));
            page.Children.Add(pending);
            return page;
        }

        private TextBox NumericControl(Grid parent, int column, int row, string label, string unit, int? value, int? minimum, int? maximum, bool available)
        {
            Border card = CardShell();
            card.Margin = new Thickness(column == 0 ? 0 : 7, row == 0 ? 0 : 7, column == 0 ? 7 : 0, row == 0 ? 7 : 0);
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 11 });
            Grid line = new Grid { Margin = new Thickness(0, 13, 0, 9) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBox input = new TextBox
            {
                Text = value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Background = Brush("#0C131E"),
                Foreground = Brushes.White,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                FontSize = 15,
                CaretBrush = AccentBrush
            };
            line.Children.Add(input);
            TextBlock suffix = new TextBlock { Text = unit, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(suffix, 1);
            line.Children.Add(suffix);
            stack.Children.Add(line);
            AddIntegerSlider(stack, input, minimum, maximum, 1);
            stack.Children.Add(new TextBlock
            {
                Text = minimum.HasValue && maximum.HasValue ? "驱动范围 " + minimum.Value + ".." + maximum.Value + " " + unit : "驱动范围不可用",
                Foreground = Brush("#667891"),
                FontSize = 10
            });
            card.Child = stack;
            ApplyUnavailableOverlay(card, available, label + " 接口、状态或范围不可用");
            Grid.SetColumn(card, column);
            Grid.SetRow(card, row);
            parent.Children.Add(card);
            return input;
        }

        private void ApplyTuning()
        {
            if (tuningCoordinator == null || applyTuningButton == null) return;
            int core;
            int memory;
            int power;
            if (!Int32.TryParse(coreInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out core) ||
                !Int32.TryParse(memoryInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out memory) ||
                !Int32.TryParse(powerInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out power))
            {
                statusText.Text = "请输入有效的整数调校值。";
                statusText.Foreground = ErrorBrush;
                return;
            }

            TuningRequest request = new TuningRequest
            {
                CoreOffsetMHz = core,
                MemoryOffsetMHz = memory,
                PowerPercent = power,
                BoostLockEnabled = boostInput.IsChecked == true
            };
            string target = "核心 " + core + " MHz · 显存 " + memory + " MHz · 功耗 " + power + "% · Boost Lock " + (request.BoostLockEnabled ? "开启" : "关闭");
            ExecuteConfirmedBestEffortWrite(target, delegate { return tuningCoordinator.ApplyVerified(request); });
        }

        private UIElement BuildVoltagePage()
        {
            StackPanel page = PageStack();
            if (snapshot == null)
            {
                page.Children.Add(EmptyState("等待电压轨和曲线采样…"));
                return page;
            }

            InitializeVoltageDrafts();
            bool writesEnabled = CanInitiateWrite;
            if (pendingProfile != null)
            {
                page.Children.Add(Alert(
                    "已载入配置档目标 · " + pendingProfile.Name,
                    "下列输入框已采用配置档值；V/F 只把与当前曲线不同的点加入暂存。此页面不会自动执行硬件写入。",
                    false));
            }

            page.Children.Add(SectionHeading("Blackwell 电压轨", "Status 字段与 Control offset 分开显示"));
            UniformGrid rails = new UniformGrid { Columns = 2 };
            if (snapshot.Voltage != null)
            {
                for (int index = 0; index < snapshot.Voltage.Rails.Count; index++)
                    rails.Children.Add(VoltageRailCard(snapshot.Voltage.Rails[index]));
            }
            if (rails.Children.Count == 0)
                rails.Children.Add(WrapAvailability(
                    EmptyState("驱动未返回 VoltVoltRails v2 数据。"),
                    false,
                    string.IsNullOrEmpty(snapshot.VoltageError) ? "VoltVoltRails 接口不可用" : snapshot.VoltageError));
            page.Children.Add(rails);

            page.Children.Add(SectionHeading("电压轨与 Crossbar 目标", "输入只在点击应用后写入；每次应用前均需单独确认"));
            CheckBox xocToggle = new CheckBox
            {
                Content = "启用 XOC 电压范围（最高 1.25 V）",
                IsChecked = xocEnabled,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 10)
            };
            xocToggle.Click += delegate
            {
                bool requested = xocToggle.IsChecked == true;
                if (requested && MessageBox.Show(
                        VoltelleLocalization.T("XOC 模式会把配置档和电压目标的上限从 1.15 V 提高到 1.25 V。当前构建仍不会写入显卡，但未来应用该配置可能增加硬件风险。是否保留此选择？"),
                        VoltelleLocalization.T("确认 XOC 范围"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    xocToggle.IsChecked = false;
                    requested = false;
                }
                xocEnabled = requested;
                statusText.Text = xocEnabled ? "已为当前草稿启用 XOC 1.25 V 范围；尚未执行硬件写入。" : "当前草稿使用标准 1.15 V 范围。";
                statusText.Foreground = xocEnabled ? WarningBrush : AccentBrush;
            };
            page.Children.Add(xocToggle);
            if (snapshot.MobileRelOnlyCompatible)
            {
                page.Children.Add(Alert(
                    "REL-only 电压路径",
                    "GPU 名称匹配 Blackwell REL-only 兼容规则；保存的配置档会记录 mobile_rel_only=true。",
                    false));
            }
            UniformGrid voltageTargets = new UniformGrid { Columns = 2 };
            VoltageRailContract nvvdd = snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(0);
            VoltageRailContract msvdd = snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(1);
            voltageTargets.Children.Add(BuildRailTargetCard(
                "NVVDD 范围", 0, nvvdd, nvvddMinimumDraft, nvvddMaximumDraft,
                delegate(string value) { nvvddMinimumDraft = value; },
                delegate(string value) { nvvddMaximumDraft = value; },
                writesEnabled));
            voltageTargets.Children.Add(BuildRailTargetCard(
                "MSVDD 范围", 1, msvdd, msvddMinimumDraft, msvddMaximumDraft,
                delegate(string value) { msvddMinimumDraft = value; },
                delegate(string value) { msvddMaximumDraft = value; },
                writesEnabled));
            voltageTargets.Children.Add(BuildSingleTargetCard(
                "Voltage Boost", "0..100%，使用 VoltRails Control boost byte", "%", voltageBoostDraft,
                delegate(string value) { voltageBoostDraft = value; },
                0, 100, 1,
                snapshot.Voltage != null && string.IsNullOrEmpty(snapshot.VoltageError),
                writesEnabled && snapshot.Voltage != null,
                delegate { ApplyVoltageBoostTarget(); }));
            voltageTargets.Children.Add(BuildSingleTargetCard(
                "Crossbar 偏移", XbarSummary(snapshot), "MHz", xbarOffsetDraft,
                delegate(string value) { xbarOffsetDraft = value; },
                snapshot.Xbar.MinimumOffsetMHz, snapshot.Xbar.MaximumOffsetMHz, 1,
                snapshot.Xbar.CurrentOffsetKHz.HasValue && string.IsNullOrEmpty(snapshot.Xbar.Error),
                writesEnabled && snapshot.Xbar.CurrentOffsetKHz.HasValue,
                delegate { ApplyXbarTarget(); }));
            voltageTargets.Children.Add(BuildSingleTargetCard(
                "SYS Clock 偏移", ClockDomainSummary(snapshot.SysClock, "SYS"), "MHz", sysClockOffsetDraft,
                delegate(string value) { sysClockOffsetDraft = value; },
                snapshot.SysClock.MinimumOffsetMHz, snapshot.SysClock.MaximumOffsetMHz, 1,
                snapshot.SysClock.CurrentOffsetKHz.HasValue && string.IsNullOrEmpty(snapshot.SysClock.Error),
                writesEnabled && snapshot.SysClock.CurrentOffsetKHz.HasValue,
                delegate { ApplyClockDomainTarget(snapshot.SysClock, NvApiXbarLayouts.Sys, sysClockOffsetDraft); }));
            voltageTargets.Children.Add(BuildSingleTargetCard(
                "Video Clock 偏移", ClockDomainSummary(snapshot.VideoClock, "Video"), "MHz", videoClockOffsetDraft,
                delegate(string value) { videoClockOffsetDraft = value; },
                snapshot.VideoClock.MinimumOffsetMHz, snapshot.VideoClock.MaximumOffsetMHz, 1,
                snapshot.VideoClock.CurrentOffsetKHz.HasValue && string.IsNullOrEmpty(snapshot.VideoClock.Error),
                writesEnabled && snapshot.VideoClock.CurrentOffsetKHz.HasValue,
                delegate { ApplyClockDomainTarget(snapshot.VideoClock, NvApiXbarLayouts.Video, videoClockOffsetDraft); }));
            page.Children.Add(voltageTargets);

            page.Children.Add(SectionHeading("显卡风扇控制", "每个 cooler 独立切换自动/手动；手动 duty 写入后立即回读"));
            UniformGrid fanTargets = new UniformGrid { Columns = 2 };
            for (int fanIndex = 0; fanIndex < snapshot.Fans.Count; fanIndex++)
                fanTargets.Children.Add(BuildFanTargetCard(snapshot.Fans[fanIndex], writesEnabled));
            if (fanTargets.Children.Count == 0)
                fanTargets.Children.Add(WrapAvailability(
                    EmptyState("驱动未返回 ClientFanCoolers 控制通道。"),
                    false,
                    string.IsNullOrEmpty(snapshot.FanControlError) ? "ClientFanCoolers 接口不可用" : snapshot.FanControlError));
            page.Children.Add(fanTargets);

            page.Children.Add(SectionHeading("RTX 50 V/F 曲线编辑器", "单点 mask、区域 offset、锚点拉平和逐点失败继续"));
            UIElement vfEditor = BuildVfEditor(writesEnabled);
            page.Children.Add(WrapAvailability(vfEditor, snapshot.VfPoints.Count != 0 && string.IsNullOrEmpty(snapshot.VfError), "V/F 接口、点表或范围不可用"));

            Border chartCard = CardShell();
            StackPanel chartStack = new StackPanel();
            vfChartSummaryText = new TextBlock
            {
                Text = snapshot.VfPoints.Count == 0
                    ? "曲线不可用"
                    : snapshot.VfPoints.Count + " 个有效点 · " + stagedVfChanges.Count + " 个暂存变更 · " + VfSummary(snapshot),
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            };
            chartStack.Children.Add(vfChartSummaryText);
            chartStack.Children.Add(new TextBlock
            {
                Text = "右键拖框多选；左键拖动任一已选点可整组上下平移。左右键单选，上下键 ±1 MHz，Shift+上下 ±15 MHz。所有改动仅进入暂存。",
                Foreground = Brush("#718198"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            chartStack.Children.Add(BuildVfChart(snapshot.VfPoints, stagedVfChanges, TargetCoreOffsetMHz()));
            chartCard.Child = chartStack;
            page.Children.Add(chartCard);

            page.Children.Add(SectionHeading("ADC 校正电压", "raw code 无效时仍保留驱动给出的 corrected µV"));
            UniformGrid adcGrid = new UniformGrid { Columns = 4 };
            if (snapshot.Adc != null)
            {
                for (int index = 0; index < snapshot.Adc.Devices.Count; index++)
                {
                    AdcDeviceContract adc = snapshot.Adc.Devices[index];
                    adcGrid.Children.Add(Metric(
                        adc.DomainName == "XBAR" ? "Crossbar" : adc.DomainName,
                        (adc.CorrectedVoltageUv / 1000.0).ToString("N2", CultureInfo.InvariantCulture) + " mV",
                        "device " + adc.DeviceIndex + " · fuse " + adc.FuseOffset + "/" + adc.FuseGain));
                }
            }
            page.Children.Add(adcGrid);
            return page;
        }

        private void InitializeVoltageDrafts()
        {
            if (voltageDraftInitialized || snapshot == null) return;
            stagedVfChanges.Clear();

            VoltageRailContract nvvdd = snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(0);
            VoltageRailContract msvdd = snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(1);
            nvvddMinimumDraft = RailMillivolts(nvvdd == null ? (uint?)null : nvvdd.MinimumLimitUv);
            nvvddMaximumDraft = RailMillivolts(nvvdd == null ? (uint?)null : nvvdd.MaximumLimitUv);
            msvddMinimumDraft = RailMillivolts(msvdd == null ? (uint?)null : msvdd.MinimumLimitUv);
            msvddMaximumDraft = RailMillivolts(msvdd == null ? (uint?)null : msvdd.MaximumLimitUv);
            voltageBoostDraft = snapshot.Voltage == null
                ? string.Empty
                : snapshot.Voltage.VoltageBoostPercent.ToString(CultureInfo.InvariantCulture);
            xbarOffsetDraft = snapshot.Xbar.CurrentOffsetKHz.HasValue
                ? (snapshot.Xbar.CurrentOffsetKHz.Value / 1000).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            sysClockOffsetDraft = snapshot.SysClock.CurrentOffsetKHz.HasValue
                ? (snapshot.SysClock.CurrentOffsetKHz.Value / 1000).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            videoClockOffsetDraft = snapshot.VideoClock.CurrentOffsetKHz.HasValue
                ? (snapshot.VideoClock.CurrentOffsetKHz.Value / 1000).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            fanDutyDrafts.Clear();
            fanManualDrafts.Clear();
            for (int fanIndex = 0; fanIndex < snapshot.Fans.Count; fanIndex++)
            {
                FanSnapshot fan = snapshot.Fans[fanIndex];
                fanDutyDrafts[fan.CoolerId] = fan.CurrentDutyPercent.ToString(CultureInfo.InvariantCulture);
                fanManualDrafts[fan.CoolerId] = fan.Manual;
            }

            if (snapshot.VfPoints.Count != 0)
            {
                VfPointSnapshot first = snapshot.VfPoints[0];
                VfPointSnapshot last = snapshot.VfPoints[snapshot.VfPoints.Count - 1];
                vfPointDraft = first.Index.ToString(CultureInfo.InvariantCulture);
                vfSelectedPointIndex = first.Index;
                vfSelectedPointIndices.Clear();
                vfSelectedPointIndices.Add(first.Index);
                vfTargetDraft = Math.Round((first.BaseFrequencyKHz + first.FrequencyOffsetKHz) / 1000.0).ToString(CultureInfo.InvariantCulture);
                vfRegionStartDraft = first.Index.ToString(CultureInfo.InvariantCulture);
                vfRegionEndDraft = last.Index.ToString(CultureInfo.InvariantCulture);
                vfRegionOffsetDraft = "0";
            }

            MVoltProfile profile = pendingProfile;
            if (profile != null)
            {
                xocEnabled = profile.Xoc;
                if (profile.Controls.Nvvdd.Enabled)
                {
                    nvvddMinimumDraft = profile.Controls.Nvvdd.MinimumMv.ToString(CultureInfo.InvariantCulture);
                    nvvddMaximumDraft = profile.Controls.Nvvdd.MaximumMv.ToString(CultureInfo.InvariantCulture);
                }
                if (profile.Controls.Msvdd.Enabled)
                {
                    msvddMinimumDraft = profile.Controls.Msvdd.MinimumMv.ToString(CultureInfo.InvariantCulture);
                    msvddMaximumDraft = profile.Controls.Msvdd.MaximumMv.ToString(CultureInfo.InvariantCulture);
                }
                if (profile.Controls.VoltageBoost.Enabled)
                    voltageBoostDraft = profile.Controls.VoltageBoost.Percent.ToString(CultureInfo.InvariantCulture);
                if (profile.Controls.Xbar.Enabled)
                    xbarOffsetDraft = profile.Controls.Xbar.OffsetMHz.ToString(CultureInfo.InvariantCulture);
                if (profile.Controls.SysClock.Enabled)
                    sysClockOffsetDraft = profile.Controls.SysClock.OffsetMHz.ToString(CultureInfo.InvariantCulture);
                if (profile.Controls.VideoClock.Enabled)
                    videoClockOffsetDraft = profile.Controls.VideoClock.OffsetMHz.ToString(CultureInfo.InvariantCulture);
                for (int fanIndex = 0; fanIndex < profile.Controls.Fans.Count; fanIndex++)
                {
                    ProfileFanControl fan = profile.Controls.Fans[fanIndex];
                    if (!fan.Enabled) continue;
                    fanDutyDrafts[fan.CoolerId] = fan.DutyPercent.ToString(CultureInfo.InvariantCulture);
                    fanManualDrafts[fan.CoolerId] = fan.Manual;
                }
                if (profile.VfCurveOffsetsKHz.Count == snapshot.VfPoints.Count)
                {
                    for (int index = 0; index < snapshot.VfPoints.Count; index++)
                    {
                        if (profile.VfCurveOffsetsKHz[index] == snapshot.VfPoints[index].FrequencyOffsetKHz) continue;
                        stagedVfChanges.Add(new VfOffsetChange
                        {
                            Index = snapshot.VfPoints[index].Index,
                            FrequencyOffsetKHz = profile.VfCurveOffsetsKHz[index]
                        });
                    }
                }
            }
            voltageDraftInitialized = true;
        }

        private static string RailMillivolts(uint? valueUv)
        {
            return valueUv.HasValue ? (valueUv.Value / 1000U).ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private UIElement BuildRailTargetCard(
            string title,
            int railIndex,
            VoltageRailContract rail,
            string minimumDraft,
            string maximumDraft,
            Action<string> minimumChanged,
            Action<string> maximumChanged,
            bool writesEnabled)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            Grid fields = new Grid { Margin = new Thickness(0, 12, 0, 9) };
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int maximumMillivolts = xocEnabled ? 1250 : 1150;
            UIElement minimum = BuildVoltageField("MIN", "mV", minimumDraft, minimumChanged, 250, maximumMillivolts, 5);
            UIElement maximum = BuildVoltageField("MAX", "mV", maximumDraft, maximumChanged, 250, maximumMillivolts, 5);
            fields.Children.Add(minimum);
            Grid.SetColumn(maximum, 1);
            fields.Children.Add(maximum);
            stack.Children.Add(fields);
            stack.Children.Add(new TextBlock
            {
                Text = rail == null
                    ? "当前电压轨不可用"
                    : "当前 " + (rail.MinimumLimitUv / 1000U) + ".." + (rail.MaximumLimitUv / 1000U) + " mV · 5 mV 步进",
                Foreground = Brush("#667891"),
                FontSize = 10
            });
            Button apply = PrimaryButton("应用并回读");
            apply.HorizontalAlignment = HorizontalAlignment.Right;
            apply.Margin = new Thickness(0, 12, 0, 0);
            apply.IsEnabled = writesEnabled && rail != null;
            apply.Click += delegate { ApplyVoltageRailTarget(railIndex); };
            stack.Children.Add(apply);
            card.Child = stack;
            return WrapAvailability(card, rail != null, title + " 接口、状态或范围不可用");
        }

        private UIElement BuildSingleTargetCard(
            string title,
            string hint,
            string unit,
            string value,
            Action<string> changed,
            int? minimum,
            int? maximum,
            int tick,
            bool available,
            bool canApply,
            Action applyAction)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(BuildVoltageField("目标", unit, value, changed, minimum, maximum, tick));
            stack.Children.Add(new TextBlock { Text = hint, Foreground = Brush("#667891"), FontSize = 10, TextWrapping = TextWrapping.Wrap });
            Button apply = PrimaryButton("应用并回读");
            apply.HorizontalAlignment = HorizontalAlignment.Right;
            apply.Margin = new Thickness(0, 12, 0, 0);
            apply.IsEnabled = canApply;
            apply.Click += delegate { applyAction(); };
            stack.Children.Add(apply);
            card.Child = stack;
            return WrapAvailability(card, available, title + " 接口、状态或范围不可用");
        }

        private UIElement BuildFanTargetCard(FanSnapshot fan, bool writesEnabled)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Fan " + fan.CoolerId,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            bool manual;
            if (!fanManualDrafts.TryGetValue(fan.CoolerId, out manual)) manual = fan.Manual;
            CheckBox mode = new CheckBox
            {
                Content = "手动控制",
                IsChecked = manual,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 10, 0, 2)
            };
            mode.Click += delegate { fanManualDrafts[fan.CoolerId] = mode.IsChecked == true; };
            stack.Children.Add(mode);
            string draft;
            if (!fanDutyDrafts.TryGetValue(fan.CoolerId, out draft))
                draft = fan.CurrentDutyPercent.ToString(CultureInfo.InvariantCulture);
            int minimum = checked((int)fan.MinimumDutyPercent);
            int maximum = checked((int)fan.MaximumDutyPercent);
            stack.Children.Add(BuildVoltageField(
                "目标 duty",
                "%",
                draft,
                delegate(string value) { fanDutyDrafts[fan.CoolerId] = value; },
                minimum,
                maximum,
                1));
            stack.Children.Add(new TextBlock
            {
                Text = "当前 " + fan.CurrentDutyPercent +
                    "% · " + fan.CurrentRpm + " RPM · 上限 " + fan.MaximumRpm + " RPM · " +
                    (fan.Manual ? "Manual" : "Auto"),
                Foreground = Brush("#667891"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
            Button apply = PrimaryButton("应用并回读");
            apply.HorizontalAlignment = HorizontalAlignment.Right;
            apply.Margin = new Thickness(0, 12, 0, 0);
            apply.IsEnabled = writesEnabled;
            apply.Click += delegate { ApplyFanTarget(fan); };
            stack.Children.Add(apply);
            card.Child = stack;
            return card;
        }

        private UIElement BuildVfEditor(bool writesEnabled)
        {
            Grid editors = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int? firstPointIndex = snapshot.VfPoints.Count == 0 ? (int?)null : snapshot.VfPoints[0].Index;
            int? lastPointIndex = snapshot.VfPoints.Count == 0 ? (int?)null : snapshot.VfPoints[snapshot.VfPoints.Count - 1].Index;
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border pointCard = CardShell();
            StackPanel point = new StackPanel();
            point.Children.Add(new TextBlock { Text = "单点与拉平", FontSize = 14, FontWeight = FontWeights.SemiBold });
            Grid pointFields = new Grid { Margin = new Thickness(0, 11, 0, 8) };
            pointFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pointFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement pointIndexField = BuildVoltageField("点索引", "", vfPointDraft, delegate(string value) { vfPointDraft = value; }, firstPointIndex, lastPointIndex, 1);
            vfPointIndexInput = VoltageFieldTextBox(pointIndexField);
            pointFields.Children.Add(pointIndexField);
            UIElement targetField = BuildVoltageField("目标频率", "MHz", vfTargetDraft, delegate(string value) { vfTargetDraft = value; }, 1, 6000, 1);
            vfTargetFrequencyInput = VoltageFieldTextBox(targetField);
            Grid.SetColumn(targetField, 1);
            pointFields.Children.Add(targetField);
            point.Children.Add(pointFields);
            WrapPanel pointActions = new WrapPanel();
            Button stagePoint = SecondaryButton("暂存点目标");
            stagePoint.Margin = new Thickness(0, 0, 8, 8);
            stagePoint.IsEnabled = snapshot.VfPoints.Count != 0;
            stagePoint.Click += delegate { StageVfPointTarget(); };
            pointActions.Children.Add(stagePoint);
            Button flatten = SecondaryButton("从该点向上拉平");
            flatten.Margin = new Thickness(0, 0, 8, 8);
            flatten.IsEnabled = snapshot.VfPoints.Count != 0;
            flatten.Click += delegate { StageVfFlatten(); };
            pointActions.Children.Add(flatten);
            point.Children.Add(pointActions);
            pointCard.Child = point;
            editors.Children.Add(pointCard);

            Border regionCard = CardShell();
            StackPanel region = new StackPanel();
            region.Children.Add(new TextBlock { Text = "区域 offset", FontSize = 14, FontWeight = FontWeights.SemiBold });
            Grid regionFields = new Grid { Margin = new Thickness(0, 11, 0, 8) };
            for (int column = 0; column < 3; column++) regionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regionFields.Children.Add(BuildVoltageField("起点", "", vfRegionStartDraft, delegate(string value) { vfRegionStartDraft = value; }, firstPointIndex, lastPointIndex, 1));
            UIElement endField = BuildVoltageField("终点", "", vfRegionEndDraft, delegate(string value) { vfRegionEndDraft = value; }, firstPointIndex, lastPointIndex, 1);
            Grid.SetColumn(endField, 1);
            regionFields.Children.Add(endField);
            UIElement offsetField = BuildVoltageField("offset", "MHz", vfRegionOffsetDraft, delegate(string value) { vfRegionOffsetDraft = value; }, -1000, 1000, 1);
            Grid.SetColumn(offsetField, 2);
            regionFields.Children.Add(offsetField);
            region.Children.Add(regionFields);
            Button stageRegion = SecondaryButton("暂存区域");
            stageRegion.HorizontalAlignment = HorizontalAlignment.Left;
            stageRegion.IsEnabled = snapshot.VfPoints.Count != 0;
            stageRegion.Click += delegate { StageVfRegion(); };
            region.Children.Add(stageRegion);
            regionCard.Child = region;
            Grid.SetColumn(regionCard, 1);
            editors.Children.Add(regionCard);

            Border actionCard = CardShell();
            actionCard.Margin = new Thickness(0, 0, 0, 10);
            StackPanel action = new StackPanel();
            action.Children.Add(new TextBlock { Text = "暂存修改", FontSize = 14, FontWeight = FontWeights.SemiBold });
            vfStagedCountText = new TextBlock
            {
                Text = stagedVfChanges.Count + " 个点待应用",
                Foreground = stagedVfChanges.Count == 0 ? MutedBrush : WarningBrush,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 5)
            };
            action.Children.Add(vfStagedCountText);
            action.Children.Add(new TextBlock
            {
                Text = "暂存仅修改预览。真实 SET 按单点发送；某点失败后继续处理剩余点，成功点保持生效。",
                Foreground = Brush("#667891"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
            WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            Button reset = SecondaryButton("暂存全曲线归零");
            reset.Margin = new Thickness(0, 0, 8, 8);
            reset.IsEnabled = snapshot.VfPoints.Count != 0;
            reset.Click += delegate { StageVfReset(); };
            actions.Children.Add(reset);
            Button discard = SecondaryButton("放弃暂存");
            discard.Margin = new Thickness(0, 0, 8, 8);
            discard.IsEnabled = true;
            discard.Click += delegate { DiscardVoltageDrafts(); };
            actions.Children.Add(discard);
            applyStagedVfButton = PrimaryButton("逐点应用并验证");
            applyStagedVfButton.Margin = new Thickness(0, 0, 8, 8);
            applyStagedVfButton.IsEnabled = writesEnabled && stagedVfChanges.Count != 0;
            applyStagedVfButton.Click += delegate { ApplyStagedVf(); };
            actions.Children.Add(applyStagedVfButton);
            action.Children.Add(actions);
            actionCard.Child = action;
            Grid.SetColumn(actionCard, 2);
            editors.Children.Add(actionCard);
            return editors;
        }

        private static TextBox VoltageFieldTextBox(UIElement field)
        {
            StackPanel stack = field as StackPanel;
            if (stack == null || stack.Children.Count < 2) return null;
            Grid line = stack.Children[1] as Grid;
            if (line == null || line.Children.Count == 0) return null;
            return line.Children[0] as TextBox;
        }

        private UIElement BuildVoltageField(string label, string unit, string value, Action<string> changed, int? minimum, int? maximum, int tick)
        {
            StackPanel field = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            field.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 9, Margin = new Thickness(0, 0, 0, 4) });
            Grid line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBox input = new TextBox
            {
                Text = value ?? string.Empty,
                Background = Brush("#0C131E"),
                Foreground = Brushes.White,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                CaretBrush = AccentBrush,
                MinWidth = 58
            };
            input.TextChanged += delegate
            {
                changed(input.Text);
            };
            line.Children.Add(input);
            if (!string.IsNullOrEmpty(unit))
            {
                TextBlock suffix = new TextBlock { Text = unit, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontSize = 9 };
                Grid.SetColumn(suffix, 1);
                line.Children.Add(suffix);
            }
            field.Children.Add(line);
            AddIntegerSlider(field, input, minimum, maximum, tick);
            return field;
        }

        private static void AddIntegerSlider(Panel parent, TextBox input, int? minimum, int? maximum, int tick)
        {
            if (!minimum.HasValue || !maximum.HasValue || minimum.Value >= maximum.Value) return;
            int step = Math.Max(1, tick);
            int parsed;
            double initial = minimum.Value;
            if (TryParseInteger(input.Text, out parsed) && parsed >= minimum.Value && parsed <= maximum.Value)
                initial = SnapInteger(parsed, minimum.Value, maximum.Value, step);

            Slider slider = new Slider
            {
                Minimum = minimum.Value,
                Maximum = maximum.Value,
                Value = initial,
                TickFrequency = step,
                SmallChange = step,
                LargeChange = Math.Max(step, (maximum.Value - minimum.Value) / 20),
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                AutoToolTipPlacement = AutoToolTipPlacement.TopLeft,
                AutoToolTipPrecision = 0,
                Foreground = AccentBrush,
                Background = StrokeBrush,
                Height = 22,
                Margin = new Thickness(0, 2, 0, 6)
            };
            bool synchronizing = false;
            slider.ValueChanged += delegate
            {
                if (synchronizing) return;
                synchronizing = true;
                int snapped = SnapInteger((int)Math.Round(slider.Value), minimum.Value, maximum.Value, step);
                slider.Value = snapped;
                input.Text = snapped.ToString(CultureInfo.InvariantCulture);
                synchronizing = false;
            };
            input.TextChanged += delegate
            {
                int value;
                if (synchronizing || !TryParseInteger(input.Text, out value) || value < minimum.Value || value > maximum.Value) return;
                synchronizing = true;
                slider.Value = SnapInteger(value, minimum.Value, maximum.Value, step);
                synchronizing = false;
            };
            parent.Children.Add(slider);
        }

        private static int SnapInteger(int value, int minimum, int maximum, int tick)
        {
            int snapped = minimum + (int)Math.Round((value - minimum) / (double)tick) * tick;
            return Math.Max(minimum, Math.Min(maximum, snapped));
        }

        private void ApplyVoltageRailTarget(int railIndex)
        {
            string minimumText = railIndex == 0 ? nvvddMinimumDraft : msvddMinimumDraft;
            string maximumText = railIndex == 0 ? nvvddMaximumDraft : msvddMaximumDraft;
            int minimum;
            int maximum;
            if (!TryParseInteger(minimumText, out minimum) || !TryParseInteger(maximumText, out maximum))
            {
                SetUiError("电压范围必须是整数 mV。");
                return;
            }
            VoltageRailContract rail = snapshot == null || snapshot.Voltage == null ? null : snapshot.Voltage.FindRail(railIndex);
            try
            {
                if (rail == null) throw new InvalidOperationException("目标电压轨不可用。");
                NvApiVoltageLayouts.CalculateTargetOffsets(
                    rail,
                    minimum,
                    maximum,
                    xocEnabled,
                    snapshot != null && snapshot.MobileRelOnlyCompatible);
            }
            catch (Exception ex)
            {
                SetUiError("电压目标无效：" + ex.Message);
                return;
            }
            ExecuteConfirmedWrite(
                (railIndex == 0 ? "NVVDD" : "MSVDD") + " " + minimum + ".." + maximum + " mV",
                delegate
                {
                    nvBackend.ApplyVoltageRailRangeVerified(
                        railIndex,
                        minimum,
                        maximum,
                        xocEnabled,
                        snapshot != null && snapshot.MobileRelOnlyCompatible);
                });
        }

        private void ApplyVoltageBoostTarget()
        {
            int percentage;
            if (!TryParseInteger(voltageBoostDraft, out percentage) || percentage < 0 || percentage > 100)
            {
                SetUiError("Voltage Boost 必须是 0..100 的整数百分比。");
                return;
            }
            ExecuteConfirmedWrite("Voltage Boost " + percentage + "%", delegate { nvBackend.ApplyVoltageBoostVerified(percentage); });
        }

        private void ApplyXbarTarget()
        {
            int offset;
            if (!TryParseInteger(xbarOffsetDraft, out offset))
            {
                SetUiError("Crossbar offset 必须是整数 MHz。");
                return;
            }
            if (snapshot != null && snapshot.Xbar.MinimumOffsetMHz.HasValue && offset < snapshot.Xbar.MinimumOffsetMHz.Value ||
                snapshot != null && snapshot.Xbar.MaximumOffsetMHz.HasValue && offset > snapshot.Xbar.MaximumOffsetMHz.Value)
            {
                SetUiError("Crossbar offset 超出驱动报告范围。");
                return;
            }
            ExecuteConfirmedWrite("Crossbar " + offset + " MHz", delegate { nvBackend.ApplyXbarVerified(offset); });
        }

        private void ApplyClockDomainTarget(XbarSnapshot domainSnapshot, ClockDomainDescriptor domain, string draft)
        {
            int offset;
            if (!TryParseInteger(draft, out offset))
            {
                SetUiError(domain.Name + " offset 必须是整数 MHz。");
                return;
            }
            if (domainSnapshot == null || !domainSnapshot.MinimumOffsetMHz.HasValue || !domainSnapshot.MaximumOffsetMHz.HasValue ||
                offset < domainSnapshot.MinimumOffsetMHz.Value || offset > domainSnapshot.MaximumOffsetMHz.Value)
            {
                SetUiError(domain.Name + " offset 超出驱动报告范围。");
                return;
            }
            ExecuteConfirmedWrite(domain.Name + " " + offset + " MHz", delegate
            {
                if (domain == NvApiXbarLayouts.Sys) nvBackend.ApplySysClockVerified(offset);
                else if (domain == NvApiXbarLayouts.Video) nvBackend.ApplyVideoClockVerified(offset);
                else nvBackend.ApplyXbarVerified(offset);
            });
        }

        private void ApplyFanTarget(FanSnapshot fan)
        {
            if (fan == null || nvBackend == null) return;
            bool manual;
            if (!fanManualDrafts.TryGetValue(fan.CoolerId, out manual)) manual = fan.Manual;
            string text;
            if (!fanDutyDrafts.TryGetValue(fan.CoolerId, out text)) text = string.Empty;
            int percent;
            if (!TryParseInteger(text, out percent) || percent < 0 || percent > 100)
            {
                SetUiError("风扇 duty 必须是 0..100 的整数百分比。");
                return;
            }
            uint dutyPercent = checked((uint)percent);
            if (manual && (dutyPercent < fan.MinimumDutyPercent || dutyPercent > fan.MaximumDutyPercent))
            {
                SetUiError("风扇 duty 超出驱动实时范围。");
                return;
            }
            ExecuteConfirmedWrite("Fan " + fan.CoolerId + " · " + (manual ? percent + "% Manual" : "Auto"), delegate
            {
                nvBackend.ApplyFanVerified(fan.CoolerId, manual, dutyPercent);
            });
        }

        private void StageVfPointTarget()
        {
            int pointIndex;
            int targetMHz;
            if (!TryParseInteger(vfPointDraft, out pointIndex) || !TryParseInteger(vfTargetDraft, out targetMHz))
            {
                SetUiError("V/F 点索引和目标频率必须是整数。");
                return;
            }
            try
            {
                MergeVfChanges(VfCurvePlanner.PlanPointTarget(
                    BuildVfPlanningCurve(),
                    pointIndex,
                    targetMHz));
                vfSelectedPointIndices.Clear();
                vfSelectedPointIndices.Add(pointIndex);
                vfSelectedPointIndex = pointIndex;
                statusText.Text = "已暂存 V/F 点 " + pointIndex + "；未执行硬件写入。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                SetUiError("V/F 点目标无效：" + ex.Message);
            }
        }

        private void StageVfRegion()
        {
            int first;
            int last;
            int offset;
            if (!TryParseInteger(vfRegionStartDraft, out first) ||
                !TryParseInteger(vfRegionEndDraft, out last) ||
                !TryParseInteger(vfRegionOffsetDraft, out offset))
            {
                SetUiError("V/F 区域起点、终点和 offset 必须是整数。");
                return;
            }
            try
            {
                MergeVfChanges(VfCurvePlanner.PlanRegionalOffset(BuildVfPlanningCurve(), first, last, offset));
                statusText.Text = "已暂存 V/F 区域 " + first + ".." + last + "；未执行硬件写入。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                SetUiError("V/F 区域无效：" + ex.Message);
            }
        }

        private void StageVfFlatten()
        {
            int pointIndex;
            if (!TryParseInteger(vfPointDraft, out pointIndex))
            {
                SetUiError("V/F 拉平锚点必须是整数。");
                return;
            }
            try
            {
                MergeVfChanges(VfCurvePlanner.PlanFlattenAbove(
                    BuildVfPlanningCurve(),
                    pointIndex));
                statusText.Text = "已从 V/F 点 " + pointIndex + " 向上暂存拉平；未执行硬件写入。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                SetUiError("V/F 拉平无效：" + ex.Message);
            }
        }

        private void StageVfReset()
        {
            try
            {
                MergeVfChanges(VfCurvePlanner.PlanReset(snapshot.VfPoints));
                statusText.Text = "已暂存 127 点 offset 归零；未执行硬件写入。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                SetUiError("V/F 重置暂存失败：" + ex.Message);
            }
        }

        private IList<VfPointSnapshot> BuildVfPlanningCurve()
        {
            Dictionary<int, int> changes = new Dictionary<int, int>();
            for (int index = 0; index < stagedVfChanges.Count; index++)
                changes[stagedVfChanges[index].Index] = stagedVfChanges[index].FrequencyOffsetKHz;
            List<VfPointSnapshot> result = new List<VfPointSnapshot>();
            for (int index = 0; index < snapshot.VfPoints.Count; index++)
            {
                VfPointSnapshot source = snapshot.VfPoints[index];
                int offset;
                if (!changes.TryGetValue(source.Index, out offset)) offset = source.FrequencyOffsetKHz;
                result.Add(new VfPointSnapshot
                {
                    Index = source.Index,
                    VoltageUv = source.VoltageUv,
                    BaseFrequencyKHz = source.BaseFrequencyKHz,
                    ActualFrequencyKHz = source.ActualFrequencyKHz,
                    FrequencyOffsetKHz = offset
                });
            }
            return result;
        }

        private int TargetCoreOffsetMHz()
        {
            if (pendingProfile != null && pendingProfile.Controls.Core.Enabled)
                return pendingProfile.Controls.Core.OffsetMHz;
            return snapshot == null ? 0 : (snapshot.Tuning.CoreOffsetMHz ?? 0);
        }

        private void MergeVfChanges(IList<VfOffsetChange> changes)
        {
            for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
            {
                VfOffsetChange change = changes[changeIndex];
                bool replaced = false;
                for (int index = 0; index < stagedVfChanges.Count; index++)
                {
                    if (stagedVfChanges[index].Index != change.Index) continue;
                    stagedVfChanges[index] = new VfOffsetChange { Index = change.Index, FrequencyOffsetKHz = change.FrequencyOffsetKHz };
                    replaced = true;
                    break;
                }
                if (!replaced)
                    stagedVfChanges.Add(new VfOffsetChange { Index = change.Index, FrequencyOffsetKHz = change.FrequencyOffsetKHz });
            }
            stagedVfChanges.Sort(delegate(VfOffsetChange left, VfOffsetChange right) { return left.Index.CompareTo(right.Index); });
        }

        private void DiscardVoltageDrafts()
        {
            pendingProfile = null;
            stagedVfChanges.Clear();
            voltageDraftInitialized = false;
            statusText.Text = "已放弃电压、Crossbar 与 V/F 暂存，恢复当前驱动读数。";
            statusText.Foreground = AccentBrush;
            RenderActivePage();
        }

        private void ApplyStagedVf()
        {
            if (stagedVfChanges.Count == 0 || nvBackend == null) return;
            List<VfOffsetChange> requested = new List<VfOffsetChange>();
            for (int index = 0; index < stagedVfChanges.Count; index++)
                requested.Add(new VfOffsetChange { Index = stagedVfChanges[index].Index, FrequencyOffsetKHz = stagedVfChanges[index].FrequencyOffsetKHz });
            ExecuteConfirmedBestEffortWrite(requested.Count + " 个 V/F 点", delegate
            {
                BestEffortWriteResult result = nvBackend.ApplyVfOffsetsVerified(requested);
                RemoveSuccessfulVfDrafts(result);
                return result;
            });
        }

        private void RemoveSuccessfulVfDrafts(BestEffortWriteResult result)
        {
            if (result == null) return;
            VfCurveInteraction.RemoveSuccessfulDrafts(stagedVfChanges, result.SuccessfulSteps);
        }

        private void ExecuteConfirmedBestEffortWrite(string target, Func<BestEffortWriteResult> write)
        {
            ExecuteConfirmedBestEffortWrite(target, write, false);
        }

        private void ExecuteConfirmedBestEffortWrite(string target, Func<BestEffortWriteResult> write, bool resetEditorState)
        {
            if (!CanInitiateWrite || nvBackend == null)
            {
                SetUiError("当前运行处于只读模式，未发送任何硬件写入。");
                return;
            }
            if (!RiskConfirmationDialog.Show(this, target))
            {
                statusText.Text = "已取消应用；没有发送 SET。";
                statusText.Foreground = MutedBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                return;
            }
            if (uiQaMode)
            {
                statusText.Text = "UI 验收完成：确认流程可用，测试构建未发送任何 SET。";
                statusText.Foreground = AccentBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                return;
            }
            try
            {
                CompleteBestEffortWrite(target, write(), resetEditorState);
            }
            catch (Exception ex)
            {
                RefreshSnapshot();
                SetUiError(target + " 无法开始分项写入：" + ex.Message);
            }
        }

        private void ExecuteConfirmedWrite(string target, Action write)
        {
            if (!CanInitiateWrite || nvBackend == null)
            {
                SetUiError("当前运行处于只读模式，未发送任何硬件写入。");
                return;
            }
            if (!RiskConfirmationDialog.Show(this, target))
            {
                statusText.Text = "已取消应用；没有发送 SET。";
                statusText.Foreground = MutedBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                return;
            }
            if (uiQaMode)
            {
                RefreshSnapshot();
                RenderActivePage();
                statusText.Text = "UI 验收完成：单项应用刷新已执行，其他草稿保持不变；测试构建未发送任何 SET。";
                statusText.Foreground = AccentBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                return;
            }
            try
            {
                write();
                // A single-item apply must not discard the rest of the editor draft.
                // The requested field already contains the value that was just verified;
                // RefreshSnapshot updates live hints while the other uncommitted fields,
                // pending profile targets and staged V/F points remain intact.
                EditorRefreshPolicy.Apply(false, ref voltageDraftInitialized, ref pendingProfile, stagedVfChanges);
                RefreshSnapshot();
                RenderActivePage();
                statusText.Text = target + " 已应用并通过回读。";
                statusText.Foreground = AccentBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
            }
            catch (Exception ex)
            {
                RefreshSnapshot();
                SetUiError(target + " 执行或回读失败；未自动回退，当前状态以刷新后的 GET 为准：" + ex.Message);
            }
        }

        private void CompleteBestEffortWrite(string target, BestEffortWriteResult result)
        {
            CompleteBestEffortWrite(target, result, false);
        }

        private void CompleteBestEffortWrite(string target, BestEffortWriteResult result, bool resetEditorState)
        {
            if (result == null) throw new InvalidOperationException("分项写入没有返回结果。");
            EditorRefreshPolicy.Apply(resetEditorState, ref voltageDraftInitialized, ref pendingProfile, stagedVfChanges);
            RefreshSnapshot();
            RenderActivePage();
            if (result.HasFailures)
            {
                statusText.Text = target + " 分项写入完成。成功：" + result.SuccessfulLabels() + "；失败：" + result.FailureDetails() + "。未回退成功项。";
                statusText.Foreground = result.HasSuccesses ? WarningBrush : ErrorBrush;
            }
            else
            {
                statusText.Text = target + " 全部项目已应用并通过回读。";
                statusText.Foreground = AccentBrush;
            }
            ApplyLocalizationToTree(Content as DependencyObject, true);
        }

        private void ResetAllAndApplyDirect()
        {
            if (!CanInitiateWrite || nvBackend == null || snapshot == null)
            {
                SetUiError("当前运行处于只读模式，未发送任何硬件写入。");
                return;
            }
            if (uiQaMode)
            {
                statusText.Text = "UI 验收完成：一键复位可直接触发，测试构建未发送任何 SET。";
                statusText.Foreground = AccentBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                return;
            }
            try
            {
                resetAllButton.IsEnabled = false;
                BestEffortWriteResult result = nvBackend.ApplyAllDefaultsVerified();
                pendingProfile = null;
                stagedVfChanges.Clear();
                xocEnabled = false;
                voltageDraftInitialized = false;
                CompleteBestEffortWrite("一键复位", result, true);
            }
            catch (Exception ex)
            {
                RefreshSnapshot();
                SetUiError("一键复位无法开始：" + ex.Message);
            }
            finally
            {
                if (resetAllButton != null) resetAllButton.IsEnabled = CanInitiateWrite && snapshot != null;
            }
        }

        private static bool TryParseInteger(string value, out int parsed)
        {
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private void SetUiError(string message)
        {
            statusText.Text = message;
            statusText.Foreground = ErrorBrush;
            ApplyLocalizationToTree(Content as DependencyObject, true);
        }

        private static UIElement VoltageRailCard(VoltageRailContract rail)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = rail.RailIndex == 0 ? "NVVDD · Rail 0" : (rail.RailIndex == 1 ? "MSVDD · Rail 1" : "Rail " + rail.RailIndex),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            Grid values = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int column = 0; column < 4; column++) values.ColumnDefinitions.Add(new ColumnDefinition());
            AddRailValue(values, 0, 0, "SENSED", rail.SensedUv, false);
            AddRailValue(values, 1, 0, "REL", rail.ReliabilityLimitUv, false);
            AddRailValue(values, 2, 0, "ALT", rail.AlternateLimitUv, true);
            AddRailValue(values, 3, 0, "OV", rail.OvervoltageLimitUv, false);
            AddRailValue(values, 0, 1, "MAX", rail.MaximumLimitUv, false);
            AddRailValue(values, 1, 1, "MIN", rail.MinimumLimitUv, false);
            AddRailValue(values, 2, 1, "NOISE", rail.NoiseLimitUv, false);
            AddRailValue(values, 3, 1, "MARGIN", rail.MarginUv, false);
            stack.Children.Add(values);
            stack.Children.Add(new TextBlock
            {
                Text = "Control offsets µV  " + rail.PrimaryMaximumOffsetUv + " / " + rail.AlternateMaximumOffsetUv + " / " + rail.MinimumOffsetUv,
                Foreground = Brush("#667891"),
                FontSize = 10,
                Margin = new Thickness(0, 13, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        private static void AddRailValue(Grid grid, int column, int row, string label, long value, bool allowMissing)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(0, row == 0 ? 0 : 12, 8, 0) };
            stack.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 9 });
            stack.Children.Add(new TextBlock
            {
                Text = allowMissing && value == 0 ? "N/A" : (value / 1000.0).ToString("N1", CultureInfo.InvariantCulture) + " mV",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(stack, column);
            Grid.SetRow(stack, row);
            grid.Children.Add(stack);
        }

        private UIElement BuildVfChart(
            IList<VfPointSnapshot> points,
            IList<VfOffsetChange> stagedChanges,
            int globalCoreOffsetMHz)
        {
            Canvas canvas = new Canvas
            {
                Height = 430,
                Width = 980,
                Background = Brush("#0C131E"),
                ClipToBounds = true,
                Focusable = true,
                FocusVisualStyle = null,
                Cursor = Cursors.Cross
            };
            // Keyboard focus is required for arrow-key point editing, but WPF's default
            // BringIntoView can scroll a partially visible chart while the pointer is
            // captured. That changes pointer coordinates mid-drag and can cause a large,
            // unintended frequency jump.
            canvas.RequestBringIntoView += delegate(object sender, RequestBringIntoViewEventArgs args)
            {
                args.Handled = true;
            };
            if (points == null || points.Count < 2)
            {
                canvas.Children.Add(new TextBlock { Text = "没有可绘制的 V/F 点", Foreground = MutedBrush, Margin = new Thickness(18) });
                return canvas;
            }

            const double width = 980;
            const double height = 430;
            const double left = 58;
            const double right = 22;
            const double top = 24;
            const double bottom = 36;
            double plotWidth = width - left - right;
            double plotHeight = height - top - bottom;
            long globalCoreOffsetKHz = checked((long)globalCoreOffsetMHz * 1000L);
            uint minVoltage = points[0].VoltageUv;
            uint maxVoltage = points[points.Count - 1].VoltageUv;

            Dictionary<int, int> staged = new Dictionary<int, int>();
            if (stagedChanges != null)
            {
                for (int index = 0; index < stagedChanges.Count; index++)
                    staged[stagedChanges[index].Index] = stagedChanges[index].FrequencyOffsetKHz;
            }

            double minimumAllowedFrequency = Double.MaxValue;
            double maximumAllowedFrequency = Double.MinValue;
            for (int index = 0; index < points.Count; index++)
            {
                VfPointSnapshot point = points[index];
                long minimumOffset = Math.Max(-1000000L, 1L - point.BaseFrequencyKHz);
                long maximumOffset = Math.Min(1000000L, 6000000L - point.BaseFrequencyKHz);
                minimumAllowedFrequency = Math.Min(minimumAllowedFrequency, Math.Max(1000L, point.BaseFrequencyKHz + minimumOffset));
                maximumAllowedFrequency = Math.Max(maximumAllowedFrequency, Math.Max(1000L, point.BaseFrequencyKHz + maximumOffset));
            }
            double axisMinimumFrequency = Math.Max(1000.0, Math.Floor(minimumAllowedFrequency / 100000.0) * 100000.0);
            double axisMaximumFrequency = Math.Ceiling(maximumAllowedFrequency / 100000.0) * 100000.0;
            if (axisMaximumFrequency <= axisMinimumFrequency) axisMaximumFrequency = axisMinimumFrequency + 100000.0;

            Func<VfPointSnapshot, double> pointX = delegate(VfPointSnapshot point)
            {
                return left + (point.VoltageUv - minVoltage) * plotWidth / Math.Max(1.0, maxVoltage - minVoltage);
            };
            Func<double, double> frequencyY = delegate(double frequencyKHz)
            {
                return top + (axisMaximumFrequency - frequencyKHz) * plotHeight / (axisMaximumFrequency - axisMinimumFrequency);
            };
            Func<VfPointSnapshot, int> previewOffset = delegate(VfPointSnapshot point)
            {
                int value;
                return staged.TryGetValue(point.Index, out value) ? value : point.FrequencyOffsetKHz;
            };
            Func<VfPointSnapshot, double> previewFrequency = delegate(VfPointSnapshot point)
            {
                return point.BaseFrequencyKHz + previewOffset(point);
            };

            for (int lineIndex = 0; lineIndex <= 6; lineIndex++)
            {
                double y = top + plotHeight * lineIndex / 6.0;
                canvas.Children.Add(new Line { X1 = left, X2 = width - right, Y1 = y, Y2 = y, Stroke = StrokeBrush, StrokeThickness = 1, IsHitTestVisible = false });
                double labelFrequency = axisMaximumFrequency - (axisMaximumFrequency - axisMinimumFrequency) * lineIndex / 6.0;
                AddCanvasLabel(canvas, Math.Round(labelFrequency / 1000.0).ToString(CultureInfo.InvariantCulture), 7, y - 7);
            }
            for (int lineIndex = 0; lineIndex <= 6; lineIndex++)
            {
                double x = left + plotWidth * lineIndex / 6.0;
                canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = height - bottom, Stroke = Brush("#172131"), StrokeThickness = 1, IsHitTestVisible = false });
            }

            Polyline referenceCurve = new Polyline
            {
                Stroke = Brush("#47566C"),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
                IsHitTestVisible = false
            };
            Polyline previewCurve = new Polyline { Stroke = staged.Count == 0 ? AccentBrush : WarningBrush, StrokeThickness = 2, IsHitTestVisible = false };
            canvas.Children.Add(referenceCurve);
            canvas.Children.Add(previewCurve);

            Line selectedGuide = new Line
            {
                Y1 = top,
                Y2 = height - bottom,
                Stroke = SecondaryAccentBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new double[] { 3, 3 }),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            canvas.Children.Add(selectedGuide);

            List<Ellipse> markers = new List<Ellipse>();
            for (int index = 0; index < points.Count; index++)
            {
                Ellipse marker = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Brush("#7A879A"),
                    Stroke = Brush("#0A0D14"),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                markers.Add(marker);
                canvas.Children.Add(marker);
            }

            TextBlock selectionInfo = new TextBlock
            {
                Foreground = Brushes.White,
                Background = Brush("#D9141925"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 6, 10, 6),
                IsHitTestVisible = false
            };
            Canvas.SetRight(selectionInfo, 28);
            Canvas.SetTop(selectionInfo, 31);
            canvas.Children.Add(selectionInfo);

            Rectangle selectionBox = new Rectangle
            {
                Fill = Brush("#287BF1C8"),
                Stroke = AccentBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            canvas.Children.Add(selectionBox);

            vfSelectedPointIndices.RemoveWhere(delegate(int pointIndex)
            {
                return FindVfPoint(points, pointIndex) == null;
            });
            if (!vfSelectedPointIndex.HasValue || FindVfPoint(points, vfSelectedPointIndex.Value) == null)
                vfSelectedPointIndex = points[0].Index;
            if (vfSelectedPointIndices.Count == 0)
                vfSelectedPointIndices.Add(vfSelectedPointIndex.Value);

            Func<List<int>> selectedIndices = delegate
            {
                List<int> result = new List<int>();
                for (int index = 0; index < points.Count; index++)
                    if (vfSelectedPointIndices.Contains(points[index].Index)) result.Add(points[index].Index);
                return result;
            };

            Action updateEditorsFromPrimary = delegate
            {
                if (!vfSelectedPointIndex.HasValue) return;
                VfPointSnapshot point = FindVfPoint(points, vfSelectedPointIndex.Value);
                if (point == null) return;
                vfPointDraft = point.Index.ToString(CultureInfo.InvariantCulture);
                vfTargetDraft = Math.Round(previewFrequency(point) / 1000.0).ToString(CultureInfo.InvariantCulture);
                if (vfPointIndexInput != null) vfPointIndexInput.Text = vfPointDraft;
                if (vfTargetFrequencyInput != null) vfTargetFrequencyInput.Text = vfTargetDraft;
            };

            Action redraw = null;
            redraw = delegate
            {
                referenceCurve.Points.Clear();
                previewCurve.Points.Clear();
                previewCurve.Stroke = staged.Count == 0 ? AccentBrush : WarningBrush;
                for (int index = 0; index < points.Count; index++)
                {
                    VfPointSnapshot point = points[index];
                    double x = pointX(point);
                    double referenceFrequency = point.BaseFrequencyKHz + point.FrequencyOffsetKHz;
                    double frequency = previewFrequency(point);
                    referenceCurve.Points.Add(new Point(x, frequencyY(referenceFrequency)));
                    Point position = new Point(x, frequencyY(frequency));
                    previewCurve.Points.Add(position);
                    bool primarySelected = vfSelectedPointIndex.HasValue && vfSelectedPointIndex.Value == point.Index;
                    bool selected = vfSelectedPointIndices.Contains(point.Index);
                    bool changed = staged.ContainsKey(point.Index);
                    Ellipse marker = markers[index];
                    marker.Width = primarySelected ? 11 : (selected ? 9 : 7);
                    marker.Height = primarySelected ? 11 : (selected ? 9 : 7);
                    marker.Fill = primarySelected ? SecondaryAccentBrush : (selected ? AccentBrush : (changed ? WarningBrush : Brush("#7A879A")));
                    marker.Stroke = selected ? Brushes.White : Brush("#0A0D14");
                    Canvas.SetLeft(marker, position.X - marker.Width / 2.0);
                    Canvas.SetTop(marker, position.Y - marker.Height / 2.0);
                }

                if (!vfSelectedPointIndex.HasValue)
                {
                    selectedGuide.Visibility = Visibility.Collapsed;
                    selectionInfo.Text = VoltelleLocalization.T("点击曲线上的点开始编辑");
                    return;
                }
                VfPointSnapshot selectedPoint = FindVfPoint(points, vfSelectedPointIndex.Value);
                if (selectedPoint == null)
                {
                    selectedGuide.Visibility = Visibility.Collapsed;
                    selectionInfo.Text = VoltelleLocalization.T("点击曲线上的点开始编辑");
                    return;
                }
                selectedGuide.X1 = selectedGuide.X2 = pointX(selectedPoint);
                selectedGuide.Visibility = Visibility.Visible;
                int selectedOffset = previewOffset(selectedPoint);
                int pointSpecificOffset = checked(selectedOffset - (int)globalCoreOffsetKHz);
                List<int> selection = selectedIndices();
                string selectionText;
                if (selection.Count <= 1)
                {
                    selectionText = "选中点 " + selectedPoint.Index +
                        " · 电压 " + (selectedPoint.VoltageUv / 1000U) + " mV" +
                        " · 频率 " + Math.Round(previewFrequency(selectedPoint) / 1000.0).ToString(CultureInfo.InvariantCulture) + " MHz" +
                        " · 全局 " + SignedMHz((int)globalCoreOffsetKHz) +
                        " · 点偏移 " + SignedMHz(pointSpecificOffset) +
                        " · 合计 " + SignedMHz(selectedOffset);
                }
                else
                {
                    VfPointSnapshot firstSelected = FindVfPoint(points, selection[0]);
                    VfPointSnapshot lastSelected = FindVfPoint(points, selection[selection.Count - 1]);
                    selectionText = "已选择 " + selection.Count + " 个点 · " +
                        (firstSelected.VoltageUv / 1000U) + ".." + (lastSelected.VoltageUv / 1000U) +
                        " mV · 主点 " + selectedPoint.Index + " · 批量拖动/方向键平移";
                }
                selectionInfo.Text = VoltelleLocalization.T(selectionText);
            };

            Action<int, bool> selectPoint = delegate(int pointIndex, bool replaceSelection)
            {
                VfPointSnapshot point = FindVfPoint(points, pointIndex);
                if (point == null) return;
                if (replaceSelection) vfSelectedPointIndices.Clear();
                vfSelectedPointIndices.Add(point.Index);
                vfSelectedPointIndex = point.Index;
                updateEditorsFromPrimary();
                redraw();
            };

            Action<IList<VfOffsetChange>> applyPlannedChanges = delegate(IList<VfOffsetChange> planned)
            {
                for (int plannedIndex = 0; plannedIndex < planned.Count; plannedIndex++)
                {
                    VfOffsetChange change = planned[plannedIndex];
                    VfPointSnapshot source = FindVfPoint(points, change.Index);
                    for (int stagedIndex = stagedVfChanges.Count - 1; stagedIndex >= 0; stagedIndex--)
                        if (stagedVfChanges[stagedIndex].Index == change.Index) stagedVfChanges.RemoveAt(stagedIndex);
                    staged.Remove(change.Index);
                    if (source != null && change.FrequencyOffsetKHz != source.FrequencyOffsetKHz)
                    {
                        stagedVfChanges.Add(new VfOffsetChange
                        {
                            Index = change.Index,
                            FrequencyOffsetKHz = change.FrequencyOffsetKHz
                        });
                        staged[change.Index] = change.FrequencyOffsetKHz;
                    }
                }
                stagedVfChanges.Sort(delegate(VfOffsetChange leftChange, VfOffsetChange rightChange)
                {
                    return leftChange.Index.CompareTo(rightChange.Index);
                });
                updateEditorsFromPrimary();
                UpdateVfStagingUi();
                redraw();
            };

            int lastTranslationDeltaMHz = 0;
            Func<IList<int>, IDictionary<int, int>, int, bool> stageTranslation = delegate(
                IList<int> selection,
                IDictionary<int, int> startingOffsets,
                int requestedDeltaMHz)
            {
                try
                {
                    lastTranslationDeltaMHz = VfCurveInteraction.ClampUniformDeltaMHz(
                        points, selection, startingOffsets, requestedDeltaMHz);
                    IList<VfOffsetChange> planned = VfCurvePlanner.PlanUniformTranslation(
                        points, selection, startingOffsets, lastTranslationDeltaMHz);
                    applyPlannedChanges(planned);
                    return true;
                }
                catch (Exception ex)
                {
                    SetUiError("V/F 批量平移无效：" + ex.Message);
                    return false;
                }
            };

            bool dragging = false;
            bool dragChanged = false;
            double dragStartY = 0;
            List<int> dragSelection = null;
            Dictionary<int, int> dragStartingOffsets = null;
            canvas.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                Point mouse = args.GetPosition(canvas);
                if (mouse.X < left - 12 || mouse.X > width - right + 12 || mouse.Y < top - 18 || mouse.Y > height - bottom + 18) return;
                int nearestIndex = -1;
                double nearestDistance = Double.MaxValue;
                for (int index = 0; index < previewCurve.Points.Count; index++)
                {
                    Point candidate = previewCurve.Points[index];
                    double dx = candidate.X - mouse.X;
                    double dy = candidate.Y - mouse.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearestIndex = index;
                }
                if (nearestIndex < 0 || nearestDistance > 24.0) return;
                int nearestPointIndex = points[nearestIndex].Index;
                selectPoint(nearestPointIndex, !vfSelectedPointIndices.Contains(nearestPointIndex));
                dragSelection = selectedIndices();
                dragStartingOffsets = new Dictionary<int, int>();
                for (int index = 0; index < dragSelection.Count; index++)
                {
                    VfPointSnapshot selectedPoint = FindVfPoint(points, dragSelection[index]);
                    dragStartingOffsets[dragSelection[index]] = previewOffset(selectedPoint);
                }
                dragStartY = mouse.Y;
                canvas.Focus();
                canvas.CaptureMouse();
                dragging = true;
                dragChanged = false;
                args.Handled = true;
            };
            canvas.MouseMove += delegate(object sender, MouseEventArgs args)
            {
                if (!dragging || dragSelection == null || dragStartingOffsets == null || args.LeftButton != MouseButtonState.Pressed) return;
                Point mouse = args.GetPosition(canvas);
                double boundedY = Math.Max(top, Math.Min(height - bottom, mouse.Y));
                int requestedDeltaMHz = (int)Math.Round(
                    (dragStartY - boundedY) * (axisMaximumFrequency - axisMinimumFrequency) /
                    plotHeight / 1000.0);
                if (stageTranslation(dragSelection, dragStartingOffsets, requestedDeltaMHz))
                    dragChanged = dragChanged || lastTranslationDeltaMHz != 0;
                args.Handled = true;
            };
            canvas.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (!dragging) return;
                dragging = false;
                canvas.ReleaseMouseCapture();
                if (dragChanged && dragSelection != null)
                {
                    statusText.Text = "已通过曲线拖动批量暂存 " + dragSelection.Count + " 个 V/F 点 · " +
                        (lastTranslationDeltaMHz >= 0 ? "+" : string.Empty) + lastTranslationDeltaMHz + " MHz；尚未执行硬件写入。";
                    statusText.Foreground = AccentBrush;
                    ApplyLocalizationToTree(Content as DependencyObject, true);
                }
                dragSelection = null;
                dragStartingOffsets = null;
                args.Handled = true;
            };

            bool boxSelecting = false;
            bool boxAdditive = false;
            Point boxStart = new Point();
            canvas.MouseRightButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                Point mouse = args.GetPosition(canvas);
                if (mouse.X < left || mouse.X > width - right || mouse.Y < top || mouse.Y > height - bottom) return;
                boxSelecting = true;
                boxAdditive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                boxStart = mouse;
                selectionBox.Visibility = Visibility.Visible;
                Canvas.SetLeft(selectionBox, mouse.X);
                Canvas.SetTop(selectionBox, mouse.Y);
                selectionBox.Width = 0;
                selectionBox.Height = 0;
                canvas.Focus();
                canvas.CaptureMouse();
                args.Handled = true;
            };
            canvas.MouseMove += delegate(object sender, MouseEventArgs args)
            {
                if (!boxSelecting || args.RightButton != MouseButtonState.Pressed) return;
                Point mouse = args.GetPosition(canvas);
                double boundedX = Math.Max(left, Math.Min(width - right, mouse.X));
                double boundedY = Math.Max(top, Math.Min(height - bottom, mouse.Y));
                double x = Math.Min(boxStart.X, boundedX);
                double y = Math.Min(boxStart.Y, boundedY);
                Canvas.SetLeft(selectionBox, x);
                Canvas.SetTop(selectionBox, y);
                selectionBox.Width = Math.Abs(boundedX - boxStart.X);
                selectionBox.Height = Math.Abs(boundedY - boxStart.Y);
                args.Handled = true;
            };
            canvas.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (!boxSelecting) return;
                boxSelecting = false;
                canvas.ReleaseMouseCapture();
                Point mouse = args.GetPosition(canvas);
                double boundedX = Math.Max(left, Math.Min(width - right, mouse.X));
                double boundedY = Math.Max(top, Math.Min(height - bottom, mouse.Y));
                double minX = Math.Min(boxStart.X, boundedX);
                double maxX = Math.Max(boxStart.X, boundedX);
                double minY = Math.Min(boxStart.Y, boundedY);
                double maxY = Math.Max(boxStart.Y, boundedY);
                List<int> hits = new List<int>();
                for (int index = 0; index < previewCurve.Points.Count; index++)
                {
                    Point candidate = previewCurve.Points[index];
                    if (candidate.X >= minX && candidate.X <= maxX && candidate.Y >= minY && candidate.Y <= maxY)
                        hits.Add(points[index].Index);
                }
                if (!boxAdditive) vfSelectedPointIndices.Clear();
                for (int index = 0; index < hits.Count; index++) vfSelectedPointIndices.Add(hits[index]);
                if (hits.Count != 0)
                    vfSelectedPointIndex = hits[0];
                else if (vfSelectedPointIndices.Count == 0)
                    vfSelectedPointIndex = null;
                else if (!vfSelectedPointIndex.HasValue || !vfSelectedPointIndices.Contains(vfSelectedPointIndex.Value))
                {
                    List<int> remaining = selectedIndices();
                    vfSelectedPointIndex = remaining.Count == 0 ? (int?)null : remaining[0];
                }
                selectionBox.Visibility = Visibility.Collapsed;
                updateEditorsFromPrimary();
                redraw();
                statusText.Text = "已框选 " + vfSelectedPointIndices.Count + " 个 V/F 点；仅改变选择，尚未执行硬件写入。";
                statusText.Foreground = AccentBrush;
                ApplyLocalizationToTree(Content as DependencyObject, true);
                args.Handled = true;
            };

            canvas.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                int selectedListIndex = 0;
                if (vfSelectedPointIndex.HasValue)
                {
                    for (int index = 0; index < points.Count; index++)
                        if (points[index].Index == vfSelectedPointIndex.Value) selectedListIndex = index;
                }
                if (args.Key == Key.Left || args.Key == Key.Right)
                {
                    selectedListIndex += args.Key == Key.Left ? -1 : 1;
                    selectedListIndex = Math.Max(0, Math.Min(points.Count - 1, selectedListIndex));
                    selectPoint(points[selectedListIndex].Index, true);
                    args.Handled = true;
                    return;
                }
                if (args.Key != Key.Up && args.Key != Key.Down) return;
                List<int> selection = selectedIndices();
                if (selection.Count == 0)
                {
                    selectPoint(points[selectedListIndex].Index, true);
                    selection = selectedIndices();
                }
                int delta = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 15 : 1;
                if (args.Key == Key.Down) delta = -delta;
                Dictionary<int, int> startingOffsets = new Dictionary<int, int>();
                for (int index = 0; index < selection.Count; index++)
                {
                    VfPointSnapshot selectedPoint = FindVfPoint(points, selection[index]);
                    startingOffsets[selection[index]] = previewOffset(selectedPoint);
                }
                if (stageTranslation(selection, startingOffsets, delta))
                {
                    statusText.Text = "已通过键盘批量暂存 " + selection.Count + " 个 V/F 点 · " +
                        (lastTranslationDeltaMHz >= 0 ? "+" : string.Empty) + lastTranslationDeltaMHz + " MHz；尚未执行硬件写入。";
                    statusText.Foreground = AccentBrush;
                    ApplyLocalizationToTree(Content as DependencyObject, true);
                }
                args.Handled = true;
            };

            AddCanvasLabel(canvas, (minVoltage / 1000U) + " mV", left, height - 27);
            AddCanvasLabel(canvas, (maxVoltage / 1000U) + " mV", width - 84, height - 27);
            AddCanvasLabel(canvas, "MHz", 9, 6);
            updateEditorsFromPrimary();
            redraw();
            return canvas;
        }

        private static VfPointSnapshot FindVfPoint(IList<VfPointSnapshot> points, int pointIndex)
        {
            if (points == null) return null;
            for (int index = 0; index < points.Count; index++)
                if (points[index].Index == pointIndex) return points[index];
            return null;
        }

        private static string SignedMHz(int offsetKHz)
        {
            double offsetMHz = offsetKHz / 1000.0;
            return (offsetKHz >= 0 ? "+" : String.Empty) +
                offsetMHz.ToString("0.###", CultureInfo.InvariantCulture) + " MHz";
        }

        private void UpdateVfStagingUi()
        {
            if (vfStagedCountText != null)
            {
                vfStagedCountText.Text = stagedVfChanges.Count + " 个点待应用";
                vfStagedCountText.Foreground = stagedVfChanges.Count == 0 ? MutedBrush : WarningBrush;
                ApplyLocalizationToTree(vfStagedCountText, true);
            }
            if (applyStagedVfButton != null)
                applyStagedVfButton.IsEnabled = CanInitiateWrite && stagedVfChanges.Count != 0;
            if (vfChartSummaryText != null && snapshot != null)
            {
                vfChartSummaryText.Text = snapshot.VfPoints.Count == 0
                    ? "曲线不可用"
                    : snapshot.VfPoints.Count + " 个有效点 · " + stagedVfChanges.Count + " 个暂存变更 · " + VfSummary(snapshot);
                ApplyLocalizationToTree(vfChartSummaryText, true);
            }
        }

        private static void AddCanvasLabel(Canvas canvas, string text, double x, double y)
        {
            TextBlock label = new TextBlock { Text = text, Foreground = MutedBrush, FontSize = 9 };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
        }

        private UIElement BuildPowerPage()
        {
            StackPanel page = PageStack();
            if (snapshot == null)
            {
                page.Children.Add(EmptyState("等待 Power Monitor 采样…"));
                return page;
            }

            UniformGrid summary = new UniformGrid { Columns = 4 };
            summary.Children.Add(Metric("Power Monitor 整板", PowerMonitorBoard(snapshot), "Status +0x08"));
            summary.Children.Add(Metric("驱动芯片", TopologyPower(snapshot, true), "Topology ID 0"));
            summary.Children.Add(Metric("驱动整板", TopologyPower(snapshot, false), "Topology ID 1"));
            summary.Children.Add(Metric("会话能量", SessionEnergy(snapshot), "主通道累计差"));
            page.Children.Add(summary);

            string reasons = "无降频原因";
            bool danger = false;
            if (snapshot.PowerTelemetry != null && snapshot.PowerTelemetry.PerfDecreaseReasons.Count != 0)
            {
                reasons = string.Join(" · ", new List<string>(snapshot.PowerTelemetry.PerfDecreaseReasons).ToArray());
                danger = snapshot.PowerTelemetry.InsufficientExternalPower == true;
            }
            page.Children.Add(Alert("Perf Decrease", reasons, danger));
            page.Children.Add(SectionHeading("Power Monitor 通道", "通道可能重叠或包含汇总项，不能把各行直接相加"));

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, CardHoverBrush));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, StrokeBrush));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));

            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 0, 9, 0)));
            Trigger selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, AccentDarkBrush));
            selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selectedCell);

            DataGrid table = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = StrokeBrush,
                VerticalGridLinesBrush = Brushes.Transparent,
                Background = CardBrush,
                Foreground = Brushes.White,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                RowBackground = CardBrush,
                AlternatingRowBackground = PanelBrush,
                ColumnHeaderStyle = headerStyle,
                CellStyle = cellStyle,
                ColumnHeaderHeight = 36,
                RowHeight = 34,
                MaxHeight = 470
            };
            table.Columns.Add(TextColumn("通道", "ChannelIndex", 58, null));
            table.Columns.Add(TextColumn("Rail", "RailName", 1, null));
            table.Columns.Add(TextColumn("功率 W", "PowerWatts", 92, "F3"));
            table.Columns.Add(TextColumn("电流 A", "CurrentAmps", 92, "F3"));
            table.Columns.Add(TextColumn("电压 V", "VoltageVolts", 92, "F4"));
            table.Columns.Add(TextColumn("会话 Wh", "SessionEnergyWh", 100, "F6"));
            table.Columns.Add(TextColumn("Driver field0", "InfoField0", 105, null));
            table.Columns.Add(TextColumn("Rail ID", "RailId", 72, null));
            if (snapshot.PowerTelemetry != null && snapshot.PowerTelemetry.Monitor != null)
                table.ItemsSource = snapshot.PowerTelemetry.Monitor.Channels;
            page.Children.Add(table);
            return page;
        }

        private static DataGridTextColumn TextColumn(string header, string path, double width, string format)
        {
            Binding binding = new Binding(path);
            if (!string.IsNullOrEmpty(format)) binding.StringFormat = format;
            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = width == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width)
            };
        }

        private UIElement BuildProfilesPage()
        {
            StackPanel page = PageStack();
            if (!string.IsNullOrEmpty(profileError))
            {
                page.Children.Add(Alert("配置档文件不可用", profileError, true));
                return page;
            }
            if (snapshot == null || profileStore == null || profileDocument == null)
            {
                page.Children.Add(EmptyState("等待 GPU 与 VBIOS 信息以初始化配置档…"));
                return page;
            }

            page.Children.Add(Alert(
                "VBIOS 范围配置档",
                "配置文件使用稳定的 v1 schema，并绑定当前 GPU 名称和 VBIOS。加载待应用不会执行硬件写入。",
                false));

            Grid layout = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border listCard = CardShell();
            listCard.Margin = new Thickness(0, 0, 12, 0);
            StackPanel listStack = new StackPanel();
            listStack.Children.Add(new TextBlock { Text = "已保存配置", FontSize = 14, FontWeight = FontWeights.SemiBold });
            listStack.Children.Add(new TextBlock
            {
                Text = profileDocument.Profiles.Count + " 个 · revision " + profileDocument.Revision,
                Foreground = MutedBrush,
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 10)
            });
            listStack.Children.Add(new TextBlock
            {
                Text = profileDocument.StartupEnabled
                    ? "启动行为：计划任务延迟自动应用指定配置档"
                    : "启动行为：仅 GET 当前驱动状态",
                Foreground = AccentBrush,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8)
            });
            ListBox profiles = new ListBox
            {
                ItemsSource = profileDocument.Profiles,
                DisplayMemberPath = "Name",
                Background = PanelBrush,
                Foreground = Brushes.White,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                MinHeight = 240,
                MaxHeight = 360
            };
            Style itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            Trigger selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, AccentDarkBrush));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, AccentBrush));
            itemStyle.Triggers.Add(selected);
            profiles.ItemContainerStyle = itemStyle;
            MVoltProfile selectedProfile = FindProfile(selectedProfileId);
            if (selectedProfile != null) profiles.SelectedItem = selectedProfile;
            profiles.SelectionChanged += delegate
            {
                MVoltProfile chosen = profiles.SelectedItem as MVoltProfile;
                selectedProfileId = chosen == null ? null : chosen.Id;
                profileNameDraft = chosen == null ? string.Empty : chosen.Name;
                pendingProfile = null;
                profileDirty = false;
                RenderActivePage();
            };
            listStack.Children.Add(profiles);
            listCard.Child = listStack;
            layout.Children.Add(listCard);

            Border editorCard = CardShell();
            editorCard.Margin = new Thickness(0);
            StackPanel editor = new StackPanel();
            editor.Children.Add(new TextBlock { Text = "配置档名称", Foreground = MutedBrush, FontSize = 11 });
            profileNameInput = new TextBox
            {
                Text = profileNameDraft,
                Background = Brush("#0C1420"),
                Foreground = Brushes.White,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 7, 0, 12),
                MaxLength = 64
            };
            profileNameInput.TextChanged += delegate
            {
                profileNameDraft = profileNameInput.Text;
                profileDirty = true;
            };
            editor.Children.Add(profileNameInput);

            WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            Button create = SecondaryButton("新建");
            create.Margin = new Thickness(0, 0, 8, 8);
            create.Click += delegate
            {
                selectedProfileId = null;
                profileNameDraft = string.Empty;
                pendingProfile = null;
                profileDirty = true;
                RenderActivePage();
            };
            actions.Children.Add(create);

            Button save = PrimaryButton("保存当前");
            save.Margin = new Thickness(0, 0, 8, 8);
            save.Click += delegate { SaveCurrentProfile(); };
            actions.Children.Add(save);

            Button loadPending = SecondaryButton("载入待应用");
            loadPending.Margin = new Thickness(0, 0, 8, 8);
            loadPending.IsEnabled = selectedProfile != null;
            loadPending.Click += delegate { LoadSelectedProfilePending(); };
            actions.Children.Add(loadPending);

            Button apply = PrimaryButton("载入并验证");
            apply.Margin = new Thickness(0, 0, 8, 8);
            apply.IsEnabled = selectedProfile != null && CanInitiateWrite;
            apply.Click += delegate { ApplySelectedProfile(); };
            actions.Children.Add(apply);

            Button delete = SecondaryButton("删除");
            delete.Margin = new Thickness(0, 0, 8, 8);
            delete.IsEnabled = selectedProfile != null;
            delete.Click += delegate { DeleteSelectedProfile(); };
            actions.Children.Add(delete);

            editor.Children.Add(actions);

            CheckBox trayToggle = new CheckBox
            {
                Content = "最小化或关闭主窗口时发送到系统托盘",
                IsChecked = minimizeToTray,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            };
            trayToggle.Click += delegate
            {
                minimizeToTray = trayToggle.IsChecked == true;
                statusText.Text = minimizeToTray
                    ? "已启用后台托盘；双击托盘图标可恢复，右键可刷新或退出。"
                    : "已关闭后台托盘；关闭窗口将直接退出。";
                statusText.Foreground = AccentBrush;
            };
            editor.Children.Add(trayToggle);

            Border startupCard = CardShell();
            startupCard.Margin = new Thickness(0, 0, 0, 12);
            StackPanel startupStack = new StackPanel();
            startupStack.Children.Add(new TextBlock
            {
                Text = "延迟开机自启",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            CheckBox startupToggle = new CheckBox
            {
                Content = "登录后自动应用当前所选配置档并收进托盘",
                IsChecked = profileDocument.StartupEnabled && selectedProfile != null && profileDocument.StartupProfileId == selectedProfile.Id,
                IsEnabled = selectedProfile != null && CanInitiateWrite,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 10, 0, 4)
            };
            startupStack.Children.Add(startupToggle);
            string startupDelayDraft = profileDocument.StartupDelaySeconds.ToString(CultureInfo.InvariantCulture);
            UIElement delayField = BuildVoltageField(
                "登录后延迟",
                "秒",
                startupDelayDraft,
                delegate(string value) { startupDelayDraft = value; },
                10,
                600,
                5);
            startupStack.Children.Add(delayField);
            Button saveStartup = PrimaryButton("保存自启设置");
            saveStartup.HorizontalAlignment = HorizontalAlignment.Right;
            saveStartup.Margin = new Thickness(0, 10, 0, 0);
            saveStartup.IsEnabled = CanInitiateWrite;
            saveStartup.Click += delegate { ConfigureStartup(selectedProfile, startupToggle.IsChecked == true, startupDelayDraft); };
            startupStack.Children.Add(saveStartup);
            startupCard.Child = startupStack;
            editor.Children.Add(startupCard);

            MVoltProfile preview = pendingProfile ?? selectedProfile;
            editor.Children.Add(BuildProfilePreview(preview));
            editorCard.Child = editor;
            Grid.SetColumn(editorCard, 1);
            layout.Children.Add(editorCard);
            page.Children.Add(layout);

            page.Children.Add(SectionHeading("配置文件", "原子替换并保留上一版 .bak；普通启动只读取，只有计划任务实例自动应用"));
            Border pathCard = CardShell();
            pathCard.Child = new TextBlock
            {
                Text = profileStore.FilePath,
                Foreground = Brush("#B8C7DA"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            };
            page.Children.Add(pathCard);
            page.Children.Add(Alert(
                "普通启动只读取",
                "手动启动始终先执行 NVAPI GET 且不会自动写入；仅当用户启用上方计划任务时，登录后的专用实例会在设定延迟后应用指定配置档。",
                false));
            return page;
        }

        private UIElement BuildProfilePreview(MVoltProfile profile)
        {
            StackPanel preview = new StackPanel();
            preview.Children.Add(new TextBlock
            {
                Text = profile == null ? "尚未选择配置档" : (pendingProfile == profile ? "待应用预览" : "所选配置预览"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 8)
            });
            if (profile == null)
            {
                preview.Children.Add(new TextBlock { Text = "保存当前读数，或从左侧选择一个配置档。", Foreground = MutedBrush, FontSize = 10 });
                return preview;
            }

            UniformGrid values = new UniformGrid { Columns = 3 };
            values.Children.Add(Metric("核心", ProfileOffset(profile.Controls.Core), "Pstates20"));
            values.Children.Add(Metric("显存", ProfileOffset(profile.Controls.Memory), profile.ConfirmedHighMemory ? "高偏移已确认" : "Pstates20"));
            values.Children.Add(Metric("功耗", ProfilePercent(profile.Controls.Power), "Power Policies"));
            values.Children.Add(Metric("Crossbar", ProfileOffset(profile.Controls.Xbar), "ClockDomains"));
            values.Children.Add(Metric("SYS Clock", ProfileOffset(profile.Controls.SysClock), "ClockDomains domain 2"));
            values.Children.Add(Metric("Video Clock", ProfileOffset(profile.Controls.VideoClock), "ClockDomains domain 21"));
            int enabledFanCount = 0;
            for (int fanIndex = 0; fanIndex < profile.Controls.Fans.Count; fanIndex++)
                if (profile.Controls.Fans[fanIndex].Enabled) enabledFanCount++;
            values.Children.Add(Metric("风扇", enabledFanCount == 0 ? "关闭" : enabledFanCount + " 路", "ClientFanCoolers · 仅保存手动通道"));
            values.Children.Add(Metric("Voltage Boost", ProfilePercent(profile.Controls.VoltageBoost), "VoltRails"));
            values.Children.Add(Metric("V/F", profile.VfCurveOffsetsKHz.Count == 0 ? "关闭" : profile.VfCurveOffsetsKHz.Count + " 点", profile.VfCurveOffsetMode));
            preview.Children.Add(values);

            TextBlock rails = new TextBlock
            {
                Text = "NVVDD " + ProfileRange(profile.Controls.Nvvdd) + "  ·  MSVDD " + ProfileRange(profile.Controls.Msvdd),
                Foreground = Brush("#B8C7DA"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            preview.Children.Add(rails);
            return preview;
        }

        private void SaveCurrentProfile()
        {
            if (profileStore == null || profileDocument == null || snapshot == null) return;
            try
            {
                bool confirmedHighMemory = false;
                if (snapshot.Tuning.MemoryOffsetMHz.HasValue && snapshot.Tuning.MemoryOffsetMHz.Value > 4000)
                {
                    if (MessageBox.Show(
                            "当前显存偏移高于 +4000 MHz。仅在已经验证稳定性时保存此配置档。是否明确确认？",
                            "确认高显存偏移",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No) != MessageBoxResult.Yes)
                        return;
                    confirmedHighMemory = true;
                }
                MVoltProfile captured = ProfileFactory.Capture(
                    snapshot,
                    profileNameInput == null ? profileNameDraft : profileNameInput.Text,
                    confirmedHighMemory,
                    xocEnabled);
                ProfileDocument next = CloneProfileDocument(profileDocument);
                MVoltProfile existing = FindProfile(next, selectedProfileId);
                if (existing != null)
                {
                    captured.Id = existing.Id;
                    int index = next.Profiles.IndexOf(existing);
                    next.Profiles[index] = captured;
                }
                else
                {
                    next.Profiles.Add(captured);
                }
                profileStore.Save(next);
                profileDocument = next;
                selectedProfileId = captured.Id;
                profileNameDraft = captured.Name;
                pendingProfile = captured;
                profileDirty = false;
                statusText.Text = "配置档已保存：" + captured.Name;
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                statusText.Text = "保存配置档失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
        }

        private void ConfigureStartup(MVoltProfile selectedProfile, bool enabled, string delayText)
        {
            if (profileStore == null || profileDocument == null) return;
            int delaySeconds;
            if (!TryParseInteger(delayText, out delaySeconds) || delaySeconds < 10 || delaySeconds > 600)
            {
                SetUiError("开机自启延迟必须是 10..600 秒的整数。");
                return;
            }
            if (enabled && selectedProfile == null)
            {
                SetUiError("启用开机自动应用前必须选择一个配置档。");
                return;
            }
            ProfileDocument previous = CloneProfileDocument(profileDocument);
            try
            {
                ProfileDocument next = CloneProfileDocument(profileDocument);
                next.StartupEnabled = enabled;
                next.StartupDelaySeconds = delaySeconds;
                next.StartupProfileId = enabled ? selectedProfile.Id : string.Empty;
                next.MinimizeToTrayAtLogon = enabled;
                StartupTaskManager.Configure(enabled, delaySeconds);
                try
                {
                    profileStore.Save(next);
                }
                catch
                {
                    StartupTaskManager.Configure(previous.StartupEnabled, previous.StartupDelaySeconds == 0 ? 60 : previous.StartupDelaySeconds);
                    throw;
                }
                profileDocument = next;
                statusText.Text = enabled
                    ? "已启用开机自动应用：“" + selectedProfile.Name + "”，延迟 " + delaySeconds + " 秒。"
                    : "已关闭开机自动应用并删除计划任务。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                statusText.Text = "保存开机自启设置失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
        }

        private void LoadSelectedProfilePending()
        {
            MVoltProfile profile = FindProfile(selectedProfileId);
            if (profile == null) return;
            pendingProfile = profile;
            stagedVfChanges.Clear();
            voltageDraftInitialized = false;
            profileNameDraft = profile.Name;
            profileDirty = false;
            statusText.Text = "已载入待应用目标；未执行硬件写入：" + profile.Name;
            statusText.Foreground = AccentBrush;
            RenderActivePage();
        }

        private void ApplySelectedProfile()
        {
            MVoltProfile profile = FindProfile(selectedProfileId);
            if (profile == null || nvBackend == null) return;
            ExecuteConfirmedBestEffortWrite(
                "配置档 “" + profile.Name + "” · 已启用的全部调校项目",
                delegate { return nvBackend.ApplyProfileVerified(profile); },
                true);
        }

        private void DeleteSelectedProfile()
        {
            MVoltProfile profile = FindProfile(selectedProfileId);
            if (profile == null || profileStore == null || profileDocument == null) return;
            if (MessageBox.Show(
                    "删除配置档“" + profile.Name + "”？",
                    "删除配置档",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                ProfileDocument next = CloneProfileDocument(profileDocument);
                MVoltProfile removing = FindProfile(next, profile.Id);
                if (removing == null) throw new InvalidOperationException("待删除配置档已不存在。");
                next.Profiles.Remove(removing);
                if (next.StartupProfileId == profile.Id)
                {
                    StartupTaskManager.Configure(false, next.StartupDelaySeconds == 0 ? 60 : next.StartupDelaySeconds);
                    next.StartupProfileId = string.Empty;
                    next.StartupEnabled = false;
                }
                profileStore.Save(next);
                profileDocument = next;
                pendingProfile = pendingProfile == profile ? null : pendingProfile;
                selectedProfileId = profileDocument.Profiles.Count == 0 ? null : profileDocument.Profiles[0].Id;
                profileNameDraft = profileDocument.Profiles.Count == 0 ? string.Empty : profileDocument.Profiles[0].Name;
                profileDirty = false;
                statusText.Text = "配置档已删除。";
                statusText.Foreground = AccentBrush;
                RenderActivePage();
            }
            catch (Exception ex)
            {
                statusText.Text = "删除配置档失败：" + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
        }

        private MVoltProfile FindProfile(string id)
        {
            return FindProfile(profileDocument, id);
        }

        private static MVoltProfile FindProfile(ProfileDocument document, string id)
        {
            if (document == null || string.IsNullOrEmpty(id)) return null;
            for (int index = 0; index < document.Profiles.Count; index++)
                if (document.Profiles[index].Id == id) return document.Profiles[index];
            return null;
        }

        private static ProfileDocument CloneProfileDocument(ProfileDocument document)
        {
            return ProfileStore.DeserializeFromUtf8(ProfileStore.SerializeToUtf8(document));
        }

        private static string ProfileRange(ProfileRangeControl control)
        {
            return control != null && control.Enabled ? control.MinimumMv + ".." + control.MaximumMv + " mV" : "关闭";
        }

        private static string ProfileOffset(ProfileOffsetControl control)
        {
            return control != null && control.Enabled ? control.OffsetMHz + " MHz" : "关闭";
        }

        private static string ProfilePercent(ProfilePercentControl control)
        {
            return control != null && control.Enabled ? control.Percent + "%" : "关闭";
        }

        private UIElement BuildInterfacesPage()
        {
            StackPanel page = PageStack();
            page.Children.Add(Alert(
                "接口入口集合已实现",
                "当前实现使用 42 个唯一 QueryInterface ID；其中 0x527FC458 用于 ClockDomains MeasureFrequency。",
                false));

            Border capabilityCard = CardShell();
            StackPanel list = new StackPanel();
            list.Children.Add(new TextBlock { Text = "当前驱动可用性", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            if (snapshot == null || snapshot.PrivateCapabilities.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "尚未完成驱动探测。", Foreground = MutedBrush });
            }
            else
            {
                foreach (KeyValuePair<string, bool> item in snapshot.PrivateCapabilities)
                    list.Children.Add(Capability(item.Key, item.Value));
            }
            capabilityCard.Child = list;
            page.Children.Add(capabilityCard);

            page.Children.Add(SectionHeading("实现与验证状态", "接口、结构布局、分项写入和真实驱动写入分别核对"));
            UniformGrid stages = new UniformGrid { Columns = 3 };
            stages.Children.Add(ProgressCard("42 / 42", "入口 ID", "10 个直接 ID + 32 个私有 ID"));
            stages.Children.Add(ProgressCard("30 / 30", "安全与布局测试", "缓冲区、草稿保留、失败继续、报告、配置档与异常路径"));
            stages.Children.Add(ProgressCard("12 / 12", "实机写入项目", "核心功能与新增 SYS、Video、Fan 已完成最小写入和 GET 回读"));
            page.Children.Add(stages);

            page.Children.Add(SectionHeading("正式版安全边界", "写入能力不代表任意参数都安全"));
            Border remaining = CardShell();
            StackPanel remainingStack = new StackPanel();
            remainingStack.Children.Add(Bullet("常规应用前显示目标和风险确认；一键复位直接执行，不显示确认弹窗。"));
            remainingStack.Children.Add(Bullet("每个项目独立写入并回读；失败项不会撤销已成功项目，后续项目继续执行。"));
            remainingStack.Children.Add(Bullet("普通托盘实例只维持实时遥测；仅用户启用的登录计划任务会延迟自动应用指定配置档。"));
            remainingStack.Children.Add(Bullet("更大目标、边界值和负向核心仍应由用户逐步验证，不承诺稳定性。"));
            remaining.Child = remainingStack;
            page.Children.Add(remaining);
            return page;
        }

        private static UIElement ProgressCard(string value, string label, string hint)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = value, Foreground = AccentBrush, FontSize = 26, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 4, 0, 4) });
            stack.Children.Add(new TextBlock { Text = hint, Foreground = MutedBrush, FontSize = 9, TextWrapping = TextWrapping.Wrap });
            card.Child = stack;
            return card;
        }

        private static UIElement Bullet(string text)
        {
            Grid row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock dot = new TextBlock { Text = "•", Foreground = AccentBrush, FontSize = 15 };
            TextBlock body = new TextBlock { Text = text, Foreground = Brush("#C8D3E3"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            row.Children.Add(dot);
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            return row;
        }

        private static StackPanel PageStack()
        {
            return new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        }

        private static Border Metric(string label, string value, string hint)
        {
            Border card = CardShell();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 9, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock { Text = hint, Foreground = Brush("#6D7A8E"), FontSize = 8, TextWrapping = TextWrapping.Wrap });
            card.Child = stack;
            return card;
        }

        private static Border CardShell()
        {
            return new Border
            {
                Background = CardBrush,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(17, 15, 17, 15)
            };
        }

        private static UIElement WrapAvailability(UIElement content, bool available, string reason)
        {
            if (available || content == null) return content;
            content.IsEnabled = false;
            content.Opacity = 0.38;
            Grid layer = new Grid();
            layer.Children.Add(content);
            Border cover = UnavailableCover(reason);
            Panel.SetZIndex(cover, 10);
            layer.Children.Add(cover);
            return layer;
        }

        private static void ApplyUnavailableOverlay(Border card, bool available, string reason)
        {
            if (card == null || available) return;
            UIElement original = card.Child;
            if (original != null)
            {
                original.IsEnabled = false;
                original.Opacity = 0.38;
            }
            Grid layer = new Grid();
            if (original != null) layer.Children.Add(original);
            Border cover = UnavailableCover(reason);
            Panel.SetZIndex(cover, 10);
            layer.Children.Add(cover);
            card.Child = layer;
        }

        private static Border UnavailableCover(string reason)
        {
            return new Border
            {
                Background = Brush("#D92A303B"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new TextBlock
                {
                    Text = "此选项不可用\n" + reason,
                    Foreground = Brush("#C8D0DA"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private static UIElement SectionHeading(string title, string subtitle)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(0, 17, 0, 11) };
            stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = subtitle, Foreground = MutedBrush, FontSize = 10, Margin = new Thickness(0, 4, 0, 0) });
            return stack;
        }

        private static Border Alert(string title, string body, bool danger)
        {
            Border alert = new Border
            {
                Background = danger ? Brush("#2A171B") : AccentDarkBrush,
                BorderBrush = danger ? Brush("#6B3039") : Brush("#285747"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 10, 10)
            };
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, Foreground = danger ? ErrorBrush : AccentBrush, FontWeight = FontWeights.SemiBold, FontSize = 12 });
            stack.Children.Add(new TextBlock { Text = body, Foreground = danger ? Brush("#E7B5BA") : Brush("#B9D9CC"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            alert.Child = stack;
            return alert;
        }

        private static Border EmptyState(string text)
        {
            Border state = CardShell();
            state.Padding = new Thickness(20, 30, 20, 30);
            state.Child = new TextBlock { Text = text, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center };
            return state;
        }

        private static UIElement Capability(string label, bool available)
        {
            Grid row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Foreground = Brush("#C7D2E3"), FontSize = 11 });
            Border badge = new Border
            {
                Background = available ? AccentDarkBrush : Brush("#321A20"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3)
            };
            badge.Child = new TextBlock { Text = available ? "可用" : "未提供", Foreground = available ? AccentBrush : ErrorBrush, FontSize = 9 };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            return row;
        }

        private static Button PrimaryButton(string text)
        {
            return new Button
            {
                Content = text,
                Style = ThemedButtonStyle(true)
            };
        }

        private static Button SecondaryButton(string text)
        {
            return new Button
            {
                Content = text,
                Style = ThemedButtonStyle(false)
            };
        }

        private static Style ThemedButtonStyle(bool primary)
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? AccentBrush : CardHoverBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Brush("#07130F") : Brushes.White));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? AccentBrush : StrokeBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(17, 9, 17, 9)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5)));
            style.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(1, 1)));

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
            presenter.SetBinding(ContentPresenter.ContentStringFormatProperty, new Binding("ContentStringFormat") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, primary ? Brush("#82EABD") : Brush("#223247")));
            style.Triggers.Add(hover);

            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(0.98, 0.98)));
            style.Triggers.Add(pressed);

            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#202936")));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#69778A")));
            disabled.Setters.Add(new Setter(Control.BorderBrushProperty, StrokeBrush));
            disabled.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Arrow));
            style.Triggers.Add(disabled);
            return style;
        }

        private static string Format(double? value, string unit)
        {
            return value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) + " " + unit : "—";
        }

        private static string HexValue(uint? value)
        {
            return value.HasValue ? "0x" + value.Value.ToString("X8", CultureInfo.InvariantCulture) : "—";
        }

        private static string OptionalUInt(uint? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";
        }

        private static string RailValue(GpuSnapshot sample, int railIndex)
        {
            VoltageRailContract rail = sample.Voltage == null ? null : sample.Voltage.FindRail(railIndex);
            return rail == null ? "—" : (rail.SensedUv / 1000.0).ToString("N1", CultureInfo.InvariantCulture) + " mV";
        }

        private static string RailSummary(GpuSnapshot sample, int railIndex)
        {
            VoltageRailContract rail = sample.Voltage == null ? null : sample.Voltage.FindRail(railIndex);
            if (rail == null) return "VoltRails v2";
            return "REL " + (rail.ReliabilityLimitUv / 1000U) + " · MAX " + (rail.MaximumLimitUv / 1000U) + " · MIN " + (rail.MinimumLimitUv / 1000U) + " mV";
        }

        private static string TuningValue(int? value, string unit)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + " " + unit : "—";
        }

        private static string TuningRange(int? minimum, int? maximum, string unit)
        {
            return minimum.HasValue && maximum.HasValue ? "允许 " + minimum.Value + ".." + maximum.Value + " " + unit : "驱动范围不可用";
        }

        private static string VfSummary(GpuSnapshot sample)
        {
            if (sample.VfPoints.Count == 0) return "RTX 50 Status/Control";
            VfPointSnapshot first = sample.VfPoints[0];
            VfPointSnapshot last = sample.VfPoints[sample.VfPoints.Count - 1];
            return (first.VoltageUv / 1000U) + ".." + (last.VoltageUv / 1000U) + " mV";
        }

        private static string XbarOffset(GpuSnapshot sample)
        {
            return ClockDomainOffset(sample.Xbar);
        }

        private static string ClockDomainOffset(XbarSnapshot domain)
        {
            return domain != null && domain.CurrentOffsetKHz.HasValue ? (domain.CurrentOffsetKHz.Value / 1000) + " MHz" : "—";
        }

        private static string XbarSummary(GpuSnapshot sample)
        {
            return ClockDomainSummary(sample.Xbar, "Crossbar");
        }

        private static string ClockDomainSummary(XbarSnapshot domain, string name)
        {
            if (domain == null || !domain.MinimumOffsetMHz.HasValue || !domain.MaximumOffsetMHz.HasValue)
                return name + " ClockDomains 不可用";
            string current = domain.CurrentOffsetKHz.HasValue ? "当前 " + (domain.CurrentOffsetKHz.Value / 1000) + " MHz · " : string.Empty;
            return current + "允许 " + domain.MinimumOffsetMHz.Value + ".." + domain.MaximumOffsetMHz.Value + " MHz";
        }

        private static string PowerMonitorBoard(GpuSnapshot sample)
        {
            return sample.PowerTelemetry != null && sample.PowerTelemetry.Monitor != null
                ? sample.PowerTelemetry.Monitor.BoardPowerWatts.ToString("N2", CultureInfo.InvariantCulture) + " W"
                : "—";
        }

        private static string TopologyPower(GpuSnapshot sample, bool chip)
        {
            if (sample.PowerTelemetry == null || sample.PowerTelemetry.Topology == null) return "—";
            double? value = chip ? sample.PowerTelemetry.Topology.ChipPowerWatts : sample.PowerTelemetry.Topology.BoardPowerWatts;
            return value.HasValue ? value.Value.ToString("N2", CultureInfo.InvariantCulture) + " W" : "—";
        }

        private static string SessionEnergy(GpuSnapshot sample)
        {
            return sample.PowerTelemetry != null && sample.PowerTelemetry.Monitor != null
                ? sample.PowerTelemetry.Monitor.PrimarySessionEnergyWh.ToString("N4", CultureInfo.InvariantCulture) + " Wh"
                : "—";
        }

        private static Brush Brush(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }
    }
}
