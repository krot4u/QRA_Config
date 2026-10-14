# QRA-Config — next release notes draft

Native Windows x64 C#/WPF companion for QRA-Talk settings over USB. Supports FRN, DMR, P25 and PoC profiles; section-limited apply preserves physical button assignments. The package includes the Microsoft .NET/WPF runtime and official Google ADB 37.0.1.

The current development candidate adds the compact master-detail settings menu, automatic reading after USB consent and clearer disabled actions. Each rebuilt candidate requires its own Windows UI, USB and Defender acceptance.

Before release: confirm parent APK compatibility, complete Windows/USB/Defender checks for the release hash, verify branding rights, link public sources/workflow/inventory and include GPL and third-party notices. First public release must use the same package form submitted for signing. Current candidate is unsigned; do not claim Foundation signing. [Code signing policy](code-signing-policy.md), [privacy](privacy.md), [components](component-inventory.json).

The existing public release is `https://github.com/krot4u/QRA_Config/releases/tag/v1.0.0`. These notes describe a later candidate and must be finalized against the exact release commit before publication.
