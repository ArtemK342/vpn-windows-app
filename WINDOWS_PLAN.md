# Windows app — rework (night 2026-11-08 → 09)

User request (2026-11-08): rework the graphics (less pixelation, crisp vector graphics, own design),
then bring ALL functions to the Android level. "Don't ask me" — work autonomously.

## Decisions
- WPF .NET 8, no third-party packages. PerMonitorV2 DPI (manifest) — Windows no longer stretches the window
  bitmap on 125–200 % screens (that was the blur / «pixels»). Vector line icons (Themes/Icons.xaml, Views/Icon),
  vector flags (Views/Flag), app.ico generated from the vector brand mark (`FsocietyNet.exe -makeicon`, all sizes
  16…256), tray icons rendered per DPI from the same mark. Dark / light / system theme.
- Binaries: amneziawg.exe + wintun.dll (in git), sing-box 1.14.4 + byedpi 17.3 official zips
  (tools/fetch-binaries.ps1, sha256-pinned, not in git). Extracted at runtime to %ProgramData%\FsocietyNet\bin
  (admin-only ACL), hash-checked and kept open while the app runs (was %AppData% — user-writable, and the
  tunnel service runs it as SYSTEM).
- Protocols: AmneziaWG (key generated on the PC, X25519 + RFC 7748 self-test, like Android 0.0.3.9),
  VLESS / Trojan / Hysteria2 via sing-box TUN (configs normalised to the 1.12+ format: new DNS servers,
  no legacy block outbound, auto_detect_interface, strict_route, Clash API for traffic and the
  «server passes traffic» probe), «Режим РФ» = ciadpi.exe SOCKS5 + system proxy PAC for YouTube/Discord
  domains (a TUN would re-assemble ciadpi's packets and kill the desync). OpenVPN+Cloak: not on Windows yet.
- Kill switch: AmneziaWG full tunnel (AllowedIPs exactly 0.0.0.0/0) = the tunnel service blocks untunneled
  traffic; off → 0.0.0.0/1,128.0.0.0/1. After a drop with the kill switch on the tunnel is kept (blocked)
  until «Разблокировать». sing-box: restart from the local config, strict_route against DNS leaks.
- Split tunnelling: sing-box — domain / ip / process_name rules; AmneziaWG — sites resolved to IPs in
  AllowedIPs (zones like .ru and app rules are not possible there → notes in the UI).
- API like Android: fsociety-vpn.org/api with fallback to api.fsociety-vpn.org (direct first for /vpn/* and
  anonymous sessions), refresh on 401. No X-App-Version (android_min_version is Android's).
- Tokens: DPAPI (CryptProtectData); the old plain-text token files are deleted on start.

## Status
- [x] theme / styles / icons / flags / app icon / tray icons
- [x] Api / Session / Settings / Loc (RU+EN) / Journal
- [x] Login (email, anonymous code, anonymous account creation, registration, forgot password)
- [x] Shell (sidebar, title bar, in-window dialogs, toasts, tray, single instance, close-to-tray)
- [x] Home (power button, status, timer, speed/traffic, servers + statuses + LOGS ON + ping, favourites,
      free-plan traffic, maintenance banner, right-click menu, «For another app» dialog, connect on start)
- [x] Rules (sites / apps, exclude / include lists, presets, «Blocked in RF» programs, apply = reconnect)
- [x] News, Support (tickets, unread badge, reply, close, delete)
- [x] Settings (account, password, logout, delete, kill switch, auto-reconnect, autostart, tray, DNS, LAN,
      Hysteria2 cc, theme, language, journal, updates, about)
- [x] Build: preview renders of every screen (RU/EN, dark/light) checked; Release single-file compiles.
- [ ] Real connection test by the user (needs admin + an account): AmneziaWG, VLESS, Trojan, Hysteria2,
      «Режим РФ», kill switch, split tunnelling, autostart. sing-box configs could not be validated here
      (running the downloaded sing-box.exe was blocked in this session) — first thing to check.
- [ ] Release: GitHub release vX.Y.Z (user), then switch the site /download Windows card from «Скоро».

## How to run
- Test build: `dotnet build FsocietyNet\FsocietyNet.csproj -c Debug` → bin\Debug\…\FsocietyNet.exe (asks for admin).
- UI without admin / network: `.\tools\build.ps1 -Preview` (PNGs in out-preview\render), or run any build with `-demo`.
- Release: `.\tools\build.ps1` (needs .NET 8 Desktop Runtime) or `.\tools\build.ps1 -SelfContained`.
