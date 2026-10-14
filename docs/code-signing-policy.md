# Code signing policy

QRA-Config is not enrolled in SignPath Foundation and has no Foundation-signed release. The application is being prepared for review; acceptance is not assumed.

## Scope and roles

The repository owner is the proposed maintainer, committer, reviewer and signing approver. Role assignment and MFA on both GitHub and SignPath must be confirmed before applying. External contributions require review; each production signing request requires manual approval.

The signing scope is the outer **QRA-Config.exe**, product QRA-Config, version 1.0.0 / file version 1.0.0.0, built from `desktop/` by `.github/workflows/build.yml`. This self-contained executable embeds ADB and Microsoft runtime components. The inventory explicitly identifies them for Foundation review. Neither nested ADB/DLL files nor Microsoft runtime libraries are re-signed as our own. No PowerShell script is shipped by the current WPF application; build/test scripts are source files, not requested Windows signing artifacts. Legacy Go/PowerShell executables and scripts are excluded.

## Release chain

1. Review sources, workflow and component inventory at a fixed commit; pass two clean builds and tests. Record the commit, CI run URL, artifact ID/digest and unsigned EXE hash.
2. Perform Windows launch, UI, USB read/apply/restore and Defender acceptance on that exact candidate. No antivirus exceptions or protection changes.
3. After enrollment, configure the reviewed SignPath artifact definition to sign only `QRA-Config.exe`, enforce product/version metadata and require a named approver. This is a pending integration, not implemented signing in the prepared build workflow.
4. Verify Authenticode chain, timestamp and product/version on the returned signed file. Signing intentionally changes the unsigned EXE bytes. Create a new SHA-256 manifest **after** signing, without modifying the signed EXE or any signed script.
5. Distribute those exact returned bytes via the public release/download page and optionally S3. Download anonymously and compare the signed hash before moving a latest pointer. Do not rebuild, patch resources, re-sign, compress the EXE internally or change metadata after signing. An external ZIP may contain the unchanged signed file and license notices.

Only explicit owner approval allows repository publication, release publication or application submission. The local unsigned candidate is not authorized for distribution.

If accepted, add the Foundation-required attribution to the homepage and download page: free signing provided by SignPath.io, certificate by SignPath Foundation. Until then, do not claim sponsorship or display a signing badge.

Requirements: [SignPath Foundation terms](https://signpath.org/terms.html). A valid signature identifies the publisher; it does not promise immediate removal of all SmartScreen warnings.
