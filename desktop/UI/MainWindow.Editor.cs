using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QraConfig.Models;
using QraConfig.Services;

namespace QraConfig.Desktop;

public partial class MainWindow
{
    private sealed record Option(string Label, string Value)
    {
        public override string ToString() => Label;
    }
    private sealed record NavigationItem(string Id, string Title, string Detail, string IconId, Action Open);
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["userEmail"]="Логин / e-mail",["userPassword"]="Пароль",["userCallsign"]="Позывной",["userDisplayName"]="Имя",["userCity"]="Город",["userLocation"]="Местоположение",["userNation"]="Страна",["userDescription"]="Описание",["userNetwork"]="Комната / Talkgroup",
        ["appTheme"]="Тема приложения",["accentColor"]="Акцентный цвет",["uiScalePercent"]="Масштаб интерфейса, %",["scanHoldSeconds"]="Удержание сканирования, с",["rogerBeepRx"]="Сигнал после приёма",["rogerBeepTx"]="Сигнал после передачи",["rogerBeepRxTone"]="Звук после приёма",["rogerBeepTxTone"]="Звук после передачи",
        ["wakeScreenOnPtt"]="Вкл. экран при PTT",["carPttSide"]="Расположение PTT",["keepScreenOn"]="Не выключать экран",["automaticMicLevel"]="Автоуровень микрофона",["automaticRxLevel"]="Автоуровень приёма",["audioOutputDevice"]="Устройство вывода звука",["audioInputDevice"]="Устройство ввода звука",["showScreenPtt"]="Экранная кнопка PTT",["menuKeyHoldMs"]="Удержание кнопки меню, мс",
        ["micGain"]="Усиление микрофона",["bassDb"]="Низкие частоты передачи, дБ",["trebleDb"]="Высокие частоты передачи, дБ",["txBassDb"]="Низкие частоты передачи, дБ",["txTrebleDb"]="Высокие частоты передачи, дБ",["essid"]="ESSID",["slot"]="Таймслот",["colorCode"]="Color Code",["module"]="Модуль",["privateCall"]="Индивидуальный вызов",["rxAutomaticGain"]="Автоуровень приёма",["rxGainDb"]="Усиление приёма, дБ",["micSource"]="Источник микрофона",["qraMicFilter"]="Фильтр микрофона QRA"
    };
    private static string Label(string key) => Labels.GetValueOrDefault(key, "Дополнительный параметр");
    private IReadOnlyList<ServerProfile> Profiles() => _draft == null ? Array.Empty<ServerProfile>() : EditorPolicy.Profiles(_draft.Settings, _mode);
    private ServerProfile? CurrentProfile() => Profiles().FirstOrDefault(x => x.Id == _profile);
    private static TextBlock Text(string value, double size = 14, bool muted = false) => new() { Text = value, FontSize = size, Foreground = muted ? Muted : Ink, TextWrapping = TextWrapping.Wrap };
    private Button ActionButton(string title, Action action, bool primary = false)
    {
        var button = new Button { Content = title, Margin = new Thickness(0,0,8,0), Style = (Style)FindResource(primary ? "PrimaryButton" : typeof(Button)) };
        AutomationProperties.SetName(button, title); button.Click += (_, _) => action(); return button;
    }
    private StackPanel Card(string title, string? subtitle = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0,0,0,subtitle == null ? 12 : 4) });
        if (subtitle != null) { var detail = Text(subtitle, 12, true); detail.Margin = new Thickness(0,0,0,12); panel.Children.Add(detail); }
        EditorPanel.Children.Add(new Border { Background = Surface, BorderBrush = BorderColor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0,0,0,10), Child = panel });
        return panel;
    }
    private string _settingsSection = "";
    private string _navigationMode = "";
    private bool _showModeSettings;
    private bool _sectionOpen;
    private int _renderedNavigationDepth;

    private static string SettingsSection(string key) => key switch
    {
        "autoStartAndConnect" => "Запуск",
        "appTheme" or "accentColor" or "uiScalePercent" => "Оформление",
        "scanHoldSeconds" => "Сканирование",
        "keepScreenOn" or "wakeScreenOnPtt" => "Экран",
        "audioInputDevice" or "audioOutputDevice" or "automaticMicLevel" or "automaticRxLevel" or "micGain" or "bassDb" or "trebleDb" or "rogerBeepRx" or "rogerBeepTx" or "rogerBeepRxTone" or "rogerBeepTxTone" => "Звук",
        "showScreenPtt" or "menuKeyHoldMs" or "carPttSide" => "Кнопки",
        _ => "Запуск"
    };
    private void OpenSettingsPage(Action navigate)
    {
        if (_invalid.Count > 0) { Status("Исправьте отмеченные поля перед переходом.", "error"); return; }
        navigate(); RenderEditor(); EditorScroll.ScrollToTop();
    }
    private void AddNavigationTile(StackPanel panel, NavigationItem item, bool selected, bool enabled = true)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var disabledInk = (Brush)FindResource("DisabledInkBrush");
        var disabledMuted = (Brush)FindResource("DisabledMutedBrush");
        var iconDefinition = SettingsIconContract.Get(item.IconId);
        var icon = new Viewbox
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(iconDefinition.SvgPath),
                Fill = enabled ? Amber : disabledMuted,
                Stretch = Stretch.Uniform,
            },
        };
        content.Children.Add(icon);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(item.Title, 14); title.Foreground = enabled ? Ink : disabledInk; title.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal; title.MaxHeight = 40; title.TextTrimming = TextTrimming.WordEllipsis;
        labels.Children.Add(title);
        if (item.Detail.Length > 0)
        {
            var hint = Text(item.Detail, 12, true); hint.Foreground = enabled ? Muted : disabledMuted; hint.Margin = new Thickness(0,3,0,0); hint.TextWrapping = TextWrapping.NoWrap; hint.TextTrimming = TextTrimming.WordEllipsis;
            labels.Children.Add(hint);
        }
        Grid.SetColumn(labels, 1); content.Children.Add(labels);
        var arrow = Text("›", 22); arrow.Foreground = enabled ? Amber : disabledMuted; arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow,2); content.Children.Add(arrow);
        var button = new Button { Content = content, Style = (Style)FindResource("SettingsMenuTile"), Tag = selected ? "selected" : null, IsEnabled = enabled };
        AutomationProperties.SetName(button, selected ? item.Title + ", выбран" : item.Title);
        AutomationProperties.SetHelpText(button, item.Detail);
        AutomationProperties.SetItemStatus(button, selected ? "Выбран раздел" : "");
        button.Click += (_, _) => OpenSettingsPage(item.Open);
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up)
            {
                var buttons = panel.Children.OfType<Button>().Where(x => x.IsEnabled).ToList();
                int index = buttons.IndexOf(button); int step = e.Key == Key.Down ? 1 : -1;
                if (buttons.Count > 0) buttons[(index + step + buttons.Count) % buttons.Count].Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true;
            }
            else if (e.Key == Key.Left && NavigationDepth > 0)
            {
                NavigateBack(); e.Handled = true;
            }
        };
        panel.Children.Add(button);
    }
    private int NavigationDepth => !_sectionOpen ? 0 : _profile.Length > 0 || _settingsSection.Length > 0 || _showModeSettings ? 2 : 1;
    private string ModeDetail(string mode)
    {
        if (_draft == null) return "";
        var profiles = EditorPolicy.Profiles(_draft.Settings, mode);
        var selectedId = _draft.Settings["selectedServer:" + mode]?.GetValue<string>();
        return profiles.FirstOrDefault(x => x.Id == selectedId)?.Name ?? profiles.FirstOrDefault()?.Name ?? "";
    }
    private IReadOnlyList<NavigationItem> RootNavigationItems() =>
    [
        new("COMMON", "Общие", "6 подразделов", "general", () => OpenRootSection("COMMON")),
        new("DMR", "DMR", ModeDetail("DMR"), "dmr", () => OpenRootSection("DMR")),
        new("P25", "P25", ModeDetail("P25"), "p25", () => OpenRootSection("P25")),
        new("FRN", "FRN", ModeDetail("FRN"), "frn", () => OpenRootSection("FRN")),
        new("POC", "PoC", ModeDetail("POC"), "poc", () => OpenRootSection("POC")),
    ];
    private IReadOnlyList<NavigationItem> GeneralNavigationItems() =>
    [
        new("Запуск", "Запуск", "Автозапуск и подключение", "general.startup", () => _settingsSection = "Запуск"),
        new("Оформление", "Оформление", "Тема, акцентный цвет и масштаб", "general.appearance", () => _settingsSection = "Оформление"),
        new("Сканирование", "Сканирование", "Пауза после приёма", "general.scan", () => _settingsSection = "Сканирование"),
        new("Экран", "Экран", "Отключение и пробуждение экрана", "general.screen", () => _settingsSection = "Экран"),
        new("Звук", "Звук", "Устройства, уровни и сигналы", "general.sound", () => _settingsSection = "Звук"),
        new("Кнопки", "Кнопки", "Экранная PTT и поведение меню", "general.buttons", () => _settingsSection = "Кнопки"),
    ];
    private IReadOnlyList<NavigationItem> ModeNavigationItems()
    {
        var result = Profiles().Select(profile =>
        {
            var selectedProfile = profile;
            return new NavigationItem(profile.Id, profile.Name, profile.Address + ":" + profile.Port + (profile.BuiltIn ? " · встроенный сервер" : ""), "mode.profile", () => _profile = selectedProfile.Id);
        }).ToList();
        result.Add(new NavigationItem("mode-settings", "Параметры режима", "Профиль по умолчанию и настройки режима", "mode.settings", () => _showModeSettings = true));
        if (_mode != "P25") result.Add(new NavigationItem("add-profile", "Добавить профиль", "Новый сервер " + ScopeLabel(_mode), "mode.add_profile", () => EditProfile(null)));
        return result;
    }
    private void OpenRootSection(string mode)
    {
        _sectionOpen = true; _mode = mode; _navigationMode = mode; _profile = ""; _settingsSection = ""; _showModeSettings = false;
    }
    private void NavigateBack()
    {
        if (NavigationDepth == 2) { _profile = ""; _settingsSection = ""; _showModeSettings = false; }
        else if (NavigationDepth == 1) { _sectionOpen = false; _profile = ""; _settingsSection = ""; _showModeSettings = false; }
        else { DevicePicker.Focus(); return; }
        RenderEditor();
    }
    private void ConfigureHeader(Button button, TextBlock label, string title, bool visible = true)
    {
        label.Text = title; button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(button, "Назад. " + title);
    }
    private void RenderNavigationMenu(StackPanel panel, IEnumerable<NavigationItem> items, string selectedId, bool enabled = true)
    {
        panel.Children.Clear();
        foreach (var item in items) AddNavigationTile(panel, item, item.Id == selectedId, enabled);
    }
    private void ShowDetail(UIElement element)
    {
        EmptyDetailHint.Visibility = element == EmptyDetailHint ? Visibility.Visible : Visibility.Collapsed;
        DetailMenuScroll.Visibility = element == DetailMenuScroll ? Visibility.Visible : Visibility.Collapsed;
        EditorScroll.Visibility = element == EditorScroll ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(UpdateMenuFades);
    }
    private void AnimateNavigation(int previousDepth, int depth)
    {
        NavigationTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        NavigationTranslate.X = 0;
        if (previousDepth == depth || !SystemParameters.ClientAreaAnimation) return;
        double distance = Math.Max(200, MasterColumn.ActualWidth);
        var animation = new DoubleAnimation(depth > previousDepth ? distance : -distance, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        NavigationTranslate.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private void RenderEditor()
    {
        if (!_ready) return;
        _building = true;
        try
        {
            if (_navigationMode != _mode) { _navigationMode = _mode; _settingsSection = ""; _showModeSettings = false; }
            int previousDepth = _renderedNavigationDepth;
            EditorPanel.Children.Clear(); DetailMenuPanel.Children.Clear();
            if (_profile.Length > 0 && CurrentProfile() == null) _profile = "";
            int depth = NavigationDepth;
            if (depth < 2)
            {
                MasterRootHeader.Visibility = Visibility.Visible;
                ConfigureHeader(MasterBackButton, MasterHeaderText, "", false);
                RenderNavigationMenu(MasterMenuPanel, RootNavigationItems(), depth == 0 ? "" : _mode, _draft != null);
            }
            else
            {
                MasterRootHeader.Visibility = Visibility.Collapsed;
                ConfigureHeader(MasterBackButton, MasterHeaderText, ScopeLabel(_mode));
                RenderNavigationMenu(MasterMenuPanel, _mode == "COMMON" ? GeneralNavigationItems() : ModeNavigationItems(), _mode == "COMMON" ? _settingsSection : _showModeSettings ? "mode-settings" : _profile);
            }
            if (_draft == null)
            {
                ConfigureHeader(DetailBackButton, DetailHeaderText, "", false);
                EmptyDetailHint.Text = "Подключите устройство, чтобы прочитать настройки"; ShowDetail(EmptyDetailHint);
            }
            else if (depth == 0)
            {
                ConfigureHeader(DetailBackButton, DetailHeaderText, "", false);
                EmptyDetailHint.Text = "Выберите раздел"; ShowDetail(EmptyDetailHint);
            }
            else if (depth == 1)
            {
                ConfigureHeader(DetailBackButton, DetailHeaderText, ScopeLabel(_mode));
                RenderNavigationMenu(DetailMenuPanel, _mode == "COMMON" ? GeneralNavigationItems() : ModeNavigationItems(), "");
                ShowDetail(DetailMenuScroll);
            }
            else
            {
                string title = CurrentProfile()?.Name ?? (_settingsSection.Length > 0 ? _settingsSection : "Параметры режима");
                ConfigureHeader(DetailBackButton, DetailHeaderText, title);
                ShowDetail(EditorScroll);
                if (CurrentProfile() is { } selected) RenderProfile(selected);
                else RenderMode();
            }
            _renderedNavigationDepth = depth;
            AnimateNavigation(previousDepth, depth);
        }
        finally { _building = false; UpdateDirty(); }
    }
    private void RenderMode()
    {
        var items = _draft!.Settings.ToArray().Where(x => IsVisibleSetting(x.Key) && ConfigModel.ScopeFor(x.Key) == _mode).ToList();
        if (_mode == "COMMON" && !_draft.Settings.ContainsKey("autoStartAndConnect")) items.Add(new KeyValuePair<string, JsonNode?>("autoStartAndConnect", JsonValue.Create(false)));
        if (_mode == "COMMON" && !_draft.Settings.ContainsKey("accentColor")) items.Add(new KeyValuePair<string, JsonNode?>("accentColor", JsonValue.Create("__default")));
        var panel = Card(_mode == "COMMON" ? _settingsSection : "Параметры " + ScopeLabel(_mode));
        var visibleItems = items.Where(item => _mode != "COMMON" || SettingsSection(item.Key) == _settingsSection).ToList();
        foreach (var item in visibleItems)
            AddSetting(panel, item.Key, item.Value, value => SetSetting(item.Key, value));
        if (visibleItems.Count == 0) panel.Children.Add(Text("В этом разделе пока нет переносимых настроек.", 13, true));
    }
    private void RenderProfile(ServerProfile profile)
    {
        var title = Card(profile.Name, $"{profile.Address}:{profile.Port}" + (profile.BuiltIn ? " · встроенный сервер" : ""));
        title.Children.Add(Text("Открытый профиль — это выбор для редактирования. Подключение на телефоне не меняется.", 12, true));
        if (!profile.BuiltIn)
        {
            var actions = new WrapPanel { Margin = new Thickness(0,14,0,0) };
            actions.Children.Add(ActionButton("Параметры сервера", () => EditProfile(profile)));
            actions.Children.Add(ActionButton("Удалить профиль", () => DeleteProfile(profile))); title.Children.Add(actions);
        }
        var connection = Card("Подключение", "Учётные данные относятся только к этому профилю.");
        if (profile.Id == "frn.qra-team.online:11024")
        {
            foreach (var item in _draft!.Settings.ToArray().Where(x => x.Key.StartsWith("user", StringComparison.Ordinal)))
                AddSetting(connection, item.Key, item.Value, value => SetSetting(item.Key, value));
        }
        else
        {
            foreach (var field in new[] { "userEmail", "userPassword", "userNetwork" }.Concat(profile.Protocol == "DMR" ? new[] { "userCallsign" } : Array.Empty<string>()))
            {
                string key = $"server:{profile.Id}:{field}";
                var value = _draft!.Settings[key] ?? JsonValue.Create(field == "userNetwork" ? EditorPolicy.RoomDefault(profile.Protocol) : "");
                AddSetting(connection, key, value, edited => SetSetting(key, edited));
            }
        }
        AddNested(Card("Звук профиля"), "profileAudio:" + profile.Id, EditorPolicy.AudioDefault);
        if (profile.Protocol == "DMR") AddNested(Card("Параметры DMR"), "dmrOptions:" + profile.Id, EditorPolicy.DmrDefault);
        if (profile.Protocol == "POC") AddAvatar(Card("Личное фото", "Фото сохраняется в профиле только после применения настроек."), profile);
        var display = Card("Отображение профиля");
        AddSetting(display, "serverIcon:" + profile.Id, _draft!.Settings["serverIcon:" + profile.Id] ?? JsonValue.Create(profile.Icon), value => SetSetting("serverIcon:" + profile.Id, value));
        // Any future portable profile options remain editable rather than being silently hidden.
        foreach (var item in _draft.Settings.ToArray().Where(x => EditorPolicy.Owns(x.Key, profile.Id) && x.Key.StartsWith("server:" + profile.Id + ":") && !new[] {"userEmail","userPassword","userNetwork","userCallsign"}.Contains(x.Key[(x.Key.LastIndexOf(':')+1)..])))
            AddSetting(connection, item.Key, item.Value, value => SetSetting(item.Key, value));
    }
    private void AddNested(StackPanel panel, string key, string defaults)
    {
        JsonObject nested;
        try { nested = JsonNode.Parse(_draft!.Settings[key]?.GetValue<string>() ?? defaults)!.AsObject(); }
        catch { panel.Children.Add(Text("Не удалось прочитать параметры. Прочитайте настройки заново.", 13, true)); return; }
        foreach (var field in nested.ToArray())
            AddSetting(panel, key, field.Value, edited =>
            {
                nested[field.Key] = edited?.DeepClone(); SetSetting(key, JsonValue.Create(nested.ToJsonString()));
            }, field.Key);
    }
    private void SetSetting(string key, JsonNode? value)
    {
        if (_building || _draft == null) return;
        if (key == "accentColor" && value?.GetValue<string>() == "__default") _draft.Settings.Remove(key);
        else _draft.Settings[key] = value?.DeepClone();
        MarkEdited(key);
    }
    private void AddSetting(StackPanel panel, string key, JsonNode? value, Action<JsonNode?> save, string? nestedField = null)
    {
        string leaf = nestedField ?? (key.StartsWith("server:") ? key[(key.LastIndexOf(':')+1)..] : key);
        string caption = leaf.StartsWith("selectedServer:") ? "Профиль по умолчанию на телефоне" : leaf.StartsWith("serverIcon:") ? "Значок профиля" : Label(leaf);
        if (leaf == "userEmail" && _mode is "DMR" or "P25") caption = _mode + " ID";
        if (leaf == "userNetwork" && _mode is "DMR" or "P25") caption = "Talkgroup";
        string validationId = key + "/" + leaf;
        FrameworkElement control;
        string hint = ""; TextBlock? validationMessage = null;
        if (value is JsonValue boolean && boolean.TryGetValue<bool>(out var flag))
        {
            var check = new CheckBox { Content = caption, IsChecked = flag, Margin = new Thickness(0,4,0,4) };
            check.Checked += (_, _) => { if (!_building) save(JsonValue.Create(true)); };
            check.Unchecked += (_, _) => { if (!_building) save(JsonValue.Create(false)); }; control = check;
        }
        else if (Choices(leaf, value) is { } options)
        {
            string current = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value?.ToJsonString() ?? "";
            if (!options.Any(x => x.Value == current)) options.Add(new(leaf is "audioInputDevice" or "audioOutputDevice" ? "Сохранённое устройство (не подключено)" : "Сохранённое значение", current));
            var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", SelectedItem = options.FirstOrDefault(x => x.Value == current) };
            combo.SelectionChanged += (_, _) =>
            {
                if (_building || combo.SelectedItem is not Option option) return;
                save(leaf == "micSource" ? JsonValue.Create(int.Parse(option.Value, CultureInfo.InvariantCulture)) : JsonValue.Create(option.Value));
            };
            control = combo;
        }
        else if (value?.GetValueKind() == JsonValueKind.Number)
        {
            var range = NumberRange(leaf); bool integer = leaf is "uiScalePercent" or "scanHoldSeconds" or "menuKeyHoldMs" or "essid" or "slot" or "colorCode";
            validationMessage = Text("", 11, true); validationMessage.Foreground = ErrorColor; validationMessage.Visibility = Visibility.Collapsed; validationMessage.Margin = new Thickness(0,5,0,0);
            var number = new TextBox { Text = double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.InvariantCulture) };
            hint = range == null ? "Числовое значение" : $"{range.Value.Min}–{range.Value.Max}" + (integer ? " · целое число" : "");
            number.TextChanged += (_, _) =>
            {
                if (_building) return;
                if (!double.TryParse(number.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || integer && parsed != Math.Truncate(parsed) || range is { } limits && (parsed < limits.Min || parsed > limits.Max))
                {
                    _invalid.Add(validationId); validationMessage.Text = "Проверьте число и диапазон " + hint; validationMessage.Visibility = Visibility.Visible; AutomationProperties.SetHelpText(number,validationMessage.Text); number.BorderBrush = ErrorColor; number.ToolTip = "Проверьте диапазон: " + hint; UpdateDirty(); return;
                }
                _invalid.Remove(validationId); validationMessage.Visibility = Visibility.Collapsed; AutomationProperties.SetHelpText(number,hint); number.ClearValue(Control.BorderBrushProperty); number.ToolTip = hint; save(integer ? JsonValue.Create((int)parsed) : JsonValue.Create(parsed));
            }; control = number;
        }
        else if (leaf.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            var password = new PasswordBox { Password = value?.GetValue<string>() ?? "" };
            password.PasswordChanged += (_, _) => { if (!_building) save(JsonValue.Create(password.Password)); };
            control = password;
        }
        else
        {
            var text = new TextBox { Text = value?.GetValue<string>() ?? "" };
            text.TextChanged += (_, _) => { if (!_building) save(JsonValue.Create(text.Text)); }; control = text;
        }
        AutomationProperties.SetName(control, caption);
        var row = new Grid { Margin = new Thickness(0,0,0,10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        if (control is CheckBox) { Grid.SetColumnSpan(control,2); row.Children.Add(control); }
        else
        {
            var label = Text(caption); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0,0,18,0);
            row.Children.Add(label); AutomationProperties.SetLabeledBy(control,label);
            var field = new StackPanel(); field.Children.Add(control);
            if (validationMessage != null) field.Children.Add(validationMessage);
            if (hint.Length > 0) { var explanation = Text(hint,11,true); explanation.Margin = new Thickness(0,5,0,0); field.Children.Add(explanation); }
            Grid.SetColumn(field,1); row.Children.Add(field);
        }
        panel.Children.Add(row);
    }
    private List<Option>? Choices(string key, JsonNode? value)
    {
        static List<Option> Pairs(params string[] pairs) => pairs.Select(x => { var split=x.Split('|',2); return new Option(split.Length>1?split[1]:split[0],split[0]); }).ToList();
        if (key == "carPttSide") return Pairs("LEFT|Слева","RIGHT|Справа");
        if (key == "appTheme") return Pairs("Dark|Тёмная","Light|Светлая","System|Системная","Monochrome|Монохромная");
        if (key == "accentColor") return Pairs("__default|По умолчанию","Green|Зелёный","Blue|Синий","Purple|Фиолетовый","Lime|Лайм","Amber|Янтарный","Red|Красный","Teal|Бирюзовый","White|Белый");
        if (key is "rogerBeepRxTone" or "rogerBeepTxTone") return Pairs("Soft|Мягкий","Short|Короткий","Digital|Цифровой","Classic|Классика","Wooden|Стук","Marimba|Маримба","ClickEvent|Щелчок","ClickFive|Щелчок 5");
        if (key == "module") return Enumerable.Range('A',26).Select(x => new Option(((char)x).ToString(),((char)x).ToString())).ToList();
        if (key == "micSource") return Pairs("1|Обычный микрофон","6|Режим голосовой связи","7|Режим распознавания речи");
        if (key.StartsWith("serverIcon:")) return Pairs("radio|Радио","cloud|Облако","globe|Глобус","satellite|Спутник","router|Маршрутизатор","hub|Узел","headset|Гарнитура","flag|Флаг","wifi|Wi-Fi","dns|Сервер","bolt|Молния","star|Звезда");
        if (key.StartsWith("selectedServer:")) return Profiles().Select(x => new Option(x.Name,x.Id)).ToList();
        if (key is "audioInputDevice" or "audioOutputDevice")
        {
            string listKey = key == "audioInputDevice" ? "audioInputDevices" : "audioOutputDevices";
            var items = _draft!.Metadata[listKey] as JsonArray;
            return items?.Select(x => new Option(x!["label"]!.GetValue<string>(),x["value"]!.GetValue<string>())).ToList() ?? Pairs("auto|Автоматически","phone|Телефон");
        }
        return null;
    }
    private static (double Min,double Max)? NumberRange(string key) => key switch
    {
        "uiScalePercent" => (75,150), "scanHoldSeconds" => (1,30), "menuKeyHoldMs" => (100,1000), "essid" => (0,99), "slot" => (1,2), "colorCode" => (0,15), "rxGainDb" => (-24,24), "bassDb" or "trebleDb" or "txBassDb" or "txTrebleDb" => (-12,12), "micGain" => (0,2), _ => null
    };
    private void AddAvatar(StackPanel panel, ServerProfile profile)
    {
        string key="pocAvatar:"+profile.Id; var row=new WrapPanel(); var image=new Image { Width=96,Height=96,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,20,0) };
        if (_draft!.Settings[key]?.GetValue<string>() is {Length:>0} encoded)
        {
            try { using var stream=new System.IO.MemoryStream(Convert.FromHexString(encoded)); var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); image.Source=bitmap; } catch { Status("Не удалось показать фото. Прочитайте настройки заново.","error"); }
        }
        AutomationProperties.SetName(image,"Фото профиля "+profile.Name); row.Children.Add(new Border {Background=(Brush)FindResource("FieldBrush"),CornerRadius=new CornerRadius(8),Child=image});
        var actions=new StackPanel();
        actions.Children.Add(ActionButton("Выбрать фото…",()=> {
            if (_invalid.Count>0) { Status("Сначала исправьте отмеченные поля.","error"); return; }
            var picker=new OpenFileDialog {Title="Фото профиля PoC",Filter="Изображения|*.jpg;*.jpeg;*.png;*.bmp",Multiselect=false};
            if(picker.ShowDialog(this)!=true)return;
            try { SetSetting(key,JsonValue.Create(AvatarService.EncodePhoto(picker.FileName))); RenderEditor(); Status("Фото подготовлено. Нажмите «Применить» для сохранения."); }
            catch(InvalidOperationException ex){Status(ex.Message,"error");}catch{Status("Не удалось прочитать фото.","error");}
        }));
        var remove=ActionButton("Убрать фото",()=>{_draft.Settings.Remove(key);MarkEdited(key);RenderEditor();}); remove.Margin=new Thickness(0,8,8,0);remove.IsEnabled=image.Source!=null;actions.Children.Add(remove);
        var hint=Text("JPEG 96×96 · до 24 КБ\nОбрезка по центру, без исходных метаданных.",11,true); hint.Margin=new Thickness(0,10,0,0); actions.Children.Add(hint);row.Children.Add(actions);panel.Children.Add(row);
    }
}
