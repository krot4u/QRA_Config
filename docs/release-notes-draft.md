# QRA-Config 1.0.0 — release draft, not published

Native Windows x64 C#/WPF companion for QRA-Talk settings over USB. Supports FRN, DMR, P25 and PoC profiles; section-limited apply preserves physical button assignments. The package includes the Microsoft .NET/WPF runtime and official Google ADB 37.0.1.

The candidate includes an explicit dark window style and a device-name template instead of the C# record dump. Native Windows visual acceptance remains pending. The prior candidate passed a Defender custom scan, but that result does not apply to the rebuilt candidate. USB read/apply/restore remains blocked by Xiaomi debugging authorization.

Before release: confirm parent APK compatibility, complete Windows/USB/Defender checks for the release hash, verify branding rights, link public sources/workflow/inventory and include GPL and third-party notices. First public release must use the same package form submitted for signing. Current candidate is unsigned; do not claim Foundation signing. [Code signing policy](code-signing-policy.md), [privacy](privacy.md), [components](component-inventory.json).

Proposed release URL: `https://github.com/krot4u/QRA_Config/releases/tag/v1.0.0` (not created). Public documentation is the README and docs from that exact release commit.
