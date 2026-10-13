using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QraConfig.Models;
using QraConfig.Services;

namespace QraConfig.Desktop;

public partial class MainWindow
{
    private void InstallOption_Changed(object sender, RoutedEventArgs e) { if (_ready) UpdateActions(); }
    private void ApkSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApkFileText.Text = ApkSource.SelectedIndex == 2
            ? _localApk is null ? "Выберите локальный APK QRA-Talk." : Path.GetFileName(_localApk)
            : "APK скачивается с сервера QRA; размер и SHA-256 проверяются перед установкой.";
        UpdateActions();
    }
    private void BrowseApk_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Выберите APK QRA-Talk", Filter = "Android APK (*.apk)|*.apk", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) { _localApk = dialog.FileName; ApkFileText.Text = Path.GetFileName(_localApk); UpdateActions(); }
    }
    private async Task InstallSelectedAsync(string serial)
    {
        if (_token.Length > 0) throw new InvalidOperationException("Завершите сеанс настройки перед установкой APK.");
        if (_review && ApkSource.SelectedIndex != 2) throw new InvalidOperationException("Тестовый режим устанавливает только локальный review APK; основной QRA-Talk сохраняется.");
        string path;
        if (ApkSource.SelectedIndex == 2)
        {
            path = _localApk ?? throw new InvalidOperationException("Сначала выберите локальный APK.");
            Status("Проверяем локальный APK…", "busy");
            await ApkInstallService.ValidateLocalAsync(path, _lifetime.Token);
        }
        else
        {
            path = await new ApkInstallService().DownloadAsync(ApkSource.SelectedIndex == 1,
                new Progress<string>(message => Status(message, "busy")), _lifetime.Token);
        }
        Status("Устанавливаем QRA-Talk с сохранением данных. Подтвердите установку на телефоне, если он попросит.", "busy");
        try { await _backend!.InstallAsync(serial, path, _lifetime.Token); }
        finally { if (ApkSource.SelectedIndex != 2) { try { File.Delete(path); } catch (IOException) { } } }
        Status("QRA-Talk установлен. Данные приложения сохранены.", "success");
    }
    private async void InstallApk_Click(object sender, RoutedEventArgs e)
    {
        if (DevicePicker.SelectedItem is not DeviceInfo { IsReady: true } device) return;
        await RunAsync(() => InstallSelectedAsync(device.Serial), "Подготавливаем установку QRA-Talk…");
    }
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("QraConfig.Guide.html")
                ?? throw new InvalidOperationException("Справка не найдена в сборке.");
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QRA-Config", "guide");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "qra-talk.html");
            using (var target = File.Create(path)) resource.CopyTo(target);
            Process.Start(new ProcessStartInfo(new Uri(path).AbsoluteUri + "#qra-config") { UseShellExecute = true });
        }
        catch (Exception) { Status("Не удалось открыть справку в браузере. Проверьте браузер по умолчанию.", "error"); }
    }
}
