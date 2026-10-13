# Privacy and removal

QRA-Config uses local USB ADB to exchange settings with QRA-Talk after the user authorizes Android USB debugging and the QRA-Talk configuration session. It does not bypass either authorization. It does not send configuration data or credentials to a QRA-Config cloud service. Network ADB devices and emulators are excluded. Configuration JSON can contain credentials; keep saved exports private. Passwords are passed to ADB through standard input, not command-line arguments.

QRA-Talk is a separate Android application with its own behavior and privacy policy. Changing its settings can affect its subsequent network behavior. QRA-Config does not initiate a radio connection or PTT operation.

Official ADB is extracted into the current user's local application data under `QRA-Config/adb`; its hashes are checked and modified cached files are restored. ADB can start its standard local server. Android stores the debugging authorization separately; the user can revoke it in Android developer settings. The upstream sources and notices are listed in `component-inventory.json` and `third-party-notices.md`.

The Windows application is portable: close it and delete its EXE and companion notices. If no QRA-Config process is running, its own `QRA-Config/adb` cache may be removed from local application data. This does not uninstall QRA-Talk or erase phone settings. Do not delete other applications' ADB files or keys.

When the user selects the public APK download channel, QRA-Config connects to the configured QRA-Talk distribution service, downloads the APK and verifies its declared size and SHA-256. Local APK installation does not require this download. Embedded help opens in the default browser; external links may connect to their respective websites. QRA-Config does not upload phone settings, passwords or photographs.
