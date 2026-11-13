using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        string instanceName = @"Local\DailyChecklist.DesktopWidget";
        string statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DailyChecklistWidget", "checklist.json");

        using (var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Show"))
        using (var instance = new Mutex(false, instanceName))
        {
            if (!Acquire(instance))
            {
                reopen.Set();
                return;
            }

            try
            {
                var app = new Application();
                var store = new ChecklistStore(statePath);
                var state = store.Read();
                state.AdvanceDay(DateTime.Today);
                var widget = new Widget(store, state, reopen);
                app.Run(widget.Window);
            }
            catch (Exception ex)
            {
                string location = "\n\n清单文件位于\n" + statePath;
                MessageBox.Show("每日清单启动失败。\n" + ex.GetBaseException().Message + location,
                    "每日清单", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                instance.ReleaseMutex();
            }
        }
    }

    private static bool Acquire(Mutex instance)
    {
        try { return instance.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }
}

internal sealed class Widget
{
    private readonly ChecklistStore store;
    private ChecklistState state;
    private readonly EventWaitHandle reopen;
    private readonly DispatcherTimer clock;
    private readonly StackPanel rows;
    private readonly TextBox editor;
    private readonly FrameworkElement listView;
    private readonly FrameworkElement editorView;
    private readonly Button editButton;
    private readonly TextBlock count;
    private readonly TextBlock date;
    private readonly TextBlock empty;
    private readonly Border progress;
    private readonly Border card;
    private readonly FrameworkElement dragHandle;
    private readonly ContextMenu options;
    private readonly CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");

    public Window Window { get; private set; }

    public Widget(ChecklistStore store, ChecklistState state, EventWaitHandle reopen)
    {
        this.store = store;
        this.state = state;
        this.reopen = reopen;
        using (var layout = Assembly.GetExecutingAssembly().GetManifestResourceStream("Widget.xaml"))
            Window = (Window)XamlReader.Load(layout);

        rows = Find<StackPanel>("Rows");
        editor = Find<TextBox>("Editor");
        listView = Find<FrameworkElement>("ListView");
        editorView = Find<FrameworkElement>("EditorView");
        editButton = Find<Button>("EditButton");
        count = Find<TextBlock>("CountLabel");
        date = Find<TextBlock>("DateLabel");
        empty = Find<TextBlock>("EmptyText");
        progress = Find<Border>("Progress");
        card = Find<Border>("Card");
        dragHandle = Find<FrameworkElement>("DragHandle");

        editButton.Click += (sender, args) => OpenEditor();
        Find<Button>("SaveButton").Click += (sender, args) => SaveEditor();
        Find<Button>("CancelButton").Click += (sender, args) => CloseEditor();
        dragHandle.MouseLeftButtonDown += Drag;
        Window.PreviewKeyDown += OnKeyDown;
        Window.SourceInitialized += (sender, args) => PlaceWindow();

        Button menuButton = Find<Button>("MenuButton");
        options = new ContextMenu { PlacementTarget = menuButton };
        options.Opened += (sender, args) => PopulateOptions();
        menuButton.ContextMenu = options;
        menuButton.Click += (sender, args) => options.IsOpen = true;

        ApplyAppearance();
        RefreshRows();
        clock = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        clock.Tick += (sender, args) =>
        {
            RefreshDay();
            if (this.reopen != null && this.reopen.WaitOne(0))
                Reveal();
        };
        Window.Closed += (sender, args) => clock.Stop();
        clock.Start();
    }

    private T Find<T>(string name) where T : class
    {
        return (T)Window.FindName(name);
    }

    private void PlaceWindow()
    {
        var handle = new WindowInteropHelper(Window).Handle;
        Matrix transform = HwndSource.FromHwnd(handle).CompositionTarget.TransformFromDevice;
        var primary = Screen.PrimaryScreen.WorkingArea;
        Point primaryEnd = transform.Transform(new Point(primary.Right, primary.Bottom));
        Point primaryStart = transform.Transform(new Point(primary.Left, primary.Top));
        Window.Left = primaryEnd.X - Window.Width - 22;
        Window.Top = primaryStart.Y + 26;
        Window.MaxHeight = Math.Max(220, primaryEnd.Y - primaryStart.Y - 30);

        if (!state.Left.HasValue || !state.Top.HasValue
            || Double.IsNaN(state.Left.Value) || Double.IsNaN(state.Top.Value)
            || Double.IsInfinity(state.Left.Value) || Double.IsInfinity(state.Top.Value))
            return;

        var header = new Rect(state.Left.Value + 12, state.Top.Value + 12, 72, 26);
        foreach (Screen screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            var visible = new Rect(transform.Transform(new Point(area.Left, area.Top)),
                transform.Transform(new Point(area.Right, area.Bottom)));
            if (visible.Contains(header))
            {
                Window.Left = state.Left.Value;
                Window.Top = state.Top.Value;
                break;
            }
        }
    }

    private void Reveal()
    {
        if (Window.WindowState == WindowState.Minimized)
            Window.WindowState = WindowState.Normal;
        Window.Topmost = true;
        Window.Activate();
        Window.Topmost = state.Pinned;
    }

    private void Drag(object sender, MouseButtonEventArgs args)
    {
        if (state.PositionLocked)
            return;

        double oldLeft = Window.Left;
        double oldTop = Window.Top;
        Window.DragMove();
        if (oldLeft == Window.Left && oldTop == Window.Top)
            return;

        ChecklistState updated = state.Copy();
        updated.Left = Window.Left;
        updated.Top = Window.Top;
        Commit(updated);
    }

    private void PopulateOptions()
    {
        options.Items.Clear();
        AddOption(options, "锁定位置", state.PositionLocked, () =>
        {
            ChecklistState updated = state.Copy();
            updated.PositionLocked = !state.PositionLocked;
            if (updated.PositionLocked)
            {
                updated.Left = Window.Left;
                updated.Top = Window.Top;
            }
            if (Commit(updated))
                ApplyAppearance();
        });

        AddOption(options, "保持置顶", state.Pinned, () =>
        {
            ChecklistState updated = state.Copy();
            updated.Pinned = !state.Pinned;
            if (Commit(updated))
                ApplyAppearance();
        });

        var opacity = new MenuItem { Header = "背景透明度" };
        options.Items.Add(opacity);
        AddOpacity(opacity, "更通透", 65);
        AddOpacity(opacity, "默认", 78);
        AddOpacity(opacity, "更清晰", 92);
        options.Items.Add(new Separator());
        AddOption(options, "关闭挂件", false, () => Window.Close());
    }

    private void AddOpacity(MenuItem parent, string label, int value)
    {
        AddOption(parent, label, state.Opacity == value, () =>
        {
            ChecklistState updated = state.Copy();
            updated.Opacity = value;
            if (Commit(updated))
                ApplyAppearance();
        });
    }

    private static void AddOption(ItemsControl parent, string label, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = label, IsChecked = isChecked };
        item.Click += (sender, args) => action();
        parent.Items.Add(item);
    }

    private void ApplyAppearance()
    {
        byte alpha = (byte)Math.Round(state.Opacity * 255.0 / 100.0);
        card.Background = new SolidColorBrush(Color.FromArgb(alpha, 24, 39, 45));
        Window.Topmost = state.Pinned;
        dragHandle.Cursor = state.PositionLocked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void RefreshDay()
    {
        if (state.AdvanceDay(DateTime.Today))
            RefreshRows();
    }

    private void RefreshRows()
    {
        date.Text = DateTime.ParseExact(state.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("M月d日 dddd", chinese);
        rows.Children.Clear();
        for (int index = 0; index < state.Items.Count; index++)
        {
            int itemIndex = index;
            ChecklistItem item = state.Items[index];
            var row = new CheckBox
            {
                Style = (Style)Window.FindResource("TaskRow"),
                Content = item.Text,
                IsChecked = item.Done
            };
            row.Click += (sender, args) => ToggleRow(itemIndex, row);
            rows.Children.Add(row);
        }
        empty.Visibility = state.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
    }

    private void ToggleRow(int index, CheckBox row)
    {
        ChecklistState updated = state.Copy();
        bool newDay = updated.AdvanceDay(DateTime.Today);
        updated.Items[index].Done = !updated.Items[index].Done;
        if (Commit(updated))
        {
            if (newDay)
                RefreshRows();
            else
                UpdateCount();
        }
        else
            row.IsChecked = state.Items[index].Done;
    }

    private void UpdateCount()
    {
        int total = state.Items.Count;
        int done = state.Items.Count(item => item.Done);
        progress.Width = total == 0 ? 0 : 44.0 * done / total;
        count.Text = String.Format("{0}/{1} 已完成", done, total);
    }

    private void OpenEditor()
    {
        editor.Text = String.Join(Environment.NewLine, state.Items.Select(item => item.Text));
        listView.Visibility = Visibility.Collapsed;
        editorView.Visibility = Visibility.Visible;
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
    }

    private void CloseEditor()
    {
        editorView.Visibility = Visibility.Collapsed;
        listView.Visibility = Visibility.Visible;
        editButton.Focus();
    }

    private void SaveEditor()
    {
        ChecklistState updated = state.Copy();
        updated.AdvanceDay(DateTime.Today);
        updated.ReplaceItems(editor.Text);
        if (Commit(updated))
        {
            RefreshRows();
            CloseEditor();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (editorView.Visibility != Visibility.Visible)
            return;
        if (args.Key == Key.Escape)
        {
            CloseEditor();
            args.Handled = true;
        }
        if (args.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveEditor();
            args.Handled = true;
        }
    }

    private bool Commit(ChecklistState updated)
    {
        try
        {
            store.Write(updated);
            state = updated;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window, "未能保存清单，请重试。\n" + ex.GetBaseException().Message,
                "每日清单", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
