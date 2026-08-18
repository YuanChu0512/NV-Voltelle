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
        private const string PageOverview = "æ€»è§ˆ";
        private const string PageTuning = "è°ƒæ ¡";
        private const string PageVoltage = "ç”µå‹ä¸ V/F";
        private const string PagePower = "åŠŸè€—é€šé“";
        private const string PageProfiles = "é…ç½®æ¡£";
        private const string PageInterfaces = "æ¥å£çŠ¶æ€";

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

        public MainWindow(bool startInTray, bool enableHardwareWrites, bool readOnly, bool enableUiQaMode, bool enableUiQaTrayCycle)
        {
            startHiddenInTray = startInTray;
            allowHardwareWrites = enableHardwareWrites;
            forceReadOnly = readOnly;
            uiQaMode = enableUiQaMode;
            uiQaTrayCycle = enableUiQaTrayCycle;
            minimizeToTray = startInTray;
            Title = VoltelleBrand.ProductName + " â€” NVIDIA GPU è°ƒæ ¡å·¥ä½œå°";
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
            trayOpenItem = new Forms.ToolStripMenuItem("æ‰“å¼€ NV Voltelle", null, delegate { ShowFromTray(); });
            trayRefreshItem = new Forms.ToolStripMenuItem("åˆ·æ–°é¥æµ‹", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { RefreshSnapshot(); })); });
            menu.Items.Add(trayOpenItem);
            menu.Items.Add(trayRefreshItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            trayExitItem = new Forms.ToolStripMenuItem("é€€å‡º", null, delegate
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
                Text = "NV Voltelle Â· Mozelle",
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
                ? VoltelleBrand.ProductName + " â€” NVIDIA GPU Tuning Studio"
                : VoltelleBrand.ProductName + " â€” NVIDIA GPU è°ƒæ ¡å·¥ä½œå°";
            ApplyLocalizationToTree(Content as DependencyObject, refreshSources);
            if (languageButton != null)
            {
                languageButton.Content = VoltelleLocalization.IsEnglish ? "ä¸­æ–‡" : "EN";
                languageButton.ToolTip = VoltelleLocalization.IsEnglish ? "åˆ‡æ¢åˆ°ä¸­æ–‡" : "Switch to English";
            }
            UpdateTrayLanguage();
        }

        private void UpdateTrayLanguage()
        {
            if (trayOpenItem == null) return;
            trayOpenItem.Text = VoltelleLocalization.IsEnglish ? "Open NV Voltelle" : "æ‰“å¼€ NV Voltelle";
            trayRefreshItem.Text = VoltelleLocalization.IsEnglish ? "Refresh telemetry" : "åˆ·æ–°é¥æµ‹";
            trayExitItem.Text = VoltelleLocalization.IsEnglish ? "Exit" : "é€€å‡º";
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
               ã=òÚ$z{-®éÜj×‚"’“°¢&÷&FW"&VÖ–æ–ærÒ6&E6†VÆÂ‚“°¢7F6µæVÂ&VÖ–æ–æu7F6²ÒæWr7F6µæVÂ‚“°¢&VÖ–æ–æu7F6²ä6†–ÆG&VâäFB„'VÆÆWB‚.[‹ŠxN[©NyJX˜Şi‹îzK®yºîj~Y(Îš8î™šzîŠêNûÉ¾Kˆ™JîZHŞKØŞy»Nhê^hš~ŠÎûÈÎKˆŞi‹îzK®zîŠêN[Ëz©~8""’“°¢&VÖ–æ–æu7F6²ä6†–ÆG&VâäFB„'VÆÆWB‚.jøşKŠ®šyºîxºÎz¸¾XiXZ^[›nY¹îŠû¾ûÉ¾ZK‹J^šKˆŞKÉ®i*N™H[{.h‰X©şšyºîûÈÎYî{ºŞšyºî{º~{ºŞhš~ŠÎ8""’“°¢&VÖ–æ–æu7F6²ä6†–ÆG&VâäFB„'VÆÆWB‚.YîXûh™y¹Xú®{»NhÈZéîi{n˜^kX¾ûÈÎKˆŞKÉ®YÊiÊ®zîŠêNi{nˆz®XªXiXZ^˜XŞ{Úî8""’“°¢&VÖ–æ–æu7F6²ä6†–ÆG&VâäFB„'VÆÆWB‚.i»NZJ~yºîj~8‹ëyXÎXÎY(Î‹IşY	j[ø>K¸Ş[©NyKyJh‹~˜	jÚ^š¨ÎŠøûÈÎKˆŞh›şŠû®z‹>Zé®h
~8""’“°¢&VÖ–æ–ærä6†–ÆBÒ&VÖ–æ–æu7F6³°¢vRä6†–ÆG&VâäFB‡&VÖ–æ–ær“°¢&WGW&âvS°¢Ğ ¢&—fFR7FF–2T”VÆVÖVçB&öw&W746&B‡7G&–ærfÇVRÂ7G&–ærÆ&VÂÂ7G&–ær†–çB¢°¢&÷&FW"6&BÒ6&E6†VÆÂ‚“°¢7F6µæVÂ7F6²ÒæWr7F6µæVÂ‚“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒfÇVRÂf÷&Vw&÷VæBÒ66VçD''W6‚ÂföçE6—¦RÒ#bÂföçEvV–v‡BÒföçEvV–v‡G2å6VÖ”&öÆBÒ“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒÆ&VÂÂf÷&Vw&÷VæBÒ''W6†W2åv†—FRÂföçE6—¦RÒ"ÂÖ&v–âÒæWrF†–6¶æW72ƒÂBÂÂB’Ò“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒ†–çBÂf÷&Vw&÷VæBÒ×WFVD''W6‚ÂföçE6—¦RÒ’ÂFW‡Ew&–ærÒFW‡Ew&–æråw&Ò“°¢6&Bä6†–ÆBÒ7F6³°¢&WGW&â6&C°¢Ğ ¢&—fFR7FF–2T”VÆVÖVçB'VÆÆWB‡7G&–ærFW‡B¢°¢w&–B&÷rÒæWrw&–B²Ö&v–âÒæWrF†–6¶æW72ƒÂRÂÂR’Ó°¢&÷rä6öÇVÖäFVf–æ—F–öç2äFB†æWr6öÇVÖäFVf–æ—F–öâ²v–GF‚ÒæWrw&–DÆVæwF‚ƒ‚’Ò“°¢&÷rä6öÇVÖäFVf–æ—F–öç2äFB†æWr6öÇVÖäFVf–æ—F–öâ²v–GF‚ÒæWrw&–DÆVæwF‚ƒÂw&–EVæ—EG—Rå7F"’Ò“°¢FW‡D&Æö6²F÷BÒæWrFW‡D&Æö6²²FW‡BÒ.(
""Âf÷&Vw&÷VæBÒ66VçD''W6‚ÂföçE6—¦RÒRÓ°¢FW‡D&Æö6²&öG’ÒæWrFW‡D&Æö6²²FW‡BÒFW‡BÂf÷&Vw&÷VæBÒ''W6‚‚"43„C4S2"’ÂföçE6—¦RÒÂFW‡Ew&–ærÒFW‡Ew&–æråw&Ó°¢&÷rä6†–ÆG&VâäFB†F÷B“°¢w&–Bå6WD6öÇVÖâ†&öG’Â“°¢&÷rä6†–ÆG&VâäFB†&öG’“°¢&WGW&â&÷s°¢Ğ ¢&—fFR7FF–27F6µæVÂvU7F6²‚¢°¢&WGW&âæWr7F6µæVÂ²Ö&v–âÒæWrF†–6¶æW72ƒÂÂÂB’Ó°¢Ğ ¢&—fFR7FF–2&÷&FW"ÖWG&–2‡7G&–ærÆ&VÂÂ7G&–ærfÇVRÂ7G&–ær†–çB¢°¢&÷&FW"6&BÒ6&E6†VÆÂ‚“°¢7F6µæVÂ7F6²ÒæWr7F6µæVÂ‚“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒÆ&VÂÂf÷&Vw&÷VæBÒ×WFVD''W6‚ÂföçE6—¦RÒ’ÂföçEvV–v‡BÒföçEvV–v‡G2å6VÖ”&öÆBÒ“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6°¢°¢FW‡BÒfÇVRÀ¢f÷&Vw&÷VæBÒ''W6†W2åv†—FRÀ¢föçE6—¦RÒ#À¢föçEvV–v‡BÒföçEvV–v‡G2å6VÖ”&öÆBÀ¢Ö&v–âÒæWrF†–6¶æW72ƒÂbÂÂB’À¢FW‡EG&–ÖÖ–ærÒFW‡EG&–ÖÖ–ærä6†&7FW$VÆÆ—6—0¢Ò“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒ†–çBÂf÷&Vw&÷VæBÒ''W6‚‚"3dCt„R"’ÂföçE6—¦RÒ‚ÂFW‡Ew&–ærÒFW‡Ew&–æråw&Ò“°¢6&Bä6†–ÆBÒ7F6³°¢&WGW&â6&C°¢Ğ ¢&—fFR7FF–2&÷&FW"6&E6†VÆÂ‚¢°¢&WGW&âæWr&÷&FW ¢°¢&6¶w&÷VæBÒ6&D''W6‚À¢&÷&FW$''W6‚Ò7G&ö¶T''W6‚À¢&÷&FW%F†–6¶æW72ÒæWrF†–6¶æW72ƒ’À¢6÷&æW%&F—W2ÒæWr6÷&æW%&F—W2ƒB’À¢Ö&v–âÒæWrF†–6¶æW72ƒÂÂ"Â"’À¢FF–ærÒæWrF†–6¶æW72ƒrÂRÂrÂR¢Ó°¢Ğ ¢&—fFR7FF–2T”VÆVÖVçB6V7F–öä†VF–ær‡7G&–ærF—FÆRÂ7G&–ær7V'F—FÆR¢°¢7F6µæVÂ7F6²ÒæWr7F6µæVÂ²Ö&v–âÒæWrF†–6¶æW72ƒÂrÂÂ’Ó°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒF—FÆRÂföçE6—¦RÒbÂföçEvV–v‡BÒföçEvV–v‡G2å6VÖ”&öÆBÒ“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒ7V'F—FÆRÂf÷&Vw&÷VæBÒ×WFVD''W6‚ÂföçE6—¦RÒÂÖ&v–âÒæWrF†–6¶æW72ƒÂBÂÂ’Ò“°¢&WGW&â7F6³°¢Ğ ¢&—fFR7FF–2&÷&FW"ÆW'B‡7G&–ærF—FÆRÂ7G&–ær&öG’Â&ööÂFævW"¢°¢&÷&FW"ÆW'BÒæWr&÷&FW ¢°¢&6¶w&÷VæBÒFævW"ò''W6‚‚"3$s""’¢66VçDF&´''W6‚À¢&÷&FW$''W6‚ÒFævW"ò''W6‚‚"3d#33’"’¢''W6‚‚"3#ƒSsCr"’À¢&÷&FW%F†–6¶æW72ÒæWrF†–6¶æW72ƒ’À¢6÷&æW%&F—W2ÒæWr6÷&æW%&F—W2ƒ’’À¢FF–ærÒæWrF†–6¶æW72ƒBÂÂBÂ’À¢Ö&v–âÒæWrF†–6¶æW72ƒÂÂÂ¢Ó°¢7F6µæVÂ7F6²ÒæWr7F6µæVÂ‚“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒF—FÆRÂf÷&Vw&÷VæBÒFævW"òW'&÷$''W6‚¢66VçD''W6‚ÂföçEvV–v‡BÒföçEvV–v‡G2å6VÖ”&öÆBÂföçE6—¦RÒ"Ò“°¢7F6²ä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒ&öG’Âf÷&Vw&÷VæBÒFævW"ò''W6‚‚"4St#T$"’¢''W6‚‚"4#”C”42"’ÂföçE6—¦RÒÂFW‡Ew&–ærÒFW‡Ew&–æråw&ÂÖ&v–âÒæWrF†–6¶æW72ƒÂBÂÂ’Ò“°¢ÆW'Bä6†–ÆBÒ7F6³°¢&WGW&âÆW'C°¢Ğ ¢&—fFR7FF–2&÷&FW"V×G•7FFR‡7G&–ærFW‡B¢°¢&÷&FW"7FFRÒ6&E6†VÆÂ‚“°¢7FFRåFF–ærÒæWrF†–6¶æW72ƒ#Â3Â#Â3“°¢7FFRä6†–ÆBÒæWrFW‡D&Æö6²²FW‡BÒFW‡BÂf÷&Vw&÷VæBÒ×WFVD''W6‚Â†÷&—¦öçFÄÆ–væÖVçBÒ†÷&—¦öçFÄÆ–væÖVçBä6VçFW"Ó°¢&WGW&â7FFS°¢Ğ ¢&—fFR7FF–2T”VÆVÖVçB6&–Æ—G’‡7G&–ærÆ&VÂÂ&ööÂf–Æ&ÆR¢°¢w&–B&÷rÒæWrw&–B²Ö&v–âÒæWrF†–6¶æW72ƒÂbÂÂb’Ó°¢&÷rä6öÇVÖäFVf–æ—F–öç2äFB†æWr6öÇVÖäFVf–æ—F–öâ²v–GF‚ÒæWrw&–DÆVæwF‚ƒÂw&–EVæ—EG—Rå7F"’Ò“°¢&÷rä6öÇVÖäFVf–æ—F–öç2äFB†æWr6öÇVÖäFVf–æ—F–öâ²v–GF‚Òw&–DÆVæwF‚äWFòÒ“°¢&÷rä6†–ÆG&VâäFB†æWrFW‡D&Æö6²²FW‡BÒÆ&VÂÂf÷&Vw&÷VæBÒ''W6‚‚"43tC$S2"’ÂföçE6—¦RÒÒ“°¢&÷&FW"&FvRÒæWr&÷&FW ¢°¢&6¶w&÷VæBÒf–Æ&ÆRò66VçDF&´''W6‚¢''W6‚‚"33##"’À¢6÷&æW%&F—W2ÒæWr6÷&æW%&F—W2ƒ‚’À¢FF–ærÒæWrF†–6¶æW72ƒ‚Â2Â‚Â2¢Ó°¢&FvRä6†–ÆBÒæWrFW‡D&Æö6²²FW‡BÒf–Æ&ÆRò.XúşyJ‚"¢.iÊ®hùKé²"Âf÷&Vw&÷VæBÒf–Æ&ÆRò66VçD''W6‚¢W'&÷$''W6‚ÂföçE6—¦RÒ’Ó°¢w&–Bå6WD6öÇVÖâ†&FvRÂ“°¢&÷rä6†–ÆG&VâäFB†&FvR“°¢&WGW&â&÷s°¢Ğ ¢&—fFR7FF–2'WGFöâ&–Ö'”'WGFöâ‡7G&–ærFW‡B¢°¢&WGW&âæWr'WGFöà¢°¢6öçFVçBÒFW‡BÀ¢7G–ÆRÒF†VÖVD'WGFöå7G–ÆR‡G'VR¢Ó°¢Ğ ¢&—fFR7FF–2'WGFöâ6V6öæF'”'WGFöâ‡7G&–ærFW‡B¢°¢&WGW&âæWr'WGFöà¢°¢6öçFVçBÒFW‡BÀ¢7G–ÆRÒF†VÖVD'WGFöå7G–ÆR†fÇ6R¢Ó°¢Ğ ¢&—fFR7FF–27G–ÆRF†VÖVD'WGFöå7G–ÆR†&ööÂ&–Ö'’¢°¢7G–ÆR7G–ÆRÒæWr7G–ÆR‡G—Vöb„'WGFöâ’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&6¶w&÷VæE&÷W'G’Â&–Ö'’ò66VçD''W6‚¢6&D†÷fW$''W6‚’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂäf÷&Vw&÷VæE&÷W'G’Â&–Ö'’ò''W6‚‚"3s3b"’¢''W6†W2åv†—FR’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&÷&FW$''W6…&÷W'G’Â&–Ö'’ò66VçD''W6‚¢7G&ö¶T''W6‚’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&÷&FW%F†–6¶æW75&÷W'G’ÂæWrF†–6¶æW72ƒ’’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂåFF–æu&÷W'G’ÂæWrF†–6¶æW72ƒrÂ’ÂrÂ’’’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂäföçEvV–v‡E&÷W'G’ÂföçEvV–v‡G2å6VÖ”&öÆB’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„g&ÖWv÷&´VÆVÖVçBä7W'6÷%&÷W'G’Â7—7FVÒåv–æF÷w2ä–çWBä7W'6÷'2ä†æB’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"…T”VÆVÖVçBå&VæFW%G&ç6f÷&Ô÷&–v–å&÷W'G’ÂæWrö–çBƒãRÂãR’’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"…T”VÆVÖVçBå&VæFW%G&ç6f÷&Õ&÷W'G’ÂæWr66ÆUG&ç6f÷&ÒƒÂ’’“° ¢6öçG&öÅFV×ÆFRFV×ÆFRÒæWr6öçG&öÅFV×ÆFR‡G—Vöb„'WGFöâ’“°¢g&ÖWv÷&´VÆVÖVçDf7F÷'’&÷&FW"ÒæWrg&ÖWv÷&´VÆVÖVçDf7F÷'’‡G—Vöb„&÷&FW"’“°¢&÷&FW"å6WEfÇVR„&÷&FW"ä6÷&æW%&F—W5&÷W'G’ÂæWr6÷&æW%&F—W2ƒ‚’“°¢&÷&FW"å6WD&–æF–ær„&÷&FW"ä&6¶w&÷VæE&÷W'G’ÂæWr&–æF–ær‚$&6¶w&÷VæB"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&÷&FW"å6WD&–æF–ær„&÷&FW"ä&÷&FW$''W6…&÷W'G’ÂæWr&–æF–ær‚$&÷&FW$''W6‚"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&÷&FW"å6WD&–æF–ær„&÷&FW"ä&÷&FW%F†–6¶æW75&÷W'G’ÂæWr&–æF–ær‚$&÷&FW%F†–6¶æW72"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&÷&FW"å6WD&–æF–ær„&÷&FW"åFF–æu&÷W'G’ÂæWr&–æF–ær‚%FF–ær"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“° ¢g&ÖWv÷&´VÆVÖVçDf7F÷'’&W6VçFW"ÒæWrg&ÖWv÷&´VÆVÖVçDf7F÷'’‡G—Vöb„6öçFVçE&W6VçFW"’“°¢&W6VçFW"å6WD&–æF–ær„6öçFVçE&W6VçFW"ä6öçFVçE&÷W'G’ÂæWr&–æF–ær‚$6öçFVçB"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&W6VçFW"å6WD&–æF–ær„6öçFVçE&W6VçFW"ä6öçFVçEFV×ÆFU&÷W'G’ÂæWr&–æF–ær‚$6öçFVçEFV×ÆFR"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&W6VçFW"å6WD&–æF–ær„6öçFVçE&W6VçFW"ä6öçFVçE7G&–ætf÷&ÖE&÷W'G’ÂæWr&–æF–ær‚$6öçFVçE7G&–ætf÷&ÖB"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&W6VçFW"å6WD&–æF–ær„6öçFVçE&W6VçFW"ä†÷&—¦öçFÄÆ–væÖVçE&÷W'G’ÂæWr&–æF–ær‚$†÷&—¦öçFÄ6öçFVçDÆ–væÖVçB"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&W6VçFW"å6WD&–æF–ær„6öçFVçE&W6VçFW"åfW'F–6ÄÆ–væÖVçE&÷W'G’ÂæWr&–æF–ær‚%fW'F–6Ä6öçFVçDÆ–væÖVçB"’²&VÆF—fU6÷W&6RÒ&VÆF—fU6÷W&6RåFV×ÆFVE&VçBÒ“°¢&÷&FW"äVæD6†–ÆB‡&W6VçFW"“°¢FV×ÆFRåf—7VÅG&VRÒ&÷&FW#°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂåFV×ÆFU&÷W'G’ÂFV×ÆFR’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä†÷&—¦öçFÄ6öçFVçDÆ–væÖVçE&÷W'G’Â†÷&—¦öçFÄÆ–væÖVçBä6VçFW"’“°¢7G–ÆRå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂåfW'F–6Ä6öçFVçDÆ–væÖVçE&÷W'G’ÂfW'F–6ÄÆ–væÖVçBä6VçFW"’“° ¢G&–vvW"†÷fW"ÒæWrG&–vvW"²&÷W'G’ÒT”VÆVÖVçBä—4Ö÷W6T÷fW%&÷W'G’ÂfÇVRÒG'VRÓ°¢†÷fW"å6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&6¶w&÷VæE&÷W'G’Â&–Ö'’ò''W6‚‚"3ƒ$T$B"’¢''W6‚‚"3##3#Cr"’’“°¢7G–ÆRåG&–vvW'2äFB††÷fW"“° ¢G&–vvW"&W76VBÒæWrG&–vvW"²&÷W'G’Ò'WGFöä&6Rä—5&W76VE&÷W'G’ÂfÇVRÒG'VRÓ°¢&W76VBå6WGFW'2äFB†æWr6WGFW"…T”VÆVÖVçBå&VæFW%G&ç6f÷&Õ&÷W'G’ÂæWr66ÆUG&ç6f÷&Òƒã“‚Âã“‚’’“°¢7G–ÆRåG&–vvW'2äFB‡&W76VB“° ¢G&–vvW"F—6&ÆVBÒæWrG&–vvW"²&÷W'G’ÒT”VÆVÖVçBä—4Væ&ÆVE&÷W'G’ÂfÇVRÒfÇ6RÓ°¢F—6&ÆVBå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&6¶w&÷VæE&÷W'G’Â''W6‚‚"3##“3b"’’“°¢F—6&ÆVBå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂäf÷&Vw&÷VæE&÷W'G’Â''W6‚‚"3c“ss„"’’“°¢F—6&ÆVBå6WGFW'2äFB†æWr6WGFW"„6öçG&öÂä&÷&FW$''W6…&÷W'G’Â7G&ö¶T''W6‚’“°¢F—6&ÆVBå6WGFW'2äFB†æWr6WGFW"„g&ÖWv÷&´VÆVÖVçBä7W'6÷%&÷W'G’Â7—7FVÒåv–æF÷w2ä–çWBä7W'6÷'2ä'&÷r’“°¢7G–ÆRåG&–vvW'2äFB†F—6&ÆVB“°¢&WGW&â7G–ÆS°¢Ğ ¢&—fFR7FF–27G&–ærf÷&ÖB†F÷V&ÆSòfÇVRÂ7G&–ærVæ—B¢°¢&WGW&âfÇVRä†5fÇVRòfÇVRåfÇVRåFõ7G&–ær‚$ã"Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²""²Væ—B¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ær†W…fÇVR‡V–çCòfÇVR¢°¢&WGW&âfÇVRä†5fÇVRò#‚"²fÇVRåfÇVRåFõ7G&–ær‚%ƒ‚"Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ær÷F–öæÅT–çB‡V–çCòfÇVR¢°¢&WGW&âfÇVRä†5fÇVRòfÇVRåfÇVRåFõ7G&–ær„7VÇGW&T–æfòä–çf&–çD7VÇGW&R’¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ær&–ÅfÇVR„wU6æ6†÷B6×ÆRÂ–çB&–Ä–æFW‚¢°¢föÇFvU&–Ä6öçG&7B&–ÂÒ6×ÆRåföÇFvRÓÒçVÆÂòçVÆÂ¢6×ÆRåföÇFvRäf–æE&–Â‡&–Ä–æFW‚“°¢&WGW&â&–ÂÓÒçVÆÂò.(	B"¢‡&–Âå6Vç6VEWbòã’åFõ7G&–ær‚$ã"Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²"Õb#°¢Ğ ¢&—fFR7FF–27G&–ær&–Å7VÖÖ'’„wU6æ6†÷B6×ÆRÂ–çB&–Ä–æFW‚¢°¢föÇFvU&–Ä6öçG&7B&–ÂÒ6×ÆRåföÇFvRÓÒçVÆÂòçVÆÂ¢6×ÆRåföÇFvRäf–æE&–Â‡&–Ä–æFW‚“°¢–b‡&–ÂÓÒçVÆÂ’&WGW&â%föÇE&–Ç2c"#°¢&WGW&â%$TÂ"²‡&–Âå&VÆ–&–Æ—G”Æ–Ö—EWbòR’²"+rÔ‚"²‡&–ÂäÖ†–×VÔÆ–Ö—EWbòR’²"+rÔ”â"²‡&–ÂäÖ–æ–×VÔÆ–Ö—EWbòR’²"Õb#°¢Ğ ¢&—fFR7FF–27G&–ærGVæ–æufÇVR†–çCòfÇVRÂ7G&–ærVæ—B¢°¢&WGW&âfÇVRä†5fÇVRòfÇVRåfÇVRåFõ7G&–ær„7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²""²Væ—B¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ærGVæ–æu&ævR†–çCòÖ–æ–×VÒÂ–çCòÖ†–×VÒÂ7G&–ærVæ—B¢°¢&WGW&âÖ–æ–×VÒä†5fÇVRbbÖ†–×VÒä†5fÇVRò.XXŠë‚"²Ö–æ–×VÒåfÇVR²"ââ"²Ö†–×VÒåfÇVR²""²Væ—B¢.š›XªˆÈ>Y»NKˆŞXúşyJ‚#°¢Ğ ¢&—fFR7FF–27G&–ærfe7VÖÖ'’„wU6æ6†÷B6×ÆR¢°¢–b‡6×ÆRåfeö–çG2ä6÷VçBÓÒ’&WGW&â%%E‚S7FGW2ô6öçG&öÂ#°¢feö–çE6æ6†÷Bf—'7BÒ6×ÆRåfeö–çG5³Ó°¢feö–çE6æ6†÷BÆ7BÒ6×ÆRåfeö–çG5·6×ÆRåfeö–çG2ä6÷VçBÒÓ°¢&WGW&â†f—'7BåföÇFvUWbòR’²"ââ"²†Æ7BåföÇFvUWbòR’²"Õb#°¢Ğ ¢&—fFR7FF–27G&–ær†&$öfg6WB„wU6æ6†÷B6×ÆR¢°¢&WGW&â6×ÆRå†&"ä7W'&VçDöfg6WD´‡¢ä†5fÇVRò‡6×ÆRå†&"ä7W'&VçDöfg6WD´‡¢åfÇVRò’²"Ô‡¢"¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ær†&%7VÖÖ'’„wU6æ6†÷B6×ÆR¢°¢–b‚6×ÆRå†&"äÖ–æ–×VÔöfg6WDÔ‡¢ä†5fÇVRÇÂ6×ÆRå†&"äÖ†–×VÔöfg6WDÔ‡¢ä†5fÇVR’&WGW&â$6Æö6´FöÖ–ç2%E‚S#°¢&WGW&â.XXŠë‚"²6×ÆRå†&"äÖ–æ–×VÔöfg6WDÔ‡¢åfÇVR²"ââ"²6×ÆRå†&"äÖ†–×VÔöfg6WDÔ‡¢åfÇVR²"Ô‡¢#°¢Ğ ¢&—fFR7FF–27G&–ær÷vW$Ööæ—F÷$&ö&B„wU6æ6†÷B6×ÆR¢°¢&WGW&â6×ÆRå÷vW%FVÆVÖWG'’ÒçVÆÂbb6×ÆRå÷vW%FVÆVÖWG'’äÖöæ—F÷"ÒçVÆÀ¢ò6×ÆRå÷vW%FVÆVÖWG'’äÖöæ—F÷"ä&ö&E÷vW%vGG2åFõ7G&–ær‚$ã""Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²"r ¢¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ærF÷öÆöw•÷vW"„wU6æ6†÷B6×ÆRÂ&ööÂ6†—¢°¢–b‡6×ÆRå÷vW%FVÆVÖWG'’ÓÒçVÆÂÇÂ6×ÆRå÷vW%FVÆVÖWG'’åF÷öÆöw’ÓÒçVÆÂ’&WGW&â.(	B#°¢F÷V&ÆSòfÇVRÒ6†—ò6×ÆRå÷vW%FVÆVÖWG'’åF÷öÆöw’ä6†—÷vW%vGG2¢6×ÆRå÷vW%FVÆVÖWG'’åF÷öÆöw’ä&ö&E÷vW%vGG3°¢&WGW&âfÇVRä†5fÇVRòfÇVRåfÇVRåFõ7G&–ær‚$ã""Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²"r"¢.(	B#°¢Ğ ¢&—fFR7FF–27G&–ær6W76–öäVæW&w’„wU6æ6†÷B6×ÆR¢°¢&WGW&â6×ÆRå÷vW%FVÆVÖWG'’ÒçVÆÂbb6×ÆRå÷vW%FVÆVÖWG'’äÖöæ—F÷"ÒçVÆÀ¢ò6×ÆRå÷vW%FVÆVÖWG'’äÖöæ—F÷"å&–Ö'•6W76–öäVæW&w•v‚åFõ7G&–ær‚$ãB"Â7VÇGW&T–æfòä–çf&–çD7VÇGW&R’²"v‚ ¢¢.(	B#°¢Ğ ¢&—fFR7FF–2''W6‚''W6‚‡7G&–ær†W‚¢°¢&WGW&â„''W6‚–æWr''W6„6öçfW'FW"‚’ä6öçfW'Dg&öÕ7G&–ær††W‚“°¢Ğ¢Ğ§Ğ