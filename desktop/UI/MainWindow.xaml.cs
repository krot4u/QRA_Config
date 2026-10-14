using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QraConfig.Models;
using QraConfig.Services;

namespace QraConfig.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsKeyboardFocusModeProperty = DependencyProperty.Register(
        nameof(IsKeyboardFocusMode), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsKeyboardFocusMode
    {
        get => (bool)GetValue(IsKeyboardFocusModeProperty);
        private set => SetValue(IsKeyboardFocusModeProperty, value);
    }
    private BackendService? _backend;
    private readonly CancellationTokenSource _lifetime = new();
    private ConfigSnapshot? _original, _draft;
    private string _serial = "", _token = "", _mode = "COMMON", _profile = "";
    private string? _localApk;
    private bool _review => Environment.GetCommandLineArgs().Contains("--review");
    private bool _ready, _busy, _building, _closing;
    private readonly HashSet<string> _invalid = new();
    private static Brush Ink => (Brush)Application.Current.FindResource("InkBrush");
    private static Brush Muted => (Brush)Application.Current.FindResource("MutedBrush");
    private static Brush Amber => (Brush)Application.Current.FindResource("AmberBrush");
    private static Brush Surface => (Brush)Application.Current.FindResource("SurfaceBrush");
    private static Brush BorderColor => (Brush)Application.Current.FindResource("BorderBrush");
    private static Brush ErrorColor => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 136, 127));

    public MainWindow()
    {
        InitializeComponent();
        _ready = true;
        AutomationProperties.SetLiveSetting(StatusText, AutomationLiveSetting.Polite);
        SourceInitialized += (_, _) => DarkTitle(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && RefreshButton.IsEnabled) { Refresh_Click(this, new RoutedEventArgs()); e.Handled = true; }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && ApplyButton.IsEnabled) { Apply_Click(this, new RoutedEventArgs()); e.Handled = true; }
            if ((e.Key is Key.Escape or Key.Back) && NavigationDepth > 0) { NavigateBack(); e.Handled = true; }
        };
        RenderEditor();
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => IsKeyboardFocusMode = true;
    private void Window_PreviewPointerDown(object sender, InputEventArgs e) => IsKeyboardFocusMode = false;
    private void MenuScroll_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not FrameworkElement target) return;
        Dispatcher.BeginInvoke(() =>
        {
            target.BringIntoView();
            UpdateMenuFades();
        }, DispatcherPriority.Loaded);
    }
    private void MenuScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateMenuFades();
    private void UpdateMenuFades()
    {
        SetFades(MasterMenuScroll, MasterTopFade, MasterBottomFade);
        var detail = EditorScroll.Visibility == Visibility.Visible ? EditorScroll : DetailMenuScroll;
        SetFades(detail, DetailTopFade, DetailBottomFade);
    }
    private static void SetFades(ScrollViewer viewer, UIElement top, UIElement bottom)
    {
        bool visible = viewer.Visibility == Visibility.Visible && viewer.ScrollableHeight > 0.5;
        top.Visibility = visible && viewer.VerticalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        bottom.Visibility = visible && viewer.VerticalOffset < viewer.ScrollableHeight - 0.5 ? Visibility.Visible : Visibility.Collapsed;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static void DarkTitle(Window window)
    {
        try { int dark = SystemParameters.HighContrast ? 0 : 1; _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, 4); } catch (DllNotFoundException) { }
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            _backend = await Task.Run(() => new BackendService(Environment.GetCommandLineArgs().Contains("--review")), _lifetime.Token);
            await RefreshDevicesAsync();
        }, "Ищем устройства, подключённые по USB…");
    }
    private async Task RunAsync(Func<Task> action, string message)
    {
        if (_busy || _closing) return;
        _busy = true; Status(message, "busy"); UpdateActions();
        try { await action(); }
        catch (OperationCanceledException) { if (!_closing) Status("Операция отменена.", "info"); }
        catch (Exception ex) { if (!_closing) Status(ex is InvalidOperationException ? ex.Message : "Не удалось выполнить действие. Проверьте USB и повторите запрос.", "error"); }
        finally { _busy = false; if (!_closing) UpdateActions(); }
    }
    private void Status(string text, string kind = "info")
    {
        StatusText.Text = text; StatusText.ToolTip = text;
        StatusDot.Fill = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : kind switch
        {
            "error" => ErrorColor, "success" => new SolidColorBrush(Color.FromRgb(95, 211, 141)), "busy" => Amber, _ => Muted
        };
        AutomationProperties.SetName(StatusText, text);
    }
    private void UpdateActions()
    {
        if (!_ready) return;
        DevicePicker.IsEnabled = !_busy && _token.Length == 0;
        RefreshButton.IsEnabled = !_busy && _token.Length == 0 && _backend != null;
        ConnectButton.IsEnabled = !_busy && _backend != null && (_token.Length > 0 || DevicePicker.SelectedItem is DeviceInfo { IsReady: true });
        ConnectButton.Content = _token.Length > 0 ? "Отключить" : "Подключить";
        ReadButton.IsEnabled = !_busy && _token.Length > 0;
        BusyProgress.Visibility = _busy ? Visibility.Visible : Visibility.Hidden;
        BusyProgress.IsIndeterminate = SystemParameters.ClientAreaAnimation;
        BusyProgress.Value = 65;
        ApkSource.IsEnabled = !_busy && _token.Length == 0;
        InstallBeforeConnect.IsEnabled = !_busy && _token.Length == 0;
        BrowseApkButton.IsEnabled = !_busy && _token.Length == 0 && ApkSource.SelectedIndex == 2;
        InstallApkButton.IsEnabled = !_busy && _token.Length == 0 && _backend != null && DevicePicker.SelectedItem is DeviceInfo { IsReady: true };
        UpdateDirty();
    }
    private async Task RefreshDevicesAsync()
    {
        var devices = await _backend!.ListDevicesAsync(_lifetime.Token);
        DevicePicker.ItemsSource = devices.Select(d => new DeviceInfo(d.Serial, d.State,
            d.Model + (d.State == "unauthorized" ? " · разрешите отладку на телефоне" : d.State == "offline" ? " · нет связи" : ""))).ToArray();
        DevicePicker.SelectedIndex = devices.Count == 1 ? 0 : -1;
        Status(devices.Count == 0 ? "Устройство не найдено. Подключите USB-кабель с передачей данных и включите отладку." : "Выберите устройство и разрешите отладку по USB на телефоне.");
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshDevicesAsync, "Ищем устройства по USB…");
    private void Device_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateActions(); }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_token.Length > 0 && TotalChanges() > 0 && !Confirm("Завершить сеанс?", "Несохранённые изменения в программе будут удалены. Настройки телефона сохранятся.", "Завершить")) return;
        await RunAsync(async () =>
        {
            if (_token.Length > 0)
            {
                await _backend!.EndAsync(_serial, _token, _lifetime.Token);
                _token = ""; _serial = ""; _original = _draft = null; _invalid.Clear(); _profile = ""; _sectionOpen = false; _settingsSection = ""; _showModeSettings = false;
                RenderEditor(); Status("Сеанс завершён. Настройки на телефоне сохранены."); return;
            }
            if (DevicePicker.SelectedItem is not DeviceInfo { IsReady: true } device) return;
            _serial = device.Serial;
            if (InstallBeforeConnect.IsChecked == true) await InstallSelectedAsync(device.Serial);
            var started = await _backend!.BeginAsync(_serial, _lifetime.Token); _token = started.Token;
            Status("Ожидание подтверждения: разрешите QRA-Config доступ к настройкам на телефоне.", "busy");
            var snapshot = await BackendService.WaitForConsentAndReadAsync(
                ct => _backend.StatusAsync(_serial, _token, ct),
                ct => _backend.ReadAsync(_serial, _token, ct),
                attempts: 90,
                pollDelay: TimeSpan.FromSeconds(2),
                ct: _lifetime.Token);
            LoadSnapshot(snapshot);
        }, "Подключаемся к телефону…");
    }
    private async Task ReadCoreAsync()
    {
        var snapshot = await _backend!.ReadAsync(_serial, _token, _lifetime.Token);
        LoadSnapshot(snapshot);
    }
    private void LoadSnapshot(ConfigSnapshot snapshot)
    {
        _original = snapshot; _draft = snapshot.DeepCopy(); _invalid.Clear();
        _sectionOpen = false; _mode = "COMMON"; _navigationMode = "COMMON"; _profile = ""; _settingsSection = ""; _showModeSettings = false;
        RenderEditor(); Status("Готово: настройки прочитаны. Выберите раздел и профиль.");
    }
    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (TotalChanges() > 0 && !Confirm("Прочитать настройки заново?", "Это заменит несохранённые изменения настройками телефона.", "Прочитать")) return;
        await RunAsync(ReadCoreAsync, "Читаем настройки телефона…");
    }
    private string[] SelectedScopes() => ScopePanel.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToArray();
    private static Dictionary<string, int> ChangesByScope(ConfigSnapshot? before, ConfigSnapshot? after)
    {
        var counts = ConfigModel.Scopes.ToDictionary(x => x, _ => 0);
        if (before == null || after == null) return counts;
        foreach (var key in before.Settings.Select(x => x.Key).Union(after.Settings.Select(x => x.Key)))
        {
            if (ConfigModel.ProtectedKeys.Contains(key) || key == "frnServers") continue;
            if (!JsonNode.DeepEquals(before.Settings[key], after.Settings[key])) counts[ConfigModel.ScopeFor(key)]++;
        }
        foreach (var mode in ConfigModel.Scopes.Where(x => x != "COMMON"))
        {
            string Rows(JsonObject settings) => new JsonArray(ConfigModel.ProfileRows(settings).Where(x => (x!["protocol"]?.GetValue<string>() ?? "FRN") == mode).OrderBy(x => ConfigModel.ServerId(x!.AsObject())).Select(x => x!.DeepClone()).ToArray()).ToJsonString();
            if (Rows(before.Settings) != Rows(after.Settings)) counts[mode]++;
        }
        return counts;
    }
    private int TotalChanges() => ChangesByScope(_original, _draft).Values.Sum();
    private void UpdateDirty()
    {
        if (!_ready) return;
        var counts = ChangesByScope(_original, _draft); var selected = SelectedScopes().ToHashSet(); int count = counts.Where(x => selected.Contains(x.Key)).Sum(x => x.Value);
        var omitted = counts.Where(x => x.Value > 0 && !selected.Contains(x.Key)).Select(x => ScopeLabel(x.Key)).ToArray();
        DirtyText.Text = _draft == null ? "Настройки ещё не прочитаны." : _invalid.Count > 0 ? "Исправьте отмеченные поля перед применением." : $"{count} изменений к применению" + (omitted.Length > 0 ? $" · не применятся: {string.Join(", ", omitted)}" : "");
        ApplyButton.Content = count > 0 ? $"Применить ({count})" : "Применить к телефону";
        ApplyButton.IsEnabled = !_busy && _token.Length > 0 && _draft != null && count > 0 && _invalid.Count == 0;
        foreach (var check in ScopePanel.Children.OfType<CheckBox>()) check.Content = ScopeLabel((string)check.Tag) + (counts[(string)check.Tag] > 0 ? $" · {counts[(string)check.Tag]}" : "");
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var scope in ScopePanel.Children.OfType<CheckBox>()) scope.IsChecked = true; }
    private void DeselectAll_Click(object sender, RoutedEventArgs e) { foreach (var scope in ScopePanel.Children.OfType<CheckBox>()) scope.IsChecked = false; }
    private void Scope_Changed(object sender, RoutedEventArgs e) { if (_ready) UpdateDirty(); }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_original == null || _draft == null || _invalid.Count > 0) return;
        var scopes = SelectedScopes();
        JsonObject merged;
        try { merged = ConfigModel.MergeScopes(_original.Config, _draft.Config, scopes); }
        catch (InvalidOperationException ex) { Status(ex.Message, "error"); return; }
        if (!Confirm("Применить настройки к телефону?", BuildPreview(_original.Settings, merged["settings"]!.AsObject()), "Применить")) return;
        await RunAsync(async () =>
        {
            await _backend!.ApplyAsync(_serial, _token, _original, _draft, scopes, _lifetime.Token);
            for (int i = 0; i < 120; i++)
            {
                var status = await _backend.StatusAsync(_serial, _token, _lifetime.Token);
                if (status.Status == "closed") { _token = ""; throw new InvalidOperationException("Сеанс завершён. Подключитесь и прочитайте настройки заново."); }
                if (status.Operation == "error") throw new InvalidOperationException(string.IsNullOrEmpty(status.Message) ? "Телефон отклонил изменения. Прочитайте настройки заново." : status.Message);
                if (status.Operation == "success") { await ReadCoreAsync(); Status("Готово: настройки сохранены на телефоне и прочитаны повторно.", "success"); return; }
                await Task.Delay(1000, _lifetime.Token);
            }
            throw new InvalidOperationException("Телефон ещё обрабатывает запрос. Прочитайте настройки перед повторной записью.");
        }, "Применяем настройки и проверяем результат…");
    }
    private void MarkEdited(string key) => UpdateDirty();
    private void MasterBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationDepth == 2) { _sectionOpen = false; _profile = ""; _settingsSection = ""; _showModeSettings = false; RenderEditor(); }
        else DevicePicker.Focus();
    }
    private void DetailBack_Click(object sender, RoutedEventArgs e) => NavigateBack();
    private void SettingsNavigationHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SettingsNavigationHost.ActualWidth <= 0) return;
        MasterColumn.Width = new GridLength(Math.Clamp(SettingsNavigationHost.ActualWidth * 0.36, 200, 280));
    }
    private static string ScopeLabel(string scope) => scope switch { "COMMON" => "Общие", "POC" => "PoC", _ => scope };
    private void Help_Click(object sender, RoutedEventArgs e) => ShowInformation("Подключение по USB",
        "1. Подключите Android USB-кабелем с передачей данных.\n\n2. В Android включите «Для разработчиков → Отладка по USB».\n\n3. Разрешите отладку с этого компьютера на телефоне.\n\n4. В QRA-Config выберите устройство и нажмите «Подключить».\n\n5. В QRA-Talk подтвердите доступ QRA-Config.\n\nНекоторым устройствам нужен USB-драйвер производителя.");
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closing && TotalChanges() > 0 && !Confirm("Закрыть QRA-Config?", "Несохранённые изменения в программе будут удалены.", "Закрыть")) { e.Cancel = true; return; }
        _closing = true; _lifetime.Cancel();
        if (_backend != null && _token.Length > 0) _ = EndBestEffortAsync(_backend, _serial, _token);
        _token = ""; _draft = _original = null; EditorPanel.Children.Clear();
    }
    private static async Task EndBestEffortAsync(BackendService backend, string serial, string token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await backend.EndAsync(serial, token, timeout.Token).ConfigureAwait(false); } catch { /* Closing cannot depend on a disconnected device; phone consent expires. */ }
    }

}
