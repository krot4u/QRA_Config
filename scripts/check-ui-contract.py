#!/usr/bin/env python3
"""Regression checks for the two defects observed on Windows, without WPF runtime."""
import logging
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
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
logging.basicConfig(level=logging.INFO, format='%(message)s')
logging.info('PASS: explicit dark window style and device label template; Windows visual acceptance remains required')
