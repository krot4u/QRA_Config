# QRA-Config

Нативная Windows-программа на C#/.NET 10 и WPF для настройки QRA-Talk по USB. Тёмная тема с янтарным акцентом, подсказки подключения в заголовке и меню профилей для FRN, DMR, P25 и PoC. Приложение обращается непосредственно к встроенному ADB; PowerShell, Go-лаунчер и браузер для работы интерфейса не используются.

## Подключение и настройка

1. Запустите `QRA-Config.exe`. Комплект содержит .NET runtime и ADB; устанавливать PowerShell или отдельный runtime не требуется.
2. Подключите Android USB-кабелем с передачей данных, включите «Отладку по USB» и подтвердите доступ компьютера на телефоне. Некоторым устройствам нужен USB-драйвер производителя.
3. Выберите устройство и нажмите «Подключить». QRA-Config покажет ожидание подтверждения, а после разрешения в QRA-Talk автоматически прочитает настройки; нажимать «Прочитать заново» не нужно. Разрешение действует десять минут.
4. Откройте режим, затем профиль в дополнительном меню. На его странице настройте подключение, звук и параметры режима. Выбор страницы не переключает активный профиль на телефоне.
5. Отметьте разделы для применения и нажмите «Применить». Если настройки параллельно изменились на телефоне, перечитайте их перед повторной записью.

В профиле PoC можно выбрать или убрать личное фото. Программа учитывает ориентацию изображения, обрезает его по центру и подготавливает JPEG 96×96 до 24 КБ без исходных метаданных. Фото сохраняется только после применения настроек к телефону.

Назначения физических кнопок защищены от изменения. Разрешения Android, установка кодеков и текущее состояние радио управляются на телефоне. Сетевая ADB-отладка и эмуляторы не используются; Windows USB подтверждается транспортом ADB, а не одним видом серийного номера. Пароли передаются в JSON через stdin, без аргументов команд и общей памяти телефона.

## Состояние и совместимость

Нужна QRA-Talk с USB-мостом настройки. Совместимость с текущим публичным APK перед первым выпуском требуется подтвердить отдельно; наличие review-моста в тестовой копии не подтверждает доступность функции в публичной версии.

Новая C#-версия собирается отдельно в `dist-dotnet/`. Модели и службы проходят 13 .NET-проверок, редактор — 111. Две чистые локальные сборки дали одинаковые байты. Запуск CI в GitHub, Windows-проверка исправленного интерфейса и Defender для нового EXE ещё не выполнены; USB read/apply/restore ждёт разрешения ADB на Xiaomi. Это не завершённый релиз.

Старый Go/PowerShell прототип в `dist/` не публикуется: Defender зарегистрировал у его процесса `Behavior:Win32/SuspClickFix.G2`. Положительные функциональные тесты не отменяют это обнаружение. Защита и исключения не изменяются.

.NET 10 официально поддерживает Windows 11 и указанные Microsoft версии Windows 10 LTSC. Обычная Windows 10 22H2 в UTM используется для фактической проверки совместимости, но не является официально поддерживаемой Microsoft конфигурацией .NET 10. См. [поддерживаемые ОС .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

Локальные кандидаты не подписаны. Windows может показывать предупреждение о неизвестном издателе или запрещать запуск согласно политике компьютера. Программа не отключает защиту. SignPath Foundation ещё не принял проект; публикация требует отдельного согласия владельца.

## Разработка

- `desktop/UI/` — WPF-интерфейс, редакторы и страницы профилей.
- `desktop/Models/` — конфигурация, области применения и политика фото.
- `desktop/Services/` — проверенный ADB, USB-сеансы и подготовка изображения.
- `desktop-tests/` — исполняемые тесты моделей, USB-фильтрации и целостности ADB.

```sh
dotnet run --project desktop-tests/CoreTests.csproj
dotnet run --project desktop/UI/Tests/EditorPolicyTests.csproj
bash scripts/build-dotnet.sh
```

Для SDK вне PATH: `QRA_DOTNET=/полный/путь/dotnet bash scripts/build-dotnet.sh`. Сборка создаёт самодостаточный Windows x64 EXE, SHA-256 и уведомления о сторонних компонентах. Она не публикует файлы.

## License and public preparation

Own QRA-Config sources are licensed under **GPL-3.0-only**; see the complete [LICENSE](LICENSE). Third-party components retain their upstream licenses and notices. QRA-Talk is separately licensed and its private sources are excluded. Rights to redistribute QRA branding must be confirmed by the owner before the public export.

The prepared workflow assumes this directory becomes the root of an independent repository. It runs tests and compares two clean builds with SDK 10.0.401, runtime 10.0.12 and locked dependencies; it has no signing, release, S3 or application-submission step. The current local candidate is not a public release. Run build commands from this directory so global.json is applied.

- [Code signing policy](docs/code-signing-policy.md)
- [Privacy and removal](docs/privacy.md)
- [Components and provenance](docs/component-inventory.json), [Microsoft package hashes](docs/runtime-pack-inventory.json), [third-party notices](docs/third-party-notices.md)
- [Parent project and public evidence](docs/parent-project.md)
- [Release description draft](docs/release-notes-draft.md)

For local reproducibility: `python3 scripts/check-reproducible.py --output /new/output/directory`. Set `QRA_DOTNET` for an SDK outside PATH. The output directory must not already exist.
