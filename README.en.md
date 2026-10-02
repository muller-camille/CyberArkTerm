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
| **PSM sessions** | Remote desktop through the PSM (like the PVWA "Connect" button): component, target machine, reason, ticket. |
| **SSH sessions (PSMP)** | Built-in terminal in a tab (xterm compatible: colors, vim, less, top…), MFA authentication. |
| **Files tab** | SFTP browser of the server: `ls`, navigation, `rm`, drag-and-drop upload over SCP, follows the terminal folder. |
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
- The Remote Desktop client (`mstsc`, installed by default) for PSM sessions.
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
- Also: new folder, download, copy path, show hidden files.
- **Follow the terminal folder**: when ticked, every `cd` in the terminal moves the browser to the same
  folder (see [How it works](#how-it-works)).

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
| SSH tab | Close | Tab cross or middle click |
| Files | Open / parent folder / delete / refresh | `Enter` / `Backspace` / `Del` / `F5` |

## Settings and configuration file

![Settings](docs/captures/en/settings.png)

| Setting | Purpose | Default |
| --- | --- | --- |
| Interface language | Français, English, Italiano or system language; applied after signing out or at the next start | Windows language (English if it is not translated) |
| PSMP address and port | PSM for SSH server; empty = SSH disabled | empty, 22 |
| Double-click on Unix = SSH | Opens Unix accounts over SSH rather than PSM | no |
| SSH in CyberArkTerm | Built-in terminal and Files tab; otherwise Windows Terminal | yes |
| Follow the terminal folder | Allows setting up folder tracking in the shell | yes |
| File upload | SCP or SFTP | SCP |
| Accepted PSMP keys | Remembered fingerprints ("Forget keys" button) | — |
| Remembered components | PSM component chosen per platform ("Forget" button) | — |

All preferences are saved in `%APPDATA%\CyberArkTerm\settings.json`: language, PVWA address, sign-in method
and user name, the settings above, "My servers" and their folders, recent sessions. This file contains
**no password, token or private key**. To start from scratch, close the application and delete it.

## Security

- **HTTPS required** to the PVWA; certificate validation is never disabled.
- **No secret on disk**: CyberArk password, session token, MFA key and PSMP password stay in memory for the
  session. The PVWA session is closed (`Logoff`) on exit.
- PVWA session opened with `concurrentSession`: your PVWA web session, if any, is not closed.
- **RDP files** (one-time PSM token) written to `%TEMP%\CyberArkTerm` and deleted after 60 s or on exit.
- **PSMP host keys pinned** on first use, with a warning if they change.
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
| `POST /PasswordVault/API/Auth/Logoff` | Sign out |

### PSMP sessions

Each SSH tab opens up to three connections to the PSMP, with the same login
`<you>@<account>[#domain]@<target>`: the terminal, the SFTP connection of the Files tab, and an SCP
connection on the first SCP upload. Each one is a PSMP session, recorded by the PSM.

### Following the terminal folder

When an SSH session opens (if the option is on), CyberArkTerm sends the shell a one-line command, preceded by
a space so it stays out of the history:

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
| The browser does not follow `cd` | The remote shell is not bash or zsh, or the option is off in Settings. |
| "The key of the PSMP has changed" warning | Only continue if your CyberArk team confirms a server change. |

## Development

### Structure

| Project | Role |
| --- | --- |
| `src/CyberArkTerm.Core` | Cross-platform logic without UI: PVWA API client, account classification, xterm terminal emulator, PSMP connections and SFTP/SCP browser (SSH.NET), "My servers" folders, preferences. |
| `src/CyberArkTerm.App` | WPF application: windows, tabs, terminal control, `mstsc` launch, icon (`Assets`). |
| `tests/CyberArkTerm.Core.Tests` | xUnit tests of Core (fake PVWA over HTTP, terminal, PSMP, folders, translations…). |

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

Push a `vX.Y.Z` tag on `main`: the [`release.yml`](.github/workflows/release.yml) workflow runs the tests,
builds the executable with that version number and creates the GitHub *Release* with the zip and
`SHA256SUMS.txt`. Release notes are read from `docs/releases/vX.Y.Z.md` when that file exists.

## Limitations and ideas

**Current limitations**

- **Privilege Cloud** (sign-in through CyberArk Identity) and **SAML** are not supported.
- The Accounts API does not say which PSM components a platform offers: the component is deduced, then can be
  remembered.
- PSM (RDP) sessions open in the Windows Remote Desktop window, not in a tab.
- Following the terminal folder requires bash or zsh on the server.
- PSM Gateway (HTML5), dual control and exclusive access are not supported.

**Ideas**

- Signed executable and MSI installer.
- RDP sessions in built-in tabs.
- Several PVWAs (connection profiles), Privilege Cloud.

## License

[MIT](LICENSE) © 2026 muller-camille
