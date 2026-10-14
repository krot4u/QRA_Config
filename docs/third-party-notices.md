# Компоненты сторонних разработчиков

QRA-Config содержит официальный Android Debug Bridge 37.0.1 (Google Android Platform Tools). Исходный архив: https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip . Исходный SHA-256: `45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99`. Встроенный минимальный архив включает ADB, две Windows библиотеки, исходный NOTICE.txt и source.properties; файлы проверяются по контрольным суммам.

Полные уведомления авторов и применимые лицензии включены в `ADB-NOTICE.txt`, а также во встроенный архив. Не удаляйте эти уведомления при распространении.

Self-contained Windows сборка включает Microsoft .NET Runtime и WindowsDesktop/WPF. Лицензия .NET: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT ; дополнительные уведомления: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT . Лицензия WPF: https://github.com/dotnet/wpf/blob/main/LICENSE.TXT .

Иконки меню основаны на Google Material Icons из https://github.com/google/material-design-icons и распространяются по Apache License 2.0. В поставку включены только SVG path 24×24, перечисленные в `docs/icon-contract.json`; имена и исходные контуры сохранены.

Собственный код QRA-Config: GPL-3.0-only, полный текст в LICENSE. Сторонние файлы не меняют своих лицензий. Точные размеры/версии/хэши: component-inventory.json и runtime-pack-inventory.json. Полный исходный NOTICE Google сохранён в docs/upstream/NOTICE.txt и включается в сборку как ADB-NOTICE.txt. NOTICE относится к полному upstream Platform Tools, включая не поставляемые нами инструменты; не следует приписывать каждую перечисленную там лицензию каждой DLL. Архив и пять включённых файлов повторно сверены с официальной загрузкой Google 13.10.2026.
