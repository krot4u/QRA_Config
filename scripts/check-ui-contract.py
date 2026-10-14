#!/usr/bin/env python3
"""Regression checks for the two defects observed on Windows, without WPF runtime."""
import logging
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
icon_contract = json.loads((root / 'docs/icon-contract.json').read_text(encoding='utf-8'))
icons = icon_contract['icons']
expected_icons = {
    'general': ('Общие', 'settings'), 'dmr': ('DMR', 'settings_input_antenna'), 'p25': ('P25', 'shield'),
    'frn': ('FRN', 'public'), 'poc': ('PoC', 'signal_cellular_alt'),
    'general.startup': ('Запуск', 'power_settings_new'), 'general.appearance': ('Оформление', 'palette'),
    'general.scan': ('Сканирование', 'radar'), 'general.screen': ('Экран', 'smartphone'),
    'general.sound': ('Звук', 'volume_up'), 'general.buttons': ('Кнопки', 'gamepad'),
    'mode.profile': ('Сервер или профиль', 'dns'), 'mode.settings': ('Параметры режима', 'tune'),
    'mode.add_profile': ('Добавить профиль', 'add'), 'import': ('Импорт настроек', 'file_download'),
    'export': ('Экспорт настроек', 'file_upload'), 'docs': ('Документация', 'menu_book'), 'about': ('О программе', 'info'),
}
by_id = {icon['id']: icon for icon in icons}
assert set(by_id) == set(expected_icons), 'Icon contract ids differ from the settings menu contract'
assert all((by_id[key]['titleRu'], by_id[key]['materialSymbol']) == value for key, value in expected_icons.items()), 'Icon names differ from the settings menu contract'
for field in ('materialSymbol', 'composeImageVector', 'svgPath'):
    assert len({icon[field] for icon in icons}) == len(icons), f'Duplicate {field} in icon contract'
editor_source = (root / 'desktop/UI/MainWindow.Editor.cs').read_text(encoding='utf-8')
for icon_id in ('general', 'dmr', 'p25', 'frn', 'poc', 'general.startup', 'general.appearance', 'general.scan', 'general.screen', 'general.sound', 'general.buttons', 'mode.profile', 'mode.settings', 'mode.add_profile'):
    assert f'"{icon_id}"' in editor_source, f'WPF menu does not reference icon id {icon_id}'
assert 'Geometry.Parse(iconDefinition.SvgPath)' in editor_source, 'WPF must draw SVG paths from the contract'
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
x = '{http://schemas.microsoft.com/winfx/2006/xaml}'
window = ET.parse(root / 'desktop/UI/MainWindow.xaml').getroot()
assert window.get('Style') == '{StaticResource {x:Type Window}}', 'Derived window needs explicit base style'
picker = next(e for e in window.findall('.//w:ComboBox', ns) if e.get(x + 'Name') == 'DevicePicker')
label = picker.find('w:ComboBox.ItemTemplate/w:DataTemplate/w:TextBlock', ns)
assert label is not None and label.get('Text') == '{Binding DisplayName}', 'Device label must not use record.ToString()'
app = ET.parse(root / 'desktop/UI/App.xaml').getroot()
style = next(e for e in app.findall('.//w:Style', ns) if e.get('TargetType') == 'Window')
assert any(e.get('Property') == 'Background' and e.get('Value') == '{StaticResource BackgroundBrush}' for e in style), 'Dark window background'
assert any(e.get('ContentTemplate') == '{TemplateBinding SelectionBoxItemTemplate}' for e in app.findall('.//w:ContentPresenter', ns)), 'Selected device template'
resources = {e.get(x + 'Key'): e.get('Color') for e in app.findall('./w:Application.Resources/w:SolidColorBrush', ns)}
def luminance(value: str) -> float:
    channels = [int(value[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in channels]
    return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2]
contrast = (max(luminance(resources['AmberBrush']), luminance(resources['OnAccentBrush'])) + .05) / (min(luminance(resources['AmberBrush']), luminance(resources['OnAccentBrush'])) + .05)
assert contrast >= 4.5, f'OnAccent contrast is only {contrast:.2f}:1'
focus_contrast = (max(luminance(resources['AmberBrush']), luminance(resources['SurfaceBrush'])) + .05) / (min(luminance(resources['AmberBrush']), luminance(resources['SurfaceBrush'])) + .05)
assert focus_contrast >= 3, f'Keyboard focus contrast is only {focus_contrast:.2f}:1'
primary = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get(x + 'Key') == 'PrimaryButton')
primary_setters = primary.findall('./w:Setter', ns)
assert any(e.get('Property') == 'Foreground' and e.get('Value') == '{StaticResource OnAccentBrush}' for e in primary_setters), 'Primary text must use OnAccent'
text_style = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get('TargetType') == 'TextBlock' and e.get(x + 'Key') is None)
assert not any(e.get('Property') == 'Foreground' for e in text_style.findall('./w:Setter', ns)), 'TextBlock must inherit control foreground'
disabled = next(e for e in primary.findall('./w:Style.Triggers/w:Trigger', ns) if e.get('Property') == 'IsEnabled' and e.get('Value') == 'False')
assert any(e.get('Property') == 'Background' and e.get('Value') == '{StaticResource FieldBrush}' for e in disabled), 'Disabled primary must not keep accent fill'
root_header = next(e for e in window.findall('.//w:TextBlock', ns) if e.get(x + 'Name') == 'MasterRootHeader')
assert root_header.get('Text') == 'Настройки' and root_header.get('Focusable') == 'False', 'Root settings header must be static and outside Tab order'
assert root_header.get('FontSize') == '18' and root_header.get('FontWeight') == 'SemiBold', 'Root header must remain prominent without extra height'
assert root_header.get('VerticalAlignment') == 'Bottom' and root_header.get('Margin') == '12,0,12,4', 'Root title must sit at the bottom of its header'
master_back = next(e for e in window.findall('.//w:Button', ns) if e.get(x + 'Name') == 'MasterBackButton')
assert master_back.get('Visibility') == 'Collapsed', 'Back arrow must be hidden at the root level'
for column_name in ('MasterNavigationColumn', 'DetailNavigationColumn'):
    column = next(e for e in window.findall('.//w:Grid', ns) if e.get(x + 'Name') == column_name)
    rows = column.findall('./w:Grid.RowDefinitions/w:RowDefinition', ns)
    assert rows[0].get('Height') == '40', f'{column_name} header row must stay at 40 DIP'
    assert column.get('Background') == 'Transparent', f'{column_name} must share the navigation background'
navigation = next(e for e in window.findall('.//w:Grid', ns) if e.get(x + 'Name') == 'NavigationPanels')
assert navigation.get('Background') == '{StaticResource BackgroundBrush}', 'Navigation headers and lists need one shared background'
divider = next(e for e in window.findall('.//w:Border', ns) if e.get(x + 'Name') == 'NavigationDivider')
assert divider.get('VerticalAlignment') == 'Stretch', 'Column divider must cross the header area'
navigation_header = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get(x + 'Key') == 'NavigationHeaderButton')
header_setters = {e.get('Property'): e.get('Value') for e in navigation_header.findall('./w:Setter', ns)}
assert header_setters.get('Height') == '40' and header_setters.get('Padding') == '12,0,12,4', 'Nested headers must match the compact root header'
assert header_setters.get('VerticalContentAlignment') == 'Bottom', 'Nested titles must sit at the bottom of the header'
assert window.get('PreviewKeyDown') == 'Window_PreviewKeyDown', 'Keyboard input modality must be tracked'
for event_name in ('PreviewMouseDown', 'PreviewTouchDown', 'PreviewStylusDown'):
    assert window.get(event_name) == 'Window_PreviewPointerDown', f'{event_name} must clear keyboard-only focus mode'
for control_type in ('Button', 'ComboBox', 'TextBox', 'PasswordBox', 'CheckBox'):
    control_style = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get('TargetType') == control_type and e.get(x + 'Key') is None)
    setters = control_style.findall('./w:Setter', ns)
    assert any(e.get('Property') == 'FocusVisualStyle' and e.get('Value') == '{x:Null}' for e in setters), f'{control_type} must not draw a second system focus ring'
assert not any(condition.get('Property') for condition in app.findall('.//w:MultiDataTrigger.Conditions/w:Condition', ns)), 'MultiDataTrigger conditions must use Binding, not Property'
menu_scroll_style = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get(x + 'Key') == 'HiddenMenuScrollViewer')
menu_scroll_setters = {e.get('Property'): e.get('Value') for e in menu_scroll_style.findall('./w:Setter', ns)}
assert menu_scroll_setters.get('VerticalScrollBarVisibility') == 'Hidden', 'Menu scrollbars must stay hidden'
menu_tile = next(e for e in app.findall('./w:Application.Resources/w:Style', ns) if e.get(x + 'Key') == 'SettingsMenuTile')
tile_setters = {e.get('Property'): e.get('Value') for e in menu_tile.findall('./w:Setter', ns)}
assert tile_setters.get('MinHeight') == '52', 'Menu tiles must use the compact 52 DIP minimum'
for scroll_name in ('MasterMenuScroll', 'DetailMenuScroll', 'EditorScroll'):
    scroll = next(e for e in window.findall('.//w:ScrollViewer', ns) if e.get(x + 'Name') == scroll_name)
    assert scroll.get('Style') == '{StaticResource HiddenMenuScrollViewer}', f'{scroll_name} must use hidden menu scrolling'
    assert scroll.get('ScrollChanged') == 'MenuScroll_ScrollChanged', f'{scroll_name} must update edge fades while scrolling'
    assert scroll.get('GotKeyboardFocus') == 'MenuScroll_GotKeyboardFocus', f'{scroll_name} must keep keyboard focus visible'
for fade_name in ('MasterTopFade', 'MasterBottomFade', 'DetailTopFade', 'DetailBottomFade'):
    fade = next(e for e in window.findall('.//w:Border', ns) if e.get(x + 'Name') == fade_name)
    assert fade.get('Height') == '24' and fade.get('IsHitTestVisible') == 'False', f'{fade_name} must be a non-blocking 24 DIP fade'
apply_button = next(e for e in window.findall('.//w:Button', ns) if e.get(x + 'Name') == 'ApplyButton')
assert apply_button.get('IsEnabled') == 'False', 'Apply starts disabled until there are changes'
logging.basicConfig(level=logging.INFO, format='%(message)s')
logging.info(f'PASS: UI and icon contracts ({len(icons)} unique icons), hidden menu scrolling, one keyboard-only focus ring, focus contrast {focus_contrast:.2f}:1, OnAccent contrast {contrast:.2f}:1; Windows visual acceptance remains required')
