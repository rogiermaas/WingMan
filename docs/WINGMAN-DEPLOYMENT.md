# WingMan deployment

Prepared for AlmaLinux 8.10 x86_64 and Apache at https://wingman.rogiermaas.nl/.
The relay listens only on **127.0.0.1:8787**. Clients use outbound HTTPS/WebSocket over existing **443**. Do not open 8787 in the firewall.

## Files

- `dist/WingMan.msi`: per-user Windows installer, self-contained .NET runtime, Start menu shortcut.
- `dist/wingman-server/wingman-relay`: Linux amd64 binary built with CGO disabled, no glibc dependency.
- `dist/wingman-server/web/`: download page and MSI.
- `dist/wingman-server/releases/`: signed update ZIP and manifest.
- `deploy/wingman.service` and `deploy/apache-wingman.inc.conf`: reviewed configuration examples, not automatically installed.

## Install on AlmaLinux

Copy `dist/wingman-server/` to a temporary directory on the server. As root, create a dedicated system account and the destination directories, then copy the supplied files:

```sh
useradd --system --home-dir /opt/wingman --shell /sbin/nologin wingman
install -d -m 0755 /opt/wingman /opt/wingman/releases /var/www/wingman
install -m 0755 wingman-relay /opt/wingman/wingman-relay
cp releases/* /opt/wingman/releases/
cp web/* /var/www/wingman/
install -m 0644 wingman.service /etc/systemd/system/wingman.service
systemctl daemon-reload
systemctl enable --now wingman
curl --fail http://127.0.0.1:8787/healthz
```

Skip `useradd` if the account already exists. Keep binaries and releases owned by root; the relay only needs read access.

Include the supplied Apache fragment **inside your existing HTTPS virtual host for this domain**. It does not create certificates or replace your other virtual hosts. Verify `proxy_module`, `proxy_http_module` and `proxy_wstunnel_module` with `httpd -M`. Keep any existing HTTP-to-HTTPS redirect.

On an enforcing SELinux host, allow Apache's reverse-proxy connection and label the nonstandard release directory for HTTP reads:

```sh
setsebool -P httpd_can_network_connect 1
semanage fcontext -a -t httpd_sys_content_t '/opt/wingman/releases(/.*)?'
restorecon -Rv /opt/wingman/releases /var/www/wingman
apachectl configtest
systemctl reload httpd
curl --fail https://wingman.rogiermaas.nl/healthz
```

The SELinux boolean permits Apache outbound connections; it does not open an inbound firewall port. `semanage` is provided by `policycoreutils-python-utils` if absent. If the path already has a custom label rule, adjust the existing rule rather than adding a duplicate.

Test with two Windows clients: enter names, let both clients connect automatically, enable Followable and verify live positions within 100 NM. Then test following with the intended aircraft. Linux execution, Apache/TLS routing and real two-pilot flight behaviour require validation on the actual server/simulators; loopback tests do not establish internet latency.

## Publish an update

Run `build-wingman.ps1 -Version 1.1.1` on the Windows development PC. It rebuilds the app/MSI and signs the update using the existing publisher key. Upload the **versioned ZIP first**, then atomically replace `latest.json` in `/opt/wingman/releases/`. Replace `/var/www/wingman/WingMan.msi` last. Do not change the embedded public key between releases.

The private key is stored outside this workspace at `%LOCALAPPDATA%\WingManPublisher\update-private.pem`. Back it up privately. Never put it on the web server. The relay cannot create trusted updates without it. The build intentionally refuses to overwrite a versioned ZIP; use a new version for changed builds.

Public source downloads are included as `web/WingMan-source.zip`. Upload the matching source archive whenever publishing a binary. To build from source without the owner's signing key, use `build-wingman.ps1 -SkipPublish` after installing .NET 10 SDK, Go, Python and WiX 4.0.6 plus `WixToolset.UI.wixext/4.0.6`. The app is licensed under GNU GPL version 3; keep the complete license and third-party notices with downloads. The MSI defaults to `%LOCALAPPDATA%\Programs\WingMan`, displays the GPL agreement, and offers a folder chooser. Choose a user-writable folder to retain updates without administrator prompts.

The app polls `/v1/update` every five minutes and also supports manual checking. The update helper downloads on a separate connection while following continues, verifies the publisher signature, size, SHA-256 and executable version, then requests a normal close. The current target/settings are saved **at close time**. It replaces the executable and checks startup; a startup failure restores the previous executable. Automatic following resumes only after an update, for the same aircraft/room/lead, with fresh telemetry and the normal autopilot checks. A normal launch or network reconnection does not automatically engage following. Updates replace the application EXE; the MSI remains the original installation/uninstall registration. A later MSI release performs a Windows Installer major upgrade.

## Operational limits

- Public discovery supports 128 online pilots. Offline identities expire after one hour; the total identity cache is bounded. A relay restart clears the roster; clients reconnect automatically, but following requires switching Follow user on again.
- No formation code is needed. Reusing a pilot identity requires its saved random secret. Display names are not Xbox authentication. Followable pilots share positions with nearby pilots and their followers. Unfollowable clients still send observer positions for proximity filtering; these positions are not forwarded.
- Own telemetry is sent at up to 10 Hz. Selected followers receive up to 10 Hz; nearby discovery is limited to 1 Hz within 100 NM. Guidance runs at up to 5 Hz, list refresh at 1 Hz. Displayed RTT measures the client-to-relay round trip, not total lead-to-follower delay.
- Pause, disconnection, stale data, large position jumps and follow loops are handled explicitly. Stopping following leaves the aircraft's last autopilot selections in place.
- The current follower holds a geometric slot behind the lead's current course. A 5 NM train is supported by chaining leads; it does not record and replay the exact route around every waypoint.
- Version 1.1.1 downloads the native SimConnect runtime only when a compatible installed copy is absent. Serve `web/runtime/msfs2024-1.7.3/` unchanged; the client pins the DLL SHA-256. Runtime redistribution proceeds on the project owner's reported confirmation from Asobo. The Microsoft SDK license accompanies the DLL on the server. Stage these files before publishing an application manifest. `tools/prepare-runtime.ps1 -SdkRoot <SDK folder>` prepares and verifies them.
- Publisher update signatures are separate from Windows Authenticode. No code-signing certificate was provided, so this MSI/EXE is not Authenticode-signed.

Apache reference: https://httpd.apache.org/docs/2.4/mod/mod_proxy_wstunnel.html
SDK gamertag limitation: https://devsupport.flightsimulator.com/t/is-there-a-way-to-obtain-xbox-gamertag-via-simconnect-wasm/11860
