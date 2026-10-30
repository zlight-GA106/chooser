using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace Chooser
{
    [DataContract]
    public sealed class XmlGuideDocument
    {
        [DataMember] public string Title { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public string ExampleFile { get; set; }
        [DataMember] public List<XmlGuideSection> Sections { get; set; }
    }

    [DataContract]
    public sealed class XmlGuideSection
    {
        [DataMember] public string Title { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Code { get; set; }
    }

    public class MainWindow : Window
    {
        readonly Store store;
        readonly AppState data;
        StackPanel page;
        ScrollViewer scroll;
        TextBlock status;
        ComboBox bankPicker;
        TextBox rangeStart, rangeEnd;
        TextBlock bankHint;
        Button startButton, examButton, nextButton;
        readonly Dictionary<string, Button> optionButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, TextBlock> optionMarkers = new Dictionary<string, TextBlock>();
        string view = "home";
        string wrongBank = "";
        int wrongMinimum = 1, wrongPage;
        bool fullScreen;
        WindowState previousState;
        bool savingFailed;
        string managementQuery = "";
        string viewedBankId = "";
        string viewedQuestionQuery = "";
        int viewedQuestionPage;

        public MainWindow(Store store, AppState data)
        {
            this.store = store; this.data = data;
            Title = "选择器 by zlight106";
            Width = 1080; Height = 880; MinWidth = 720; MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            InstallStyles(); ApplyTheme();
            AllowDrop = true;
            DragOver += delegate(object s, DragEventArgs e) {
                e.Effects = view == "quiz" ? DragDropEffects.None : (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None);
                e.Handled = true;
            };
            Drop += delegate(object s, DragEventArgs e) {
                if (view == "quiz") return;
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null) ImportFiles(paths);
            };
            PreviewKeyDown += OnKey;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = !Save(); };
            ShowHome();
            Loaded += delegate {
                if (!String.IsNullOrEmpty(store.LoadWarning)) MessageBox.Show(this, store.LoadWarning, "数据恢复", MessageBoxButton.OK, MessageBoxImage.Information);
            };
        }

        void InstallStyles()
        {
            const string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'>
 <Setter Property='FontSize' Value='14'/><Setter Property='FontWeight' Value='Medium'/><Setter Property='Foreground' Value='{DynamicResource Primary}'/>
 <Setter Property='Background' Value='{DynamicResource Surface}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='BorderThickness' Value='1'/>
 <Setter Property='Padding' Value='20,11'/><Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='shell' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='{DynamicResource Primary}'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Opacity' Value='0.65'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.38'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='TextBox'>
 <Setter Property='FontSize' Value='16'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Surface}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='13,11'/><Setter Property='VerticalContentAlignment' Value='Center'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border x:Name='shell' CornerRadius='8' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ScrollViewer x:Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='{DynamicResource Primary}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='ComboBox'>
 <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Surface}'/><Setter Property='FontSize' Value='16'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='Padding' Value='13,12'/><Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton Focusable='False' Foreground='{TemplateBinding Foreground}' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8'><Grid><ContentPresenter Margin='13,12,35,12' TextElement.Foreground='{TemplateBinding Foreground}' Content='{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}'/><TextBlock Text='⌄' Foreground='{DynamicResource Muted}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,14,0'/></Grid></Border></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'><Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='320' Padding='4'><ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Padding' Value='12,10'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='item' CornerRadius='5' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource Tint}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource Tint}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='Expander'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='Transparent'/><Setter Property='FontSize' Value='15'/></Style>
</ResourceDictionary>";
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }

        void ApplyTheme()
        {
            bool d = data.Dark;
            Resources["Primary"] = Brush(d ? "#A8C7FA" : "#0B57D0");
            Resources["OnPrimary"] = Brush(d ? "#062E6F" : "#FFFFFF");
            Resources["Canvas"] = Brush(d ? "#10141B" : "#F0F4F8");
            Resources["Surface"] = Brush(d ? "#1B2029" : "#FFFFFF");
            Resources["Tint"] = Brush(d ? "#243C60" : "#EAF2FF");
            Resources["Ink"] = Brush(d ? "#E5EAF3" : "#172338");
            Resources["Muted"] = Brush(d ? "#B2BED0" : "#607086");
            Resources["Line"] = Brush(d ? "#3C485A" : "#D4DEEB");
            Resources["Success"] = Brush(d ? "#8DDEA5" : "#16713D");
            Resources["Error"] = Brush(d ? "#FFB4AB" : "#B3261E");
            SetResourceReference(BackgroundProperty, "Canvas");
            SetResourceReference(ForegroundProperty, "Ink");
        }
        static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        Brush Color(string resource) { return (Brush)Resources[resource]; }
        double Z { get { return data.Zoom < 0.85 || data.Zoom > 1.5 ? 1 : data.Zoom; } }
        TextBlock Text(string value, double size, string color)
        {
            return new TextBlock { Text = value, FontSize = size * Z, Foreground = Color(color), TextWrapping = TextWrapping.Wrap, LineHeight = size * Z * 1.55 };
        }
        TextBlock Label(string value) { var t = Text(value, 13, "Muted"); t.Margin = new Thickness(0, 0, 0, 8); return t; }
        Button Action(string label, Action action, bool primary)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 10, 10), FontSize = 14 * Z };
            if (primary) { b.Background = Color("Primary"); b.Foreground = Color("OnPrimary"); b.BorderBrush = Color("Primary"); }
            b.Click += delegate { action(); };
            return b;
        }
        WrapPanel Actions() { return new WrapPanel { Margin = new Thickness(0, 22, 0, 0) }; }
        Border Card(StackPanel inner)
        {
            return new Border { Background = Color("Surface"), BorderBrush = Color("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(30), Margin = new Thickness(0, 0, 0, 20), Child = inner };
        }
        StackPanel Heading(string title, string subtitle)
        {
            var inner = new StackPanel();
            var titleText = Text(title, 28, "Ink"); titleText.FontWeight = FontWeights.SemiBold;
            inner.Children.Add(titleText);
            var sub = Text(subtitle, 14, "Muted"); sub.Margin = new Thickness(0, 4, 0, 23);
            inner.Children.Add(sub); return inner;
        }
        void Begin(string name)
        {
            view = name;
            optionButtons.Clear(); optionMarkers.Clear();
            var shell = new DockPanel();
            status = Text(savingFailed ? "保存失败，请检查磁盘或数据目录权限" : "", 11, "Error");
            status.Margin = new Thickness(28, 5, 28, 10);
            status.Visibility = savingFailed ? Visibility.Visible : Visibility.Collapsed;
            DockPanel.SetDock(status, Dock.Bottom); shell.Children.Add(status);
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var outer = new StackPanel { MaxWidth = 930, Margin = new Thickness(26, 20, 26, 16) };
            scroll.Content = outer; shell.Children.Add(scroll);
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 24), LastChildFill = true };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var minus = Action("A−", delegate { ChangeZoom(-0.1); }, false); minus.Padding = new Thickness(12, 7, 12, 7); minus.ToolTip = "缩小字号";
            var plus = Action("A+", delegate { ChangeZoom(0.1); }, false); plus.Padding = new Thickness(12, 7, 12, 7); plus.ToolTip = "放大字号";
            var theme = Action(data.Dark ? "浅色" : "深色", delegate { data.Dark = !data.Dark; Save(); ApplyTheme(); Redraw(); }, false); theme.Padding = new Thickness(12, 7, 12, 7);
            var help = Action("帮助", ShowHelp, false); help.Padding = new Thickness(12, 7, 12, 7); help.ToolTip = "题库 XML 写作指南（F1）";
            var full = Action(fullScreen ? "退出全屏" : "全屏 F11", ToggleFull, false); full.Padding = new Thickness(12, 7, 12, 7); full.Margin = new Thickness(0, 0, 0, 10);
            controls.Children.Add(minus); controls.Children.Add(plus); controls.Children.Add(theme); controls.Children.Add(help); controls.Children.Add(full);
            DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
            var mark = new Border { Width = 36, Height = 36, Background = Brush("#008080"), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 11, 0) };
            var glyph = new Grid { Width = 36, Height = 36 };
            glyph.Children.Add(new Border { Width = 22, Height = 24, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            glyph.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 11,18 L 16,23 L 25,12"), Stroke = Brushes.White, StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            mark.Child = glyph; System.Windows.Automation.AutomationProperties.SetName(mark, "选择器勾选图标");
            brand.Children.Add(mark); var logo = Text("选择器", 17, "Ink"); logo.FontWeight = FontWeights.SemiBold; brand.Children.Add(logo);
            header.Children.Add(brand); outer.Children.Add(header);
            page = new StackPanel(); outer.Children.Add(page); Content = shell;
        }
        void Redraw()
        {
            if (view == "quiz") ShowQuestion();
            else if (view == "result") ShowResult();
            else if (view == "manage") ShowManage();
            else if (view == "wrong") ShowWrongs();
            else if (view == "help") ShowHelp();
            else if (view == "bank") ShowBank(viewedBankId);
            else ShowHome();
        }

        static XmlGuideDocument ReadGuide()
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("xml-guide.json"))
            {
                if (resource != null) return (XmlGuideDocument)new DataContractJsonSerializer(typeof(XmlGuideDocument)).ReadObject(resource);
            }
            // Core-only verification builds do not embed release resources.
            return new XmlGuideDocument { Title = "题库 XML 写作指南", Version = "1.4.0", ExampleFile = "example.xml", Sections = new List<XmlGuideSection> { new XmlGuideSection { Title = "XML 题库", Text = "正式单文件版内置完整指南及可下载示例。题库使用 <quiz>、<question>、<text>、<option id=\"A\"> 和 <answer>，每题固定 1 分。" } } };
        }
        static string ReadExample()
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("example.xml"))
            {
                if (resource == null) throw new InvalidDataException("示例资源未找到，请使用完整的选择器单文件版。");
                using (var reader = new StreamReader(resource, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
        void ShowHelp()
        {
            Begin("help");
            var guide = ReadGuide();
            var inner = new StackPanel();
            var heading = Text(guide.Title, 28, "Ink"); heading.FontWeight = FontWeights.SemiBold; inner.Children.Add(heading);
            var actions = Actions(); actions.Margin = new Thickness(0, 18, 0, 8);
            actions.Children.Add(Action("返回首页", ShowHome, false));
            if (data.Active != null && ActiveBank != null && (!data.Active.Completed || data.Active.FeedbackPending)) actions.Children.Add(Action("返回答题", ShowQuestion, false));
            actions.Children.Add(Action("下载示例 XML", ExportExample, true)); inner.Children.Add(actions);
            foreach (var section in guide.Sections)
            {
                var title = Text(section.Title, 19, "Ink"); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 21, 0, 8); inner.Children.Add(title);
                inner.Children.Add(Text(section.Text, 14, "Muted"));
                if (!String.IsNullOrWhiteSpace(section.Code))
                {
                    var code = new TextBox { Text = section.Code, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 13 * Z, Background = Color("Tint"), Margin = new Thickness(0, 13, 0, 2), Padding = new Thickness(15), BorderBrush = Color("Line") };
                    System.Windows.Automation.AutomationProperties.SetName(code, section.Title + " XML 示例"); inner.Children.Add(code);
                }
            }
            var version = Text("选择器 v" + guide.Version, 12, "Muted"); version.Margin = new Thickness(0, 25, 0, 0); inner.Children.Add(version);
            page.Children.Add(Card(inner));
        }
        void ExportExample()
        {
            var dialog = new SaveFileDialog { Title = "保存示例 XML 题库", Filter = "XML 题库 (*.xml)|*.xml", FileName = "选择器-示例题库.xml" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, ReadExample(), new UTF8Encoding(false)); MessageBox.Show(this, "示例题库已保存，可修改后导入。", "示例已保存", MessageBoxButton.OK, MessageBoxImage.Information); }
            catch (Exception error) { MessageBox.Show(this, "保存失败：" + error.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void ChangeZoom(double amount) { data.Zoom = Math.Max(0.85, Math.Min(1.5, Math.Round(Z + amount, 2))); Save(); Redraw(); }
        void ToggleFull()
        {
            if (!fullScreen)
            {
                previousState = WindowState; WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; fullScreen = true;
            }
            else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = previousState; fullScreen = false; }
            Redraw();
        }
        bool Save()
        {
            try
            {
                store.Save(data); savingFailed = false;
                if (status != null) { status.Text = ""; status.Visibility = Visibility.Collapsed; }
                return true;
            }
            catch (Exception ex)
            {
                savingFailed = true;
                if (status != null) { status.Text = "保存失败：" + ex.Message; status.Foreground = Color("Error"); status.Visibility = Visibility.Visible; }
                MessageBox.Show(this, "未能保存到磁盘，本次窗口中的数据仍保留。\n请检查磁盘空间和数据目录权限后重试。\n\n" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        Bank SelectedBank { get { return data.Banks.FirstOrDefault(b => b.Id == data.SelectedBankId) ?? data.Banks.FirstOrDefault(); } }
        Bank ActiveBank { get { return data.Active == null ? null : data.Banks.FirstOrDefault(b => b.Id == data.Active.BankId); } }
        void ShowHome()
        {
            Begin("home");
            var inner = new StackPanel();
            var heading = new DockPanel { LastChildFill = true };
            var manage = Action("管理题库", ShowManage, false); manage.Margin = new Thickness(10, 0, 0, 0); manage.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(manage, Dock.Right); heading.Children.Add(manage);
            var ht = new StackPanel();
            var title = Text("题库选择器", 30, "Ink"); title.FontWeight = FontWeights.SemiBold; ht.Children.Add(title);
            heading.Children.Add(ht); heading.Margin = new Thickness(0, 0, 0, 24); inner.Children.Add(heading);
            inner.Children.Add(Label("选择已有题库"));
            bankPicker = new ComboBox { Margin = new Thickness(0, 0, 0, 16), FontSize = 16 * Z };
            foreach (var bank in data.Banks)
            {
                bankPicker.Items.Add(new ComboBoxItem { Content = bank.Name + "  ·  " + bank.Questions.Count + " 题", Tag = bank.Id });
            }
            inner.Children.Add(bankPicker);
            var uploadContent = new StackPanel();
            var uploadText = Text("＋  导入 XML 题库", 16, "Primary"); uploadText.FontWeight = FontWeights.Medium; uploadText.HorizontalAlignment = HorizontalAlignment.Center;
            uploadContent.Children.Add(uploadText);
            var upload = Action("", PickFiles, false); upload.Content = uploadContent; upload.ToolTip = "选择 XML 文件或拖入窗口"; upload.Background = Color("Tint"); upload.BorderBrush = Color("Tint"); upload.Padding = new Thickness(20, 21, 20, 21); upload.Margin = new Thickness(0, 0, 0, 23); upload.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            inner.Children.Add(upload);
            var ranges = new Grid(); ranges.ColumnDefinitions.Add(new ColumnDefinition()); ranges.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); ranges.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel(); left.Children.Add(Label("起始题号")); rangeStart = new TextBox { Text = "1", FontSize = 16 * Z, MaxLength = 7 }; left.Children.Add(rangeStart); ranges.Children.Add(left);
            var right = new StackPanel(); right.Children.Add(Label("结束题号")); rangeEnd = new TextBox { FontSize = 16 * Z, MaxLength = 7 }; right.Children.Add(rangeEnd); Grid.SetColumn(right, 2); ranges.Children.Add(right);
            inner.Children.Add(ranges); bankHint = Text("", 12, "Muted"); bankHint.Margin = new Thickness(0, 9, 0, 0); inner.Children.Add(bankHint);
            var options = new WrapPanel { Margin = new Thickness(0, 17, 0, 0) };
            var showAnswers = new CheckBox { Content = "显示答案", IsChecked = data.ShowAnswers, Foreground = Color("Ink"), FontSize = 14 * Z, Margin = new Thickness(0, 0, 24, 6), VerticalContentAlignment = VerticalAlignment.Center };
            showAnswers.ToolTip = "答题时显示正确答案和题库解析";
            showAnswers.Checked += delegate { data.ShowAnswers = true; Save(); };
            showAnswers.Unchecked += delegate { data.ShowAnswers = false; Save(); };
            var immediate = new CheckBox { Content = "立即评分", IsChecked = data.ImmediateScoring, Foreground = Color("Ink"), FontSize = 14 * Z, Margin = new Thickness(0, 0, 0, 6), VerticalContentAlignment = VerticalAlignment.Center };
            immediate.ToolTip = "提交答案后立刻显示本题评分，再进入下一题";
            immediate.Checked += delegate { data.ImmediateScoring = true; Save(); };
            immediate.Unchecked += delegate { data.ImmediateScoring = false; Save(); };
            options.Children.Add(showAnswers); options.Children.Add(immediate); inner.Children.Add(options);
            var actions = Actions();
            startButton = Action("开始答题  →", delegate { Start(false); }, true); actions.Children.Add(startButton);
            examButton = Action("随机考试", delegate { Start(true); }, false); actions.Children.Add(examButton);
            if (data.Active != null && ActiveBank != null)
            {
                if (!data.Active.Completed || data.Active.FeedbackPending) actions.Children.Add(Action("继续上次进度", ShowQuestion, false));
                else actions.Children.Add(Action("查看上次成绩", ShowResult, false));
            }
            inner.Children.Add(actions);
            bankPicker.SelectionChanged += delegate {
                var item = bankPicker.SelectedItem as ComboBoxItem;
                data.SelectedBankId = item == null ? null : (string)item.Tag;
                UpdateBankHint(); Save();
            };
            var chosen = SelectedBank;
            if (chosen != null) bankPicker.SelectedItem = bankPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chosen.Id);
            else UpdateBankHint();
            page.Children.Add(Card(inner));
            var lower = new Grid(); lower.ColumnDefinitions.Add(new ColumnDefinition()); lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); lower.ColumnDefinitions.Add(new ColumnDefinition());
            var library = new StackPanel(); library.Children.Add(Label("本机题库"));
            var libraryCount = Text(data.Banks.Count + " 个题库  /  " + data.Banks.Sum(b => b.Questions.Count) + " 题", 18, "Ink"); libraryCount.FontWeight = FontWeights.Medium; library.Children.Add(libraryCount);
            var card1 = Card(library); card1.Padding = new Thickness(23); lower.Children.Add(card1);
            var wrong = new StackPanel(); wrong.Children.Add(Label("累计错题"));
            var wrongCount = Text(data.Wrongs.Count + " 题  /  " + data.Wrongs.Sum(w => w.Count) + " 次", 18, "Ink"); wrongCount.FontWeight = FontWeights.Medium; wrong.Children.Add(wrongCount);
            var wlink = Action("查看错题本  →", delegate { wrongPage = 0; ShowWrongs(); }, false); wlink.Padding = new Thickness(0, 9, 0, 0); wlink.BorderThickness = new Thickness(0); wlink.Margin = new Thickness(0); wlink.HorizontalAlignment = HorizontalAlignment.Left; wrong.Children.Add(wlink);
            var card2 = Card(wrong); card2.Padding = new Thickness(23); Grid.SetColumn(card2, 2); lower.Children.Add(card2); page.Children.Add(lower);
        }
        void UpdateBankHint()
        {
            var bank = SelectedBank;
            bool exists = bank != null;
            rangeStart.IsEnabled = rangeEnd.IsEnabled = startButton.IsEnabled = examButton.IsEnabled = exists;
            rangeStart.Text = "1"; rangeEnd.Text = exists ? bank.Questions.Count.ToString() : "";
            bankHint.Text = exists ? "共 " + bank.Questions.Count + " 题" : "请先导入 XML 题库";
        }
        void PickFiles()
        {
            var dialog = new OpenFileDialog { Title = "导入 XML 题库", Filter = "XML 题库 (*.xml)|*.xml", Multiselect = true };
            if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames);
        }
        void ImportFiles(string[] paths)
        {
            int imported = 0, duplicate = 0; var errors = new List<string>();
            var candidate = CloneState(data);
            foreach (var path in paths)
            {
                try
                {
                    if (!String.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 .xml 格式的题库。");
                    var bank = XmlQuiz.ParseFile(path);
                    var existing = candidate.Banks.FirstOrDefault(b => b.Id == bank.Id);
                    if (existing != null) { candidate.SelectedBankId = existing.Id; duplicate++; continue; }
                    // Different contents with the same title are kept as a separate bank.
                    if (candidate.Banks.Any(b => b.Name == bank.Name))
                    {
                        string original = bank.Name; int suffix = 2;
                        while (candidate.Banks.Any(b => b.Name == bank.Name)) bank.Name = original + " (" + suffix++ + ")";
                    }
                    candidate.Banks.Add(bank); candidate.SelectedBankId = bank.Id; imported++;
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
            }
            try { CommitState(candidate); }
            catch (Exception error) { MessageBox.Show(this, "导入未能保存，原有题库和记录未更改。\n" + error.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            ShowHome();
            string summary = "已导入 " + imported + " 个题库" + (duplicate > 0 ? "；" + duplicate + " 个已存在，直接使用缓存" : "") + "。";
            if (errors.Count > 0) summary += "\n\n以下文件未导入：\n" + String.Join("\n", errors);
            MessageBox.Show(this, summary, "导入题库", MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        bool CanReplaceProgress()
        {
            return data.Active == null || data.Active.Completed || MessageBox.Show(this, "开始新一轮将替换尚未完成的答题进度。已累计的错题次数会保留。\n\n继续开始？", "开始新一轮", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
        void Start(bool random)
        {
            var bank = SelectedBank; if (bank == null) return;
            int start, end;
            if (!Int32.TryParse(rangeStart.Text, out start) || !Int32.TryParse(rangeEnd.Text, out end) || start < 1 || end < start || end > bank.Questions.Count)
            { MessageBox.Show(this, "请输入有效题号：1 ≤ 起始题号 ≤ 结束题号 ≤ " + bank.Questions.Count + "。", "检查答题范围", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            int count = end - start + 1;
            if (random)
            {
                int? picked = AskCount(start, end, count); if (!picked.HasValue) return; count = picked.Value;
            }
            if (!CanReplaceProgress()) return;
            data.Active = QuizEngine.CreateSession(bank, start, end, count, random, random ? "随机考试" : "顺序练习");
            Save(); ShowQuestion();
        }
        int? AskCount(int start, int end, int maximum)
        {
            var dialog = new Window { Owner = this, Title = "随机考试", Width = 430, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Color("Surface"), Foreground = Color("Ink"), FontFamily = FontFamily };
            dialog.Resources.MergedDictionaries.Add(Resources);
            var body = new StackPanel { Margin = new Thickness(28) };
            body.Children.Add(Text("随机考试", 23, "Ink"));
            var sub = Text("从第 " + start + "–" + end + " 题中随机抽取，不重复出题。", 14, "Muted"); sub.Margin = new Thickness(0, 10, 0, 20); body.Children.Add(sub);
            body.Children.Add(Label("抽取题数（1–" + maximum + "）"));
            var input = new TextBox { Text = Math.Min(20, maximum).ToString(), MaxLength = 7 }; body.Children.Add(input);
            var error = Text("", 12, "Error"); error.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(error);
            int? result = null;
            var actions = Actions(); actions.Children.Add(Action("取消", delegate { dialog.Close(); }, false));
            var confirm = Action("进入考试", delegate {
                int number;
                if (!Int32.TryParse(input.Text, out number) || number < 1 || number > maximum) { error.Text = "请输入 1–" + maximum + " 之间的整数。"; return; }
                result = number; dialog.DialogResult = true;
            }, true); confirm.IsDefault = true; actions.Children.Add(confirm); body.Children.Add(actions); dialog.Content = body;
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            dialog.ShowDialog(); return result;
        }

        void ShowQuestion()
        {
            var bank = ActiveBank; var session = data.Active;
            if (bank == null || session == null) { ShowHome(); return; }
            if (session.Completed && !session.FeedbackPending) { ShowResult(); return; }
            var question = QuizEngine.Displayed(bank, session);
            bool feedback = session.FeedbackPending;
            Begin("quiz");
            var inner = new StackPanel();
            var meta = Text(session.Mode + "  /  " + bank.Name, 13, "Muted"); meta.Margin = new Thickness(0, 0, 0, 18); inner.Children.Add(meta);
            var titleRow = new DockPanel();
            var badgeText = Text((question.Answers.Count > 1 ? "多选" : (question.Options.Count == 2 && question.Options.Any(o => o.Id == "T") && question.Options.Any(o => o.Id == "F") ? "判断" : "单选")) + "  ·  1 分", 12, "Primary");
            var badge = new Border { Background = Color("Tint"), CornerRadius = new CornerRadius(6), Padding = new Thickness(11, 5, 11, 5), Child = badgeText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(badge, Dock.Right); titleRow.Children.Add(badge);
            var title = Text("第 " + (feedback ? session.Position : session.Position + 1) + " 题 / " + session.QuestionIds.Count, 23, "Ink"); title.FontWeight = FontWeights.SemiBold; titleRow.Children.Add(title); inner.Children.Add(titleRow);
            var progress = new ProgressBar { Minimum = 0, Maximum = session.QuestionIds.Count, Value = session.Position, Height = 5, Background = Color("Tint"), Foreground = Color("Primary"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 17, 0, 24) };
            inner.Children.Add(progress);
            var stem = Text(question.Text, 23, "Ink"); stem.FontWeight = FontWeights.Medium; stem.Margin = new Thickness(0, 0, 0, 22); inner.Children.Add(stem);
            foreach (var option in question.Options)
            {
                string id = option.Id;
                var row = new DockPanel { LastChildFill = true };
                var marker = Text(question.Answers.Count > 1 ? "□" : "○", 20, "Muted"); marker.Margin = new Thickness(0, 0, 14, 0); marker.VerticalAlignment = VerticalAlignment.Center; marker.Width = 26;
                DockPanel.SetDock(marker, Dock.Left); row.Children.Add(marker);
                var content = Text(option.Id + ".  " + option.Text, 18, "Ink"); content.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(content);
                var button = Action("", delegate { SelectOption(id); }, false); button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(18, 14, 18, 14); button.Margin = new Thickness(0, 0, 0, 12);
                System.Windows.Automation.AutomationProperties.SetName(button, option.Id + ". " + option.Text);
                optionButtons[id] = button; optionMarkers[id] = marker; inner.Children.Add(button);
            }
            if (feedback || data.ShowAnswers)
            {
                var answerPanel = new StackPanel();
                if (feedback)
                {
                    var attempt = session.Attempts[session.Position - 1];
                    var scoreText = Text((attempt.Correct ? "回答正确  +1 分" : "回答错误  0 分") + "  ·  当前得分 " + QuizEngine.Score(session) + " / " + session.QuestionIds.Count, 17, attempt.Correct ? "Success" : "Error");
                    scoreText.FontWeight = FontWeights.SemiBold; scoreText.Margin = new Thickness(0, 0, 0, 8); answerPanel.Children.Add(scoreText);
                    answerPanel.Children.Add(Text("你的选择：" + AnswersText(question, attempt.Selected), 14, attempt.Correct ? "Success" : "Error"));
                }
                var correctAnswer = Text("正确答案：" + AnswersText(question, question.Answers), 15, "Success"); correctAnswer.FontWeight = FontWeights.Medium; answerPanel.Children.Add(correctAnswer);
                if (!String.IsNullOrWhiteSpace(question.Explanation))
                {
                    var analysis = Text("解析：" + question.Explanation, 14, "Muted"); analysis.Margin = new Thickness(0, 8, 0, 0); answerPanel.Children.Add(analysis);
                }
                inner.Children.Add(new Border { Background = Color("Tint"), Padding = new Thickness(17), CornerRadius = new CornerRadius(10), Child = answerPanel, Margin = new Thickness(0, 5, 0, 0) });
            }
            else
            {
                var helper = Text(question.Answers.Count > 1 ? "多选题：选择全部正确选项，漏选或多选均不得分。" : "选择答案后提交。", 12, "Muted"); helper.Margin = new Thickness(0, 4, 0, 0); inner.Children.Add(helper);
            }
            var actions = Actions();
            var pause = Action("保存并返回", delegate { Save(); ShowHome(); }, false); actions.Children.Add(pause);
            var finish = Action("提前交卷", FinishEarly, false); actions.Children.Add(finish);
            string nextText = feedback ? (session.Completed ? "查看成绩  →" : "下一题  →") : (data.ImmediateScoring ? "提交答案  →" : (session.Position == session.QuestionIds.Count - 1 ? "完成并结算  →" : "提交并下一题  →"));
            nextButton = Action(nextText, SubmitCurrent, true); actions.Children.Add(nextButton); inner.Children.Add(actions);
            page.Children.Add(Card(inner));
            var hint = Text("原题号 " + question.Number + "  ·  已提交 " + session.Position + " 题  ·  答案自动保存\n快捷键：按选项字母或数字 1–9 选择，Enter 提交，F11 全屏。", 12, "Muted"); hint.Margin = new Thickness(6, 0, 0, 0); page.Children.Add(hint);
            RefreshOptions();
            // Focus the page rather than a previously clicked option so Enter submits predictably.
            Focus();
        }
        void SelectOption(string id)
        {
            var s = data.Active; if (s == null || s.Completed || s.FeedbackPending) return;
            var q = QuizEngine.Current(ActiveBank, s);
            if (q.Answers.Count > 1)
            {
                if (s.Draft.Contains(id)) s.Draft.Remove(id); else s.Draft.Add(id);
            }
            else s.Draft = new List<string> { id };
            RefreshOptions(); Save();
        }
        void RefreshOptions()
        {
            var s = data.Active; var q = QuizEngine.Displayed(ActiveBank, s);
            var selectedIds = s.FeedbackPending ? s.Attempts[s.Position - 1].Selected : s.Draft;
            foreach (var pair in optionButtons)
            {
                bool selected = selectedIds.Contains(pair.Key);
                pair.Value.IsHitTestVisible = !s.FeedbackPending;
                pair.Value.Focusable = !s.FeedbackPending;
                pair.Value.Background = Color(selected ? "Tint" : "Surface");
                pair.Value.BorderBrush = Color(selected ? "Primary" : "Line");
                optionMarkers[pair.Key].Text = q.Answers.Count > 1 ? (selected ? "☑" : "□") : (selected ? "●" : "○");
                optionMarkers[pair.Key].Foreground = Color(selected ? "Primary" : "Muted");
            }
            nextButton.IsEnabled = s.FeedbackPending || s.Draft.Count > 0;
        }
        void SubmitCurrent()
        {
            if (view != "quiz" || data.Active == null) return;
            if (data.Active.FeedbackPending)
            {
                QuizEngine.AdvanceFeedback(data.Active); Save();
                if (data.Active.Completed) ShowResult(); else ShowQuestion();
                return;
            }
            if (data.Active.Completed || data.Active.Draft.Count == 0) return;
            QuizEngine.Submit(data, data.Active, ActiveBank, new List<string>(data.Active.Draft));
            Save();
            if (data.Active.Completed && !data.Active.FeedbackPending) ShowResult(); else ShowQuestion();
        }
        void FinishEarly()
        {
            var s = data.Active;
            if (MessageBox.Show(this, "现在交卷并显示成绩？\n当前已选答案会提交，其余未答题按 0 分计算。", "提前交卷", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (s.Draft.Count > 0) QuizEngine.Submit(data, s, ActiveBank, new List<string>(s.Draft));
            s.Completed = true; s.FeedbackPending = false; s.Draft.Clear(); Save(); ShowResult();
        }
        string AnswersText(Question question, IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? new List<string>());
            return String.Join("、", question.Options.Where(o => set.Contains(o.Id)).Select(o => o.Id));
        }
        Border QuestionReview(Bank bank, Question question, IEnumerable<string> selected, string meta, bool expanded)
        {
            var inner = new StackPanel();
            var heading = new StackPanel();
            var metadata = Text(meta, 12, "Primary"); metadata.Margin = new Thickness(0, 0, 0, 6); heading.Children.Add(metadata);
            var qtext = Text(question.Text, 16, "Ink"); qtext.FontWeight = FontWeights.Medium; heading.Children.Add(qtext);
            var expander = new Expander { Header = heading, IsExpanded = expanded, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var details = new StackPanel { Margin = new Thickness(24, 14, 4, 2) };
            var chosen = new HashSet<string>(selected ?? new List<string>());
            foreach (var option in question.Options)
            {
                bool correct = question.Answers.Contains(option.Id), picked = chosen.Contains(option.Id);
                var line = Text(option.Id + ".  " + option.Text + (correct ? "  ✓" : (picked ? "  ×" : "")), 15, correct ? "Success" : (picked ? "Error" : "Muted"));
                line.Margin = new Thickness(0, 0, 0, 8); details.Children.Add(line);
            }
            details.Children.Add(Text("你的选择：" + (chosen.Count == 0 ? "未作答" : AnswersText(question, chosen)), 14, "Error"));
            var answer = Text("正确答案：" + AnswersText(question, question.Answers), 14, "Success"); answer.FontWeight = FontWeights.SemiBold; details.Children.Add(answer);
            if (!String.IsNullOrWhiteSpace(question.Explanation))
            {
                var explanation = Text("解析：" + question.Explanation, 14, "Muted"); explanation.Margin = new Thickness(0, 12, 0, 0); details.Children.Add(explanation);
            }
            expander.Content = details; inner.Children.Add(expander);
            return new Border { Child = inner, Padding = new Thickness(15), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Color("Line"), Margin = new Thickness(0, 0, 0, 12) };
        }
        void ShowResult()
        {
            var s = data.Active; var bank = ActiveBank;
            if (s == null || !s.Completed || bank == null) { ShowHome(); return; }
            if (s.FeedbackPending) { ShowQuestion(); return; }
            Begin("result");
            var inner = Heading("答题结算", s.Mode + "  ·  " + bank.Name);
            int score = QuizEngine.Score(s), wrong = s.Attempts.Count(a => !a.Correct), unanswered = s.QuestionIds.Count - s.Attempts.Count;
            var summary = new StackPanel();
            var result = Text(score.ToString(), 62, "Primary"); result.FontWeight = FontWeights.SemiBold; result.LineHeight = 76 * Z;
            var scoreRow = new StackPanel { Orientation = Orientation.Horizontal }; scoreRow.Children.Add(result);
            var max = Text(" / " + s.QuestionIds.Count + " 分", 22, "Primary"); max.VerticalAlignment = VerticalAlignment.Bottom; max.Margin = new Thickness(5, 0, 0, 10); scoreRow.Children.Add(max); summary.Children.Add(scoreRow);
            summary.Children.Add(Text("每题 1 分  ·  答对 " + score + " 题  ·  答错 " + wrong + " 题  ·  未答 " + unanswered + " 题", 14, "Ink"));
            summary.Children.Add(Text("正确率 " + (s.QuestionIds.Count == 0 ? 0 : Math.Round(100.0 * score / s.QuestionIds.Count)) + "%  ·  共提交 " + s.Attempts.Count + " 题", 13, "Muted"));
            inner.Children.Add(new Border { Background = Color("Tint"), CornerRadius = new CornerRadius(13), Padding = new Thickness(24, 17, 24, 22), Child = summary, Margin = new Thickness(0, 0, 0, 23) });
            var actions = new WrapPanel();
            actions.Children.Add(Action("返回首页", ShowHome, true));
            var wrongIds = s.Attempts.Where(a => !a.Correct).Select(a => a.QuestionId).ToList();
            if (wrongIds.Count > 0) actions.Children.Add(Action("重练本次错题", delegate { StartQuestions(bank, wrongIds, "错题重练"); }, false));
            actions.Children.Add(Action("查看错题本", delegate { wrongBank = bank.Id; wrongPage = 0; ShowWrongs(); }, false));
            inner.Children.Add(actions);
            var label = Text("本次错题回顾", 19, "Ink"); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 18, 0, 16); inner.Children.Add(label);
            foreach (var attempt in s.Attempts.Where(a => !a.Correct))
            {
                var question = bank.Questions.First(q => q.Id == attempt.QuestionId);
                var record = data.Wrongs.FirstOrDefault(w => w.BankId == bank.Id && w.QuestionId == question.Id);
                inner.Children.Add(QuestionReview(bank, question, attempt.Selected, "原题号 " + question.Number + "  ·  累计答错 " + (record == null ? 1 : record.Count) + " 次  ·  点击展开", false));
            }
            if (wrong == 0) inner.Children.Add(Text(unanswered == 0 ? "本次全部答对，继续保持。" : "已提交的题目均答对。", 15, "Success"));
            if (unanswered > 0)
            {
                var unsubmitted = Text("还有 " + unanswered + " 题未作答，已按 0 分计入本轮成绩。未答题不计入错题次数。", 13, "Muted"); unsubmitted.Margin = new Thickness(0, 12, 0, 0); inner.Children.Add(unsubmitted);
            }
            page.Children.Add(Card(inner));
        }
        void StartQuestions(Bank bank, List<string> ids, string mode)
        {
            if (ids.Count == 0 || !CanReplaceProgress()) return;
            data.Active = new Session { BankId = bank.Id, Mode = mode, QuestionIds = new List<string>(ids), Position = 0 };
            data.SelectedBankId = bank.Id; Save(); ShowQuestion();
        }

        void ShowWrongs()
        {
            Begin("wrong");
            var inner = Heading("我的错题本", "按累计答错次数排序；每次提交答错加 1，之后答对仍保留历史次数。");
            var actions = new WrapPanel(); actions.Children.Add(Action("返回首页", ShowHome, false)); inner.Children.Add(actions);
            var statistics = Text("共 " + data.Wrongs.Count + " 道错题  /  累计答错 " + data.Wrongs.Sum(w => w.Count) + " 次", 17, "Primary"); statistics.FontWeight = FontWeights.Medium; statistics.Margin = new Thickness(0, 9, 0, 18); inner.Children.Add(statistics);
            var filters = new Grid(); filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            var bankBox = new StackPanel(); bankBox.Children.Add(Label("题库筛选"));
            var picker = new ComboBox(); picker.Items.Add(new ComboBoxItem { Content = "全部题库", Tag = "" });
            foreach (var b in data.Banks) picker.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b.Id });
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == wrongBank) ?? picker.Items[0]; bankBox.Children.Add(picker); filters.Children.Add(bankBox);
            var minBox = new StackPanel(); minBox.Children.Add(Label("至少答错次数")); var minimum = new TextBox { Text = wrongMinimum.ToString(), MaxLength = 7 }; minBox.Children.Add(minimum); Grid.SetColumn(minBox, 2); filters.Children.Add(minBox); inner.Children.Add(filters);
            var filterActions = Actions(); filterActions.Margin = new Thickness(0, 13, 0, 5);
            filterActions.Children.Add(Action("筛选", delegate {
                int value;
                if (!Int32.TryParse(minimum.Text, out value) || value < 1) { MessageBox.Show(this, "次数请填写大于 0 的整数。", "检查次数"); return; }
                wrongMinimum = value; wrongBank = (string)((ComboBoxItem)picker.SelectedItem).Tag; wrongPage = 0; ShowWrongs();
            }, true)); inner.Children.Add(filterActions);
            var filtered = data.Wrongs.Where(w => w.Count >= wrongMinimum && (wrongBank == "" || w.BankId == wrongBank))
                .OrderByDescending(w => w.Count).ThenByDescending(w => w.LastAt).ToList();
            var matches = Text("当前筛选 " + filtered.Count + " 题", 12, "Muted"); matches.Margin = new Thickness(0, 3, 0, 16); inner.Children.Add(matches);
            if (filtered.Count > 0 && wrongBank != "")
            {
                var b = data.Banks.FirstOrDefault(x => x.Id == wrongBank);
                if (b != null) inner.Children.Add(Action("重练筛选出的 " + filtered.Count + " 道错题", delegate { StartQuestions(b, filtered.Select(w => w.QuestionId).ToList(), "错题重练"); }, false));
            }
            else if (filtered.Count > 0 && data.Banks.Count > 1)
            {
                var info = Text("选择一个题库后，可一键重练筛选出的错题。", 12, "Muted"); info.Margin = new Thickness(0, 0, 0, 16); inner.Children.Add(info);
            }
            else if (filtered.Count > 0 && data.Banks.Count == 1)
                inner.Children.Add(Action("重练全部错题", delegate { StartQuestions(data.Banks[0], filtered.Select(w => w.QuestionId).ToList(), "错题重练"); }, false));
            const int pageSize = 30;
            wrongPage = Math.Max(0, Math.Min(wrongPage, Math.Max(0, (filtered.Count - 1) / pageSize)));
            foreach (var w in filtered.Skip(wrongPage * pageSize).Take(pageSize))
            {
                var bank = data.Banks.FirstOrDefault(b => b.Id == w.BankId); if (bank == null) continue;
                var q = bank.Questions.FirstOrDefault(x => x.Id == w.QuestionId); if (q == null) continue;
                string date = w.LastAt; DateTime parsed;
                if (DateTime.TryParse(w.LastAt, out parsed)) date = parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                inner.Children.Add(QuestionReview(bank, q, w.LastSelected, "答错 " + w.Count + " 次  ·  " + bank.Name + "  ·  第 " + q.Number + " 题\n最近答错：" + date + "  ·  点击展开", false));
            }
            if (filtered.Count == 0) inner.Children.Add(Text(data.Wrongs.Count == 0 ? "还没有错题记录。完成答题后，答错的题目会显示在这里。" : "没有符合当前筛选条件的错题。", 15, "Muted"));
            if (filtered.Count > pageSize)
            {
                var pages = Actions(); var prev = Action("上一页", delegate { wrongPage--; ShowWrongs(); }, false); prev.IsEnabled = wrongPage > 0; pages.Children.Add(prev);
                var count = Text((wrongPage + 1) + " / " + ((filtered.Count + pageSize - 1) / pageSize), 14, "Muted"); count.Margin = new Thickness(4, 10, 18, 0); pages.Children.Add(count);
                var next = Action("下一页", delegate { wrongPage++; ShowWrongs(); }, false); next.IsEnabled = (wrongPage + 1) * pageSize < filtered.Count; pages.Children.Add(next); inner.Children.Add(pages);
            }
            page.Children.Add(Card(inner));
        }

        void ShowManage()
        {
            Begin("manage");
            var inner = Heading("题库管理", "");
            var actions = new WrapPanel(); actions.Children.Add(Action("返回首页", ShowHome, false)); actions.Children.Add(Action("新建题库", delegate { OpenBankEditor(null); }, true)); actions.Children.Add(Action("导入 XML", PickFiles, false));
            actions.Children.Add(Action("备份全部数据", ExportBackup, false)); actions.Children.Add(Action("恢复数据备份", RestoreBackup, false)); inner.Children.Add(actions);
            var search = new TextBox { Text = managementQuery, FontSize = 15 * Z, Margin = new Thickness(0, 14, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(search, "搜索题库名称或题干");
            inner.Children.Add(Label("搜索题库名称或题干")); inner.Children.Add(search);
            var searchActions = new WrapPanel { Margin = new Thickness(0, 12, 0, 3) };
            searchActions.Children.Add(Action("搜索", delegate { managementQuery = search.Text.Trim(); ShowManage(); }, true));
            searchActions.Children.Add(Action("清空搜索", delegate { managementQuery = ""; ShowManage(); }, false)); inner.Children.Add(searchActions);
            search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { managementQuery = search.Text.Trim(); ShowManage(); e.Handled = true; } };
            var visible = data.Banks.Where(b => String.IsNullOrWhiteSpace(managementQuery) || ContainsText(b.Name, managementQuery) || b.Questions.Any(q => ContainsText(q.Text, managementQuery))).ToList();
            inner.Children.Add(Text("找到 " + visible.Count + " 个题库", 12, "Muted"));
            foreach (var b in visible)
            {
                Bank bank = b; var row = new StackPanel();
                var title = Text(bank.Name, 17, "Ink"); title.FontWeight = FontWeights.Medium; row.Children.Add(title);
                DateTime date; string dateText = DateTime.TryParse(bank.ImportedAt, out date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : bank.ImportedAt;
                row.Children.Add(Text(bank.Questions.Count + " 题  ·  导入于 " + dateText, 12, "Muted"));
                var buttons = Actions(); buttons.Margin = new Thickness(0, 12, 0, 0);
                buttons.Children.Add(Action("使用题库", delegate { data.SelectedBankId = bank.Id; Save(); ShowHome(); }, true));
                buttons.Children.Add(Action("查看", delegate { viewedQuestionQuery = ""; viewedQuestionPage = 0; ShowBank(bank.Id); }, false));
                buttons.Children.Add(Action("编辑 XML", delegate { OpenBankEditor(bank); }, false));
                buttons.Children.Add(Action("重命名", delegate { OpenRenameBank(bank); }, false));
                buttons.Children.Add(Action("导出 XML", delegate { ExportBank(bank); }, false));
                var delete = Action("删除", delegate {
                    try { if (DeleteManagedBank(bank.Id, true)) ShowManage(); }
                    catch (Exception error) { MessageBox.Show(this, "删除失败：" + error.Message, "题库管理", MessageBoxButton.OK, MessageBoxImage.Warning); }
                }, false); delete.Foreground = Color("Error"); buttons.Children.Add(delete); row.Children.Add(buttons);
                inner.Children.Add(new Border { Child = row, Padding = new Thickness(18), BorderBrush = Color("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0) });
            }
            if (visible.Count == 0) inner.Children.Add(Text(data.Banks.Count == 0 ? "暂无题库，可新建或导入 XML。" : "没有匹配的题库。", 16, "Muted"));
            page.Children.Add(Card(inner));
        }

        static bool ContainsText(string value, string query)
        {
            return (value ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        void ShowBank(string bankId)
        {
            var bank = data.Banks.FirstOrDefault(b => b.Id == bankId);
            if (bank == null) { ShowManage(); return; }
            viewedBankId = bankId; Begin("bank");
            var inner = Heading(bank.Name, "共 " + bank.Questions.Count + " 题");
            var actions = new WrapPanel(); actions.Children.Add(Action("返回管理", ShowManage, false));
            actions.Children.Add(Action("编辑 XML", delegate { OpenBankEditor(bank); }, true));
            actions.Children.Add(Action("重命名", delegate { OpenRenameBank(bank); }, false));
            actions.Children.Add(Action("导出 XML", delegate { ExportBank(bank); }, false)); inner.Children.Add(actions);
            inner.Children.Add(Label("搜索本题库题干、选项或解析"));
            var search = new TextBox { Text = viewedQuestionQuery, FontSize = 15 * Z };
            System.Windows.Automation.AutomationProperties.SetName(search, "搜索本题库题目"); inner.Children.Add(search);
            var filters = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            filters.Children.Add(Action("搜索题目", delegate { viewedQuestionQuery = search.Text.Trim(); viewedQuestionPage = 0; ShowBank(bank.Id); }, true));
            filters.Children.Add(Action("清空题目搜索", delegate { viewedQuestionQuery = ""; viewedQuestionPage = 0; ShowBank(bank.Id); }, false)); inner.Children.Add(filters);
            search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { viewedQuestionQuery = search.Text.Trim(); viewedQuestionPage = 0; ShowBank(bank.Id); e.Handled = true; } };
            var questions = bank.Questions.Where(q => String.IsNullOrWhiteSpace(viewedQuestionQuery) || ContainsText(q.Text, viewedQuestionQuery) || ContainsText(q.Explanation, viewedQuestionQuery) || q.Options.Any(o => ContainsText(o.Text, viewedQuestionQuery))).ToList();
            const int size = 30;
            viewedQuestionPage = Math.Max(0, Math.Min(viewedQuestionPage, Math.Max(0, (questions.Count - 1) / size)));
            var count = Text("找到 " + questions.Count + " 题", 12, "Muted"); count.Margin = new Thickness(0, 9, 0, 15); inner.Children.Add(count);
            foreach (var question in questions.Skip(viewedQuestionPage * size).Take(size)) inner.Children.Add(BankQuestionPreview(question));
            if (questions.Count == 0) inner.Children.Add(Text("没有匹配的题目。", 15, "Muted"));
            if (questions.Count > size)
            {
                var pages = Actions(); var prev = Action("上一页", delegate { viewedQuestionPage--; ShowBank(bank.Id); }, false); prev.IsEnabled = viewedQuestionPage > 0; pages.Children.Add(prev);
                var number = Text((viewedQuestionPage + 1) + " / " + ((questions.Count + size - 1) / size), 14, "Muted"); number.Margin = new Thickness(3, 10, 16, 0); pages.Children.Add(number);
                var next = Action("下一页", delegate { viewedQuestionPage++; ShowBank(bank.Id); }, false); next.IsEnabled = (viewedQuestionPage + 1) * size < questions.Count; pages.Children.Add(next); inner.Children.Add(pages);
            }
            page.Children.Add(Card(inner));
        }
        Border BankQuestionPreview(Question question)
        {
            var header = new StackPanel(); var number = Text("第 " + question.Number + " 题 · " + (question.Answers.Count > 1 ? "多选" : "单选 / 判断"), 12, "Primary"); header.Children.Add(number);
            var stem = Text(question.Text, 16, "Ink"); stem.FontWeight = FontWeights.Medium; header.Children.Add(stem);
            var details = new StackPanel { Margin = new Thickness(24, 12, 2, 0) };
            foreach (var option in question.Options)
            {
                bool correct = question.Answers.Contains(option.Id);
                var line = Text(option.Id + ". " + option.Text + (correct ? " ✓" : ""), 15, correct ? "Success" : "Muted"); line.Margin = new Thickness(0, 0, 0, 7); details.Children.Add(line);
            }
            details.Children.Add(Text("正确答案：" + AnswersText(question, question.Answers), 14, "Success"));
            if (!String.IsNullOrWhiteSpace(question.Explanation)) { var explanation = Text("解析：" + question.Explanation, 14, "Muted"); explanation.Margin = new Thickness(0, 10, 0, 0); details.Children.Add(explanation); }
            var expand = new Expander { Header = header, Content = details, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            return new Border { Child = expand, Padding = new Thickness(15), CornerRadius = new CornerRadius(10), BorderBrush = Color("Line"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        }
        static AppState CloneState(AppState state)
        {
            var serializer = new DataContractJsonSerializer(typeof(AppState), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 5000000 });
            using (var memory = new MemoryStream()) { serializer.WriteObject(memory, state); memory.Position = 0; return (AppState)serializer.ReadObject(memory); }
        }
        void CommitState(AppState candidate)
        {
            store.Save(candidate);
            data.Banks = candidate.Banks; data.Wrongs = candidate.Wrongs; data.Active = candidate.Active; data.SelectedBankId = candidate.SelectedBankId;
            data.Dark = candidate.Dark; data.Zoom = candidate.Zoom; data.ShowAnswers = candidate.ShowAnswers; data.ImmediateScoring = candidate.ImmediateScoring;
            savingFailed = false;
            if (status != null) { status.Text = ""; status.Visibility = Visibility.Collapsed; }
        }
        Bank SaveBankDraft(string bankId, string name, string xml, bool allowAffected)
        {
            var candidate = CloneState(data);
            Bank saved = String.IsNullOrEmpty(bankId) ? BankManager.Create(candidate, name, xml) : BankManager.Replace(candidate, bankId, name, xml);
            int removed = data.Wrongs.Count - candidate.Wrongs.Count;
            bool reset = data.Active != null && candidate.Active == null;
            if (!allowAffected && (removed > 0 || reset))
            {
                var impacts = new List<string>();
                if (removed > 0) impacts.Add("清除 " + removed + " 道已修改或删除题目的错题记录");
                if (reset) impacts.Add("清除该题库本轮答题记录");
                if (MessageBox.Show(this, "保存此修改将" + String.Join("，并", impacts) + "。未改动题目的错题次数会保留。\n\n继续保存？", "保存题库修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return null;
            }
            CommitState(candidate);
            if (!String.IsNullOrEmpty(bankId) && bankId != saved.Id)
            {
                if (wrongBank == bankId) wrongBank = saved.Id;
                if (viewedBankId == bankId) viewedBankId = saved.Id;
            }
            return saved;
        }
        Bank RenameManagedBank(string bankId, string name)
        {
            var candidate = CloneState(data); var saved = BankManager.Rename(candidate, bankId, name); CommitState(candidate); return saved;
        }
        bool DeleteManagedBank(string bankId, bool confirm)
        {
            var bank = data.Banks.FirstOrDefault(b => b.Id == bankId); if (bank == null) return false;
            if (confirm && MessageBox.Show(this, "删除题库「" + bank.Name + "」及其错题次数和答题进度？\n如需保留，请先导出 XML 或备份全部数据。", "删除题库", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            var candidate = CloneState(data); BankManager.Delete(candidate, bankId); CommitState(candidate);
            if (wrongBank == bankId) { wrongBank = ""; wrongPage = 0; }
            return true;
        }
        Window ManagementDialog(string title, double width, double height)
        {
            var dialog = new Window { Owner = this, Title = title, Width = width, Height = height, MinWidth = 540, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Color("Surface"), Foreground = Color("Ink"), FontFamily = FontFamily };
            dialog.Resources.MergedDictionaries.Add(Resources); return dialog;
        }
        void OpenRenameBank(Bank bank)
        {
            var dialog = ManagementDialog("重命名题库", 560, 370); dialog.ResizeMode = ResizeMode.NoResize;
            var body = new StackPanel { Margin = new Thickness(28) }; body.Children.Add(Text("重命名题库", 24, "Ink"));
            var label = Label("题库名称"); label.Margin = new Thickness(0, 17, 0, 8); body.Children.Add(label);
            var input = new TextBox { Text = bank.Name, MaxLength = 120 }; System.Windows.Automation.AutomationProperties.SetName(input, "题库名称"); body.Children.Add(input);
            var error = Text("", 13, "Error"); error.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(error);
            bool saved = false;
            var actions = Actions(); actions.Children.Add(Action("取消", delegate { dialog.Close(); }, false));
            var save = Action("保存名称", delegate {
                try { var renamed = RenameManagedBank(bank.Id, input.Text); saved = true; dialog.DialogResult = true; if (view == "bank") ShowBank(renamed.Id); else { managementQuery = ""; ShowManage(); } }
                catch (Exception ex) { error.Text = ex.Message; }
            }, true); save.IsDefault = true; actions.Children.Add(save); body.Children.Add(actions); dialog.Content = body;
            dialog.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!saved && input.Text != bank.Name && MessageBox.Show(dialog, "名称尚未保存，放弃修改？", "未保存的修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); }; dialog.ShowDialog();
        }
        void OpenBankEditor(Bank bank)
        {
            bool create = bank == null;
            string originalName = create ? "新题库" : bank.Name;
            string originalXml;
            if (create) { try { originalXml = ReadExample(); } catch { originalXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<quiz title=\"新题库\">\n  <question type=\"multiple_choice\">\n    <text>在这里填写题干</text>\n    <option id=\"A\">选项一</option>\n    <option id=\"B\">选项二</option>\n    <answer>A</answer>\n  </question>\n</quiz>"; } }
            else originalXml = BankManager.CurrentXml(bank);
            originalXml = FormatEditorXml(originalXml, originalName);
            var dialog = ManagementDialog(create ? "新建题库" : "编辑题库 XML", 900, 740);
            var body = new DockPanel { Margin = new Thickness(26), LastChildFill = true };
            var top = new StackPanel(); top.Children.Add(Text(create ? "新建题库" : "编辑题库 XML", 24, "Ink"));
            var nameLabel = Label("题库名称"); nameLabel.Margin = new Thickness(0, 14, 0, 8); top.Children.Add(nameLabel);
            var name = new TextBox { Text = originalName, MaxLength = 120, FontSize = 15 * Z }; System.Windows.Automation.AutomationProperties.SetName(name, "题库名称"); top.Children.Add(name);
            var xmlLabel = Label("XML 内容"); xmlLabel.ToolTip = "保存时使用上方填写的题库名称。"; xmlLabel.Margin = new Thickness(0, 15, 0, 8); top.Children.Add(xmlLabel); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
            var error = Text("", 13, "Error"); error.MaxHeight = 90; error.Margin = new Thickness(0, 10, 0, 2);
            var bottom = new StackPanel(); bottom.Children.Add(error); var actions = Actions(); actions.Margin = new Thickness(0, 12, 0, 0); bottom.Children.Add(actions); DockPanel.SetDock(bottom, Dock.Bottom); body.Children.Add(bottom);
            var xml = new TextBox { Text = originalXml, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 13 * Z, Padding = new Thickness(12), VerticalContentAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(xml, "题库 XML 内容"); body.Children.Add(xml);
            bool saved = false;
            actions.Children.Add(Action("校验 XML", delegate { try { var preview = BankManager.Preview(name.Text, xml.Text); error.Text = "校验通过，共 " + preview.Questions.Count + " 题。"; error.Foreground = Color("Success"); } catch (Exception ex) { error.Text = ex.Message; error.Foreground = Color("Error"); } }, false));
            actions.Children.Add(Action("取消", delegate { dialog.Close(); }, false));
            actions.Children.Add(Action("保存题库", delegate {
                try { var result = SaveBankDraft(create ? null : bank.Id, name.Text, xml.Text, false); if (result == null) return; saved = true; dialog.DialogResult = true; viewedQuestionQuery = ""; viewedQuestionPage = 0; ShowBank(result.Id); }
                catch (Exception ex) { error.Text = ex.Message; error.Foreground = Color("Error"); }
            }, true));
            dialog.Content = body;
            dialog.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!saved && (name.Text != originalName || xml.Text != originalXml) && MessageBox.Show(dialog, "题库尚未保存，放弃编辑？", "未保存的修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
            dialog.Loaded += delegate { name.Focus(); if (create) name.SelectAll(); }; dialog.ShowDialog();
        }
        static string FormatEditorXml(string xml, string name)
        {
            try
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32L * 1024L * 1024L };
                using (var input = new StringReader(xml))
                using (var reader = System.Xml.XmlReader.Create(input, settings))
                {
                    var document = System.Xml.Linq.XDocument.Load(reader);
                    if (document.Root != null) document.Root.SetAttributeValue("title", name);
                    document.Declaration = new System.Xml.Linq.XDeclaration("1.0", "utf-8", null);
                    return document.Declaration + Environment.NewLine + document.ToString();
                }
            }
            catch { return xml; }
        }
        static string FileNameSafe(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return String.IsNullOrWhiteSpace(value) ? "题库" : value;
        }
        void ExportBank(Bank bank)
        {
            var dialog = new SaveFileDialog { Title = "导出 XML 题库", Filter = "XML 题库 (*.xml)|*.xml", FileName = FileNameSafe(bank.Name) + ".xml" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, bank.Xml, new UTF8Encoding(false)); MessageBox.Show(this, "已导出 XML 题库。", "导出完成"); }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败"); }
        }
        void ExportBackup()
        {
            var dialog = new SaveFileDialog { Title = "备份全部题库和记录", Filter = "选择器备份 (*.json)|*.json", FileName = "选择器备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" };
            if (dialog.ShowDialog(this) != true || !Save()) return;
            try
            {
                string source = Path.Combine(store.DirectoryPath, "state.json");
                if (String.Equals(Path.GetFullPath(source), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)) throw new IOException("请将备份保存到缓存目录以外的位置。");
                File.Copy(source, dialog.FileName, true); MessageBox.Show(this, "题库、错题次数和当前进度已备份。", "备份完成");
            }
            catch (Exception ex) { MessageBox.Show(this, "备份失败：" + ex.Message, "备份失败"); }
        }
        void RestoreBackup()
        {
            var dialog = new OpenFileDialog { Title = "恢复选择器备份", Filter = "选择器备份 (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                AppState restored;
                if (new FileInfo(dialog.FileName).Length > 256L * 1024 * 1024) throw new IOException("备份文件超过 256 MB，无法恢复。");
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppState), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 5000000 });
                using (var input = File.OpenRead(dialog.FileName)) restored = (AppState)serializer.ReadObject(input);
                // Validate through the same serializer/store path before asking to replace live data.
                string checkDirectory = Path.Combine(Path.GetTempPath(), "chooser-restore-" + Guid.NewGuid().ToString("N"));
                try { new Store(checkDirectory).Save(restored); }
                finally { if (Directory.Exists(checkDirectory)) Directory.Delete(checkDirectory, true); }
                if (MessageBox.Show(this, "备份包含 " + restored.Banks.Count + " 个题库、" + restored.Wrongs.Count + " 道错题。\n恢复会替换当前全部题库、错题次数和进度。\n\n确定恢复？", "恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                // Persist replacement before mutating the live state.
                store.Save(restored);
                data.Banks = restored.Banks; data.Wrongs = restored.Wrongs; data.Active = restored.Active; data.SelectedBankId = restored.SelectedBankId; data.Dark = restored.Dark; data.Zoom = restored.Zoom; data.ShowAnswers = restored.ShowAnswers; data.ImmediateScoring = restored.ImmediateScoring;
                wrongBank = ""; wrongMinimum = 1; wrongPage = 0; ApplyTheme(); ShowHome();
                MessageBox.Show(this, "备份已恢复。", "恢复完成");
            }
            catch (Exception ex) { MessageBox.Show(this, "无法恢复此备份：\n" + ex.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void OnKey(object sender, KeyEventArgs e)
        {
            Key actualKey = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : (e.Key == Key.System ? e.SystemKey : e.Key);
            if (actualKey == Key.F11) { ToggleFull(); e.Handled = true; return; }
            if (actualKey == Key.Escape && fullScreen) { ToggleFull(); e.Handled = true; return; }
            // Modal dialogs own their keyboard input.
            if (OwnedWindows.Count > 0) return;
            if (actualKey == Key.F1 && Keyboard.Modifiers == ModifierKeys.None) { ShowHelp(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && actualKey == Key.O && view != "quiz") { PickFiles(); e.Handled = true; return; }
            if (view != "quiz" || data.Active == null || (data.Active.Completed && !data.Active.FeedbackPending) || Keyboard.Modifiers != ModifierKeys.None) return;
            if (actualKey == Key.Enter) { SubmitCurrent(); e.Handled = true; return; }
            if (data.Active.FeedbackPending) return;
            var q = QuizEngine.Current(ActiveBank, data.Active);
            string key = actualKey.ToString().ToUpperInvariant();
            var exact = q.Options.FirstOrDefault(o => o.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { SelectOption(exact.Id); e.Handled = true; return; }
            int number = actualKey >= Key.D1 && actualKey <= Key.D9 ? (int)actualKey - (int)Key.D1 : (actualKey >= Key.NumPad1 && actualKey <= Key.NumPad9 ? (int)actualKey - (int)Key.NumPad1 : -1);
            if (number >= 0 && number < q.Options.Count) { SelectOption(q.Options[number].Id); e.Handled = true; }
        }
    }
}
