<img src="docs/icone.png" alt="" width="72" align="right">

# CyberArkTerm

[Français](README.md) · **English** · [Italiano](README.it.md)

[![build](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml/badge.svg)](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/muller-camille/CyberArkTerm)](https://github.com/muller-camille/CyberArkTerm/releases/latest)

**Multi-session Windows client for CyberArk.** CyberArkTerm signs in to your PVWA, lists the accounts you
can access and opens your sessions with a double-click: remote desktop through **PSM**, or an SSH terminal
through **PSM for SSH (PSMP)** with a built-in **file browser** to upload files to the server.

![SSH session through the PSMP, with the Files tab following the terminal folder](docs/captures/en/main-window.png)

> Screenshots come from a demo environment (fictitious data).

## Contents

- [Features](#features)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Shortcuts](#shortcuts)
- [Settings and configuration file](#settings-and-configuration-file)
- [Security](#security)
- [How it works](#how-it-works)
- [Troubleshooting](#troubleshooting)
- [Development](#development)
- [Limitations and ideas](#limitations-and-ideas)
- [License](#license)

## Features

| | |
| --- | --- |
| **CyberArk sign-in** | CyberArk, LDAP, RADIUS (challenge / OTP included) or Windows (current session) authentication. |
| **Available** | Every account visible in the vault, grouped by safe, platform or target type, with instant search. |
| **My servers** | Your working servers, organized in folders and subfolders, each with its own settings. |
| **PSM sessions** | Remote desktop through the PSM (like the PVWA "Connect" button), in an application tab: component, target machine, reason, ticket. |
| **SSH sessions (PSMP)** | Built-in terminal in a tab (xterm compatible: colors, vim, less, top…), MFA authentication. |
| **Files tab** | SFTP browser of the server: `ls`, navigation, `rm`, drag-and-drop upload over SCP, editing in your text editor, permissions (`chmod`), follows the terminal folder. |
| **Emergency access (KeePass)** | Without CyberArk: KeePass vaults (.kdbx) in "My servers", direct SSH and remote desktop connections, creating and editing entries, local log. |
| **PVWA session kept open** | A light request every 4 minutes avoids the timeout while you work (paused while Windows is locked). |
| **Home** | Quick connect (type a server, press Enter), recent sessions. |
| **Export** | Account list as CSV, opens directly in Excel (separator follows the Windows region). |
| **Languages** | Interface in English, French and Italian: Windows language by default, can be changed at any time. |

## Installation

### Download the executable

1. Open the [latest release](https://github.com/muller-camille/CyberArkTerm/releases/latest) in the
   repository's *Releases*.
2. Download **`CyberArkTerm-<version>-win-x64.zip`** and unzip it (the SHA256 hash is in
   `SHA256SUMS.txt`).
3. Run `CyberArkTerm.exe`: a single file, no runtime to install, no administrator rights needed.

Development builds: the executable of every build is also available as the `CyberArkTerm-win-x64`
artifact in the [Actions](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml) tab.

The executable is not signed: on first launch, Windows SmartScreen may show a warning
("More info" → "Run anyway").

### Requirements

**Workstation**

- Windows 10 or 11 (x64).
- The Windows Remote Desktop client (installed by default) for PSM sessions: its built-in control for tabs,
  or `mstsc`.
- Optional: Windows Terminal and the Windows "OpenSSH Client", only if you choose to open SSH outside
  CyberArkTerm.

**CyberArk side**

- PVWA **v10 or later** (REST API `/PasswordVault/API/...`), reachable over **HTTPS** with a certificate
  trusted by the workstation.
- **List accounts** permission on the relevant safes: the application only shows what the API lets you see.
- PSM configured on the platforms you use (`PSM-RDP`, `PSM-SSH`… components).
- For SSH: a **PSM for SSH (PSMP)**, with SFTP allowed for the Files tab (and SCP for SCP uploads).
- Optional: **MFA caching** enabled on the PVWA, so you do not type your password and MFA again at the PSMP.

## Getting started

### 1. Sign in to the vault

<img src="docs/captures/en/sign-in.png" alt="Sign-in window" width="440">

Enter the PVWA address (`pvwa.mydomain.local` is enough: `https://` and `/PasswordVault` are added), choose
the authentication method, then your user name and password. If the RADIUS server asks a question (OTP
code), the window shows it and waits for your answer.

The address, method and user name are remembered; **the password never is**.

The list at the bottom left changes the interface language (Français, English, Italiano); the window reopens
right away in the chosen language, keeping the address and user name you typed.

### 2. Find an account: "Available" tab

- The search box filters on every field (server, user, safe, platform, domain…), several words allowed
  (`prd sql`).
- "Group by" sorts accounts by safe, platform or target type.
- The "All accounts" tab shows the same list as a sortable table, exportable to CSV.
- On the Home tab, **quick connect** finds a server as you type: press Enter to connect.

### 3. Open a PSM session (remote desktop)

Double-click the account (or press Enter, or the "Connect" button). CyberArkTerm requests the connection from
the PVWA and opens Remote Desktop on the PSM, exactly like the PVWA "Connect" button.

The session opens **in a CyberArkTerm tab**, with the Windows Remote Desktop control (the same engine as
`mstsc`):

- the remote desktop resolution follows the tab size;
- "Full screen" shows the session on the whole screen (use the connection bar at the top to come back, or
  `Ctrl+Alt+Break`);
- "Disconnect" ends the session and keeps the tab; "Reconnect" asks the PVWA for a new connection (the token
  of a PSM session works only once);
- closing the tab (cross or middle click) disconnects the session, after confirmation.

A PSM component that opens a **remote application** (RemoteApp) also shows **in the tab**: CyberArkTerm opens the
same connection as a desktop that starts the program published by the PSM (`||PSMInitSession`), with the same PSM
session request. The PSM server must accept this mode. If it closes the session, the tab offers "Open in separate
windows": a new request to the PVWA, and the application opens as a remote application, its windows on their own,
on this computer's desktop as with `mstsc`; the tab then shows its state ("Disconnect" closes it, "Reconnect"
starts it again). To do so every time, untick "Show PSM remote applications in the tab" in the Settings.

The session opens in **Remote Desktop Connection** (`mstsc`) if the option is unticked in the Settings or if the
Remote Desktop control can't be used on this computer; the status bar then says why.

- **PSM component**: deduced from the platform (`PSM-RDP` for Windows, `PSM-SSH` for Unix and network,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Tick "Remember this component" to keep it for the whole
  platform.
- **Domain accounts**: the window asks for the target machine, prefilled with the account's allowed machines.
- **Reason and ticket**: if the PVWA refuses the request (reason required, component not configured…), its
  message is shown and you can fix it and try again.
- The "Advanced…" button (or right-click → "Advanced connection…") opens this window on demand.

### 4. Open an SSH session through the PSMP

Set the PSMP address once in **Settings**. Then right-click → "Connect over SSH" (or the "SSH" button). With
the option "Double-click on a Unix account: connect over SSH through the PSMP", a double-click is enough.

The session opens **in a CyberArkTerm tab**, with the standard PSMP login
`<you>@<target account>[#domain]@<target server>`. User names containing spaces (`John Smith`,
`Local Admin`) are accepted.

<img src="docs/captures/en/psmp-authentication.png" alt="Authentication question asked by the PSMP" width="640">

- **Authentication**: if the PVWA provides an "MFA caching" key, no question is asked. Otherwise the PSMP
  questions (password, MFA code) are shown; the password is reused for the SFTP and SCP connections of the
  same tab, never saved.
- **PSMP key**: on first connection, its SHA256 fingerprint is shown and must be accepted; if it changes
  later, a warning is shown.
- **Terminal**: selecting copies, right-click pastes, the mouse wheel scrolls back, AltGr works on
  international keyboards. Close the tab with its cross or a middle click.

### 5. Browse and upload files: "Files" tab

When an SSH session opens, the **Files** tab appears on the side and follows the active SSH tab.

- **Path bar**: current path, editable (type a path, then Enter). Double-click a folder to enter it, `..` to
  go up, "parent folder" and "home folder" buttons.
- **Upload files**: drag them from Explorer onto the list (or the "Upload" button). Sent over **SCP** by
  default (SFTP as an option), folders included; confirmation before overwriting an existing file.
- **Delete**: select, then Del (or right-click → "Delete (rm)"), with confirmation. Folders must be empty.
- **Edit a file**: select it, then `F4` (or right-click → "Edit", or the pencil button). The file opens in the
  text editor chosen in Settings (Notepad by default). Every time you save, CyberArkTerm offers to send it back
  to the server: sent over SFTP, the file's permissions are kept. If the file changed on the server since you
  opened it, a warning asks before overwriting it.
- **Permissions**: right-click → "Permissions…" (or the padlock button). Read / write / execute boxes for owner,
  group and others, special bits (setuid, setgid, sticky) and the octal value (`644`, `1777`…), for one or
  several items. For a folder, "Apply to the folder contents too" propagates the permissions to subfolders and
  files; by default, execute (x) is only given to folders and to files that are already executable. Symbolic
  links are not followed and the owner is not changed.
- Also: new folder, download, copy path, show hidden files.
- **Follow the terminal folder**: when ticked, every `cd` in the terminal moves the browser to the same
  folder (see [How it works](#how-it-works)). After `sudo -i` or `su`, tick the box again at the shell prompt to
  re-enable tracking in that new shell.

### 6. Organize your servers: "My servers" tab

![My servers organized in folders](docs/captures/en/my-servers.png)

- **Add** an account: right-click in "Available" → "Add to my servers" then the folder you want, or drag the
  account onto the "My servers" tab, or the "Add" toolbar button.
- **Folders**: right-click → new folder or subfolder, rename, delete; drag servers and folders to move them.
- **Settings of each server** (right-click → "Properties…"):

| Setting | Effect |
| --- | --- |
| Name, folder | Display and position in the tree. |
| PSM or SSH via PSMP | Connection type opened on double-click. |
| PSM component | Component to use (empty: deduced from the platform). |
| Target machine | Server to open the session on, for a domain account. |
| Default reason | Access reason sent automatically to the PVWA. |
| SFTP start folder | The terminal **and** the file browser open directly in this folder. |

A server whose account is no longer visible in CyberArk is greyed out.

### 7. Emergency access outside CyberArk: KeePass vaults

When CyberArk is unavailable, CyberArkTerm opens your KeePass vaults (`.kdbx`) and connects **directly** to the
servers, over SSH or remote desktop, with the accounts they hold.

> These connections **do not go through the PSM**: no recording, no CyberArk rules. Every vault opening,
> connection and change is written to the local log `%APPDATA%\CyberArkTerm\urgence.log`.

![Emergency access: KeePass vault unlocked in "My servers"](docs/captures/en/keepass-vault.png)

- **Without CyberArk**: on the sign-in screen, "Emergency access (KeePass)" opens the main window without the
  PVWA (only the KeePass vaults are shown). With CyberArk, the vaults also appear at the top of "My servers".
- **Add a vault**: vault button of the "My servers" tab (or right-click → "Add a KeePass vault…"): `.kdbx`
  file, name, optional key file.
- **Unlock**: double-click the vault. Master password and/or key file (every KeePass format). "Remember the
  master password in the local vault" saves typing it again (see below).
- **Connect**: double-click an entry. The protocol comes from its address (`ssh://server:22`, `rdp://server`,
  `server:3389`), a "Protocol" / "Port" field or an `ssh` / `rdp` tag; otherwise CyberArkTerm asks SSH or
  remote desktop. The entry's password is used directly (terminal + Files tabs over SSH, remote desktop tab over
  RDP); it is never shown or written to disk.
- **Edit the vault**: right-click → "New entry…", "Edit…" (`F2`), "Delete" (`Del`, into the vault's recycle
  bin). The rest of the vault (attachments, fields, settings) is kept; the previous version of an entry goes to
  its history, like in KeePass.
- **Lock**: right-click → "Lock". Vaults also lock on sign-out, on exit and when **Windows is locked**.

**Local vault**: the master passwords you choose to remember are kept in
`%APPDATA%\CyberArkTerm\coffre-local.dat`, encrypted with a password of your own (asked when CyberArkTerm starts,
"Later" to skip it) and tied to your Windows account. Manage it in the **Settings**: create, unlock, change the
password, delete.

## Shortcuts

| Where | Action | Shortcut |
| --- | --- | --- |
| Everywhere | Reload the accounts from the PVWA | `F5` |
| Everywhere | Filter the accounts | `Ctrl+F` |
| Lists and trees | Open the session | Double-click or `Enter` |
| Search | Clear the filter | `Esc` |
| My servers | Rename / remove or delete | `F2` / `Del` |
| Terminal | Copy | Mouse selection, or `Ctrl+Shift+C` |
| Terminal | Paste | Right-click, `Shift+Insert` or `Ctrl+Shift+V` |
| Terminal | Scrollback | Mouse wheel, `Shift+Page Up` / `Shift+Page Down` |
| SSH or remote desktop tab | Close | Tab cross or middle click |
| Remote desktop | Full screen / back | `Ctrl+Alt+Break` |
| Files | Open / edit / parent folder / delete / refresh | `Enter` / `F4` / `Backspace` / `Del` / `F5` |
| KeePass vault | Connect / edit / delete an entry | Double-click or `Enter` / `F2` / `Del` |

## Settings and configuration file

![Settings](docs/captures/en/settings.png)

| Setting | Purpose | Default |
| --- | --- | --- |
| Interface language | Français, English, Italiano or system language; applied after signing out or at the next start | Windows language (English if it is not translated) |
| PSMP address and port | PSM for SSH server; empty = SSH disabled | empty, 22 |
| Double-click on Unix = SSH | Opens Unix accounts over SSH rather than PSM | no |
| Keep the PVWA session open | Light request every 4 minutes; paused while Windows is locked | yes |
| Local vault | Remembered KeePass master passwords: create, unlock, change password, delete | — |
| Remote desktop in CyberArkTerm | PSM sessions in a tab; otherwise Remote Desktop Connection (`mstsc`) | yes |
| PSM remote applications in the tab | PSM RemoteApp components opened as a desktop in the tab (the PSM must accept it); otherwise separate windows | yes |
| Debug log | Settings button menu: how connections unfold, in a file, without secrets (see [Security](#security)); "Show the debug log file" opens it in Explorer | no |
| SSH in CyberArkTerm | Built-in terminal and Files tab; otherwise Windows Terminal | yes |
| Follow the terminal folder | Allows setting up folder tracking in the shell | yes |
| File upload | SCP or SFTP | SCP |
| Text editor | Program opened by "Edit" in the Files tab | Notepad |
| Accepted PSMP keys | Remembered fingerprints ("Forget keys" button) | — |
| Remembered components | PSM component chosen per platform ("Forget" button) | — |

All preferences are saved in `%APPDATA%\CyberArkTerm\settings.json`: language, PVWA address, sign-in method
and user name, the settings above, "My servers" and their folders, recent sessions, location of the KeePass
vaults and of their key files. This file contains **no password, token or private key**. To start from scratch, close the application and delete it.

## Security

- **HTTPS required** to the PVWA; certificate validation is never disabled.
- **No secret on disk**: CyberArk password, session token, MFA key and PSMP password stay in memory for the
  session. The PVWA session is closed (`Logoff`) on exit.
- PVWA session opened with `concurrentSession`: your PVWA web session, if any, is not closed.
- **Remote desktop sessions in a tab**: the PVWA response (one-time PSM token) stays in memory, nothing is
  written to disk. Redirections (drives, printers, ports, smart cards) are only turned on if the PVWA asks for
  them; the clipboard follows its request (on if it says nothing).
- **RDP files for `mstsc`** (one-time PSM token) written to `%TEMP%\CyberArkTerm` and deleted after 60 s or on
  exit.
- **PSMP host keys pinned** on first use, with a warning if they change (the same for servers reached in
  emergency access).
- **PVWA session keep-alive**: it avoids the idle timeout; nothing is sent while Windows is locked, and the option
  can be turned off in the Settings if your policy requires it.
- **KeePass vaults**:
  - the master password is never saved, except in the local vault if you ask for it: Argon2id (64 MiB,
    3 passes) then AES-256-GCM, key derivation settings authenticated, all protected by DPAPI (Windows account);
  - in memory, the vault key and the entry passwords stay masked and are only revealed when connecting; vaults
    lock on sign-out, on exit and when Windows is locked;
  - safe saving: the file is read again, the change is applied to its current version (changes made elsewhere
    are kept), the decrypted result is checked, a `.bak` copy is kept and the file is replaced in one step; an
    entry changed elsewhere in the meantime is not overwritten;
  - direct remote desktop: the password is only passed to the Remote Desktop control (no file, no credential
    manager), with network level authentication (NLA) and a warning if the server is not recognized;
  - `urgence.log`: date, Windows account, computer, action, vault, entry, target; never a password.
- **Debug log**, off by default (Settings button menu): `%LOCALAPPDATA%\CyberArkTerm\debug.log`, 5 MB at most
  plus one `.1` generation. It records how PVWA, PSM, remote desktop and SSH connections unfold: request addresses
  and statuses, .rdp file settings, Remote Desktop control events and codes, errors. It contains server and
  account names, but **never** a password, session token, PSM session request (`PSM@…` masked), signature,
  request header or body, nor session content. The status bar shows it while it is on. Read it before passing it
  on, and delete it once the problem is solved.
- **Edited files**: the local copy opened in the editor is stored in `%TEMP%\CyberArkTerm\edit` and deleted when
  the SSH tab closes; a warning shows if changes were not sent back.
- **No command injection**: SCP paths and start folders are quoted for the remote shell; `ssh` / Windows
  Terminal arguments are validated and passed without a shell.
- CSV export protected against Excel formula injection.
- PSM and PSMP sessions opened by CyberArkTerm are standard CyberArk sessions: they are recorded and audited
  by the PSM like the ones opened from the PVWA.

To report a vulnerability, see [SECURITY.md](SECURITY.md) (private reporting, no public issue).

## How it works

### PVWA API calls

| Call | Purpose |
| --- | --- |
| `POST /PasswordVault/API/auth/{CyberArk\|LDAP\|RADIUS\|Windows}/Logon` | Sign in |
| `GET /PasswordVault/API/Accounts?offset=…&limit=1000` | Paged account list |
| `POST /PasswordVault/API/Accounts/{id}/PSMConnect` | RDP file of the PSM session |
| `POST /PasswordVault/API/Users/Secret/SSHKeys/Cache` | Temporary "MFA caching" SSH key (if enabled) |
| `GET /PasswordVault/API/Accounts?offset=0&limit=1` | Session keep-alive (every 4 minutes) |
| `POST /PasswordVault/API/Auth/Logoff` | Sign out |

### KeePass vaults

Native reading and writing (no KeePass installed) of the **KDBX 3.1 and 4.x** formats: AES-256 or ChaCha20
encryption, AES-KDF (processor AES instructions) or Argon2d / Argon2id key derivation, XML 1.0 / 2.0 key files,
32 bytes, 64 hexadecimal characters or any file. The rewritten file keeps the original version, encryption and
key derivation, with new seeds on every save. The test vaults (`tests/CyberArkTerm.Core.Tests/KeePass/Vaults`)
come from KeePassXC and pykeepass, and files written by CyberArkTerm were checked in both tools.

### Remote desktop sessions

Remote desktop tabs host the Windows ActiveX control (`mstscax.dll`, the most recent `MsRdpClient` class
available). CyberArkTerm reads the RDP file returned by `PSMConnect` and applies its settings: `full address`,
`username`, `alternate shell` (start of the PSM session), server authentication level, NLA (CredSSP), gateway,
redirections, sound, visual effects. Session ends and connection errors are explained in the tab with the
Windows message.

A PSM component that opens a remote application is opened as a desktop by default: RemoteApp mode off, and the
session starts `remoteapplicationprogram` (for PSM, `||PSMInitSession`, followed by `remoteapplicationcmdline` if
any), with the same user (`PSM@…`). A server in RemoteApp mode usually only accepts its published programs when a
session starts: a PSM closed the session that started `alternate shell` (`PSM@…`) directly (version 0.4.1). The
file's signature (`signature`) is checked only by `mstsc`, not by the control nor by the server. If the session
ends within its first minute, the tab offers "Open in separate windows", which requests the PVWA again and opens
the file as it is.

Otherwise (option unticked, file without `alternate shell`, or separate windows requested), for a remote application
(`remoteapplicationmode:i:1`), the control switches to RemoteApp mode
(`disableremoteappcapscheck` applied), then starts the application once the session is open, once per
connection: `remoteapplicationprogram` (for PSM, `||PSMInitSession`) with the `remoteapplicationcmdline`
arguments; `remoteapplicationname` is used for display and `alternate shell` is not used. If the server refuses
the application, the session ends with the reason. The remote desktop takes the size of all screens so the
windows can go anywhere. An integration test (`rdp-integration` workflow) opens real sessions on the CI machine:
a desktop in a tab, Notepad as a remote application, a remote application file opened as a desktop (the CI
machine, without the Session Host role, does not run the start program: only its transfer is checked) then in
separate windows, and an unknown application (error message). Session end messages give the Windows codes
(reason, extended reason).

### PSMP sessions

Each SSH tab opens up to three connections to the PSMP, with the same login
`<you>@<account>[#domain]@<target>`: the terminal, the SFTP connection of the Files tab, and an SCP
connection on the first SCP upload. Each one is a PSMP session, recorded by the PSM.

### Following the terminal folder

When an SSH session opens (if the option is on), CyberArkTerm waits for the target server's shell to show its
prompt (up to 60 s: the PSMP sometimes takes several seconds to reach the target), then sends it a one-line
command, preceded by a space so it stays out of the history. Nothing is sent if you already started typing;
the command can be sent again without duplicate effect (the "Follow" box):

- sets `PROMPT_COMMAND` (bash) or `precmd` (zsh) to emit the standard **OSC 7** sequence with the current
  folder at each prompt;
- if a start folder is configured, a `cd` to that folder;
- erases the typed command so it does not stay on screen.

The built-in terminal decodes the OSC 7 sequence and the Files tab moves to that folder.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| "TLS connection refused: this computer does not trust the PVWA certificate" | The certificate (or its issuing authority) is not in the workstation's Windows store. |
| "The PVWA must be reached over HTTPS" | Type the address without `http://` (or with `https://`). |
| "Your CyberArk session has expired" | PVWA inactivity timeout reached: sign in again. |
| "Connection component … is not configured for platform …" | Choose the right component in "Advanced connection", tick "Remember" for the platform. |
| "You must specify a reason…" | Enter a reason in the window that opens (or a default reason in the server's properties). |
| The account does not show up | You lack the "List accounts" permission on its safe, or the list needs reloading (`F5`). |
| The PSMP password is asked for each tab | MFA caching is not enabled on the PVWA: expected behavior (once per tab). |
| The Files tab shows "SFTP connection failed" | SFTP is not allowed on the PSMP or for this account: ask your CyberArk team. |
| The browser does not follow `cd` | The remote shell is not bash or zsh, the option is off in Settings, or the prompt was not recognized: tick "Follow the terminal folder" again at the shell prompt. |
| "The key of the PSMP has changed" warning | Only continue if your CyberArk team confirms a server change. |
| "Wrong master password or key file." | Check the password and the key file; a vault protected by a YubiKey is not supported. |
| The KeePass vault asks for the password despite "Remember" | Local vault locked ("Later" at start-up) or master password changed elsewhere: type it, it is remembered again. |
| "The local vault file is damaged or was created by another Windows account." | The local vault does not follow a change of computer or account: delete it in the Settings and create it again. |
| "The entry … was changed or deleted in the vault in the meantime" | Someone changed the same entry elsewhere: the vault is reloaded, make the change again. |
| The PSM session opens in `mstsc`, not in a tab | Remote Desktop control unavailable or failing, or option unticked: the status bar gives the reason. |
| PSM session of a remote application ends at once ("An internal error has occurred"…) | The PSM refuses the remote application opened as a desktop: "Open in separate windows" in the tab, or untick "Show PSM remote applications in the tab". |
| Understanding a connection failure | Settings → Debug log, reproduce the problem, then Settings → "Show the debug log file". |
| Remote application (RemoteApp): "not allowed on the server" | The requested application is not published on the PSM server: check with the CyberArk administrator. |
| The tab shows "Remote Desktop control error" | Untick "Open remote desktop sessions in a CyberArkTerm tab" in the Settings to use `mstsc`, and report the code shown. |

## Development

### Structure

| Project | Role |
| --- | --- |
| `src/CyberArkTerm.Core` | Cross-platform logic without UI: PVWA API client, account classification, xterm terminal emulator, PSMP connections and SFTP/SCP browser (SSH.NET), "My servers" folders, KeePass vaults (KDBX), local vault, preferences. |
| `src/CyberArkTerm.App` | WPF application: windows, tabs, terminal control, Remote Desktop control (RDP tabs), `mstsc` launch, icon (`Assets`). |
| `tests/CyberArkTerm.Core.Tests` | xUnit tests of Core (fake PVWA over HTTP, terminal, PSMP, folders, translations…). |
| `tests/CyberArkTerm.App.Tests` | Windows tests of the application (real Remote Desktop control, DPAPI). |

External dependency: [SSH.NET](https://github.com/sshnet/SSH.NET) (MIT license).

### Translations

Interface texts live in `src/CyberArkTerm.Core/Localization/CoreStrings*.resx` and
`src/CyberArkTerm.App/Localization/Strings*.resx`: English in the neutral file, then `.fr` and `.it`.
The `*.Designer.cs` classes are generated by Visual Studio (`PublicResXFileCodeGenerator`); a test checks that
every language has all the keys, the same `{0}` parameters and the same `_` access keys.
To add a language: copy the `.resx` files with the new code (`.de.resx`…), translate them, then add the code
to `UiLanguage.Supported`.

### Build and test

With the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet test CyberArkTerm.sln
dotnet run --project src/CyberArkTerm.App
```

The project also builds on Linux or macOS (`EnableWindowsTargeting`); the application only runs on Windows.
The tests in `tests/CyberArkTerm.App.Tests` (including a test of the real Remote Desktop control) only run on
Windows; elsewhere, run `dotnet test tests/CyberArkTerm.Core.Tests`.

### Publish the executable

```powershell
# Self-contained (~65 MB): nothing to install on the workstation
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

# Lightweight: requires the ".NET Desktop Runtime 10" on the workstation
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
```

CI ([`.github/workflows/build.yml`](.github/workflows/build.yml)) runs the tests and publishes the
self-contained executable as the `CyberArkTerm-win-x64` artifact for every pull request and every push to
`main`.

### Publish a release

From GitHub: **Actions → release → Run workflow** on `main`, with the `X.Y.Z` number; or push a `vX.Y.Z` tag
on `main`. The [`release.yml`](.github/workflows/release.yml) workflow runs the tests, builds the executable
with that version number, creates the tag if it does not exist and publishes the GitHub *Release* with the
zip and `SHA256SUMS.txt`. Release notes are read from `docs/releases/vX.Y.Z.md` when that file exists.

## Limitations and ideas

**Current limitations**

- **Privilege Cloud** (sign-in through CyberArk Identity) and **SAML** are not supported.
- The Accounts API does not say which PSM components a platform offers: the component is deduced, then can be
  remembered.
- KeePass vaults: Twofish encryption and YubiKey keys are not supported; no vault creation (create it with KeePass
  or KeePassXC); attachments are kept but not shown.
- Following the terminal folder requires bash or zsh on the server.
- PSM Gateway (HTML5), dual control and exclusive access are not supported.

**Ideas**

- Signed executable and MSI installer.
- Several PVWAs (connection profiles), Privilege Cloud.

## License

[MIT](LICENSE) © 2026 muller-camille
