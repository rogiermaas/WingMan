# Code signing policy and status

WingMan 1.1.6 is currently distributed without Windows Authenticode signatures. Repository owner: [rogiermaas](https://github.com/rogiermaas). No SignPath Foundation approval or certificate has been obtained. Do not treat the cryptographically verified auto-update manifest as a Windows code-signing certificate.

## Free route under consideration

[SignPath Foundation](https://signpath.org/) offers signing for accepted open-source projects. The [application](https://signpath.org/apply.html) and [terms](https://signpath.org/terms.html) describe admission; approval is discretionary and executable projects need verifiable reputation. Publishing a new repository alone does not guarantee acceptance.

Application references:

- Source: https://github.com/rogiermaas/WingMan
- Builds: https://github.com/rogiermaas/WingMan/actions
- Releases: https://github.com/rogiermaas/WingMan/releases
- Website: https://wingman.rogiermaas.nl/
- License: GNU GPL v3

The owner must supply contact details, establish MFA on the repository/signing accounts and confirm maintainer, reviewer and human release-approver responsibilities. Application and production-signing approval must be performed by the project owner, not represented as an AI agent's personal attestation.

Before production signing, resolve the privacy-policy and user-control requirements and disclose the proprietary Microsoft SimConnect runtime. It is downloaded separately when missing and is not WingMan's code. Ask SignPath whether this dependency is acceptable; do not request that Microsoft's DLL be signed as WingMan code. The .NET runtime and WiX installer components also need to be covered correctly in the signing configuration.

Current networking behavior: after a pilot name is configured, the relay receives simulated position data for discovery even when Followable is off. Update checks and missing-runtime downloads are separate network functions. The installer currently has no network-data opt-out. A completed operator privacy notice and any required controls must describe the real implementation before eligibility is claimed. The project does not currently claim to satisfy every Foundation requirement.

After approval, build from reviewed repository source, submit WingMan.exe to the signing service, package the returned signed EXE in the MSI, sign the MSI, and only then generate release hashes and the auto-update archive/manifest. Human approval remains required for Foundation signing requests. Code-signing and update private keys must never be committed or included in releases. Until this process is configured and verified, releases remain unsigned.

## What signing does and does not promise

Microsoft recommends trusted code signatures for [Smart App Control compatibility](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control). Its checks can also apply to dependencies, so signing WingMan.exe alone does not prove every runtime will be accepted.

[SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) is a separate concern: even a valid OV/EV signature can initially show an unfamiliar-app warning. EV certificates no longer grant an automatic reputation bypass. Do not promise that obtaining a certificate immediately eliminates every warning.

For a paid alternative, [Microsoft Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) supports public trust for organizations in the EU, but currently limits individual-developer eligibility to the US and Canada. A Netherlands-based individual should not subscribe assuming eligibility; a qualifying registered organization may be an option. Account ownership, identity validation and any paid subscription are the owner's decisions.
