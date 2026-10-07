# ZillaTerm user guide

[Français](guide.fr.md) · **English** · [Italiano](guide.it.md) · [← Back to the README](../README.md)

## Contents

- [1. Sign in to the CyberArk Vault](#1-sign-in-to-the-cyberark-vault)
- [2. Find an account: "Available" tab](#2-find-an-account-available-tab)
- [3. Open a PSM session (remote desktop)](#3-open-a-psm-session-remote-desktop)
- [4. Open an SSH session through the PSMP](#4-open-an-ssh-session-through-the-psmp)
- [5. Browse and upload files: "Files" tab](#5-browse-and-upload-files-files-tab)
- [6. Organize your servers: "My servers" tab](#6-organize-your-servers-my-servers-tab)
- [7. Emergency access outside CyberArk: KeePass databases](#7-emergency-access-outside-cyberark-keepass-databases)
- [Shortcuts](#shortcuts)
- [Settings and configuration file](#settings-and-configuration-file)
- [Security](#security)
- [How it works](#how-it-works)
- [Troubleshooting](#troubleshooting)

## 1. Sign in to the CyberArk Vault

<img src="captures/en/sign-in.png" alt="Sign-in window" width="440">

Enter the PVWA address (`pvwa.mydomain.local` is enough: `https://` and `/PasswordVault` are added), choose the
authentication method, then your user name and password. If the RADIUS server asks a question (OTP code), the window
shows it and waits for your answer. While you type a password, a "Caps Lock is on." warning shows if the key is on
(likewise for KeePass databases and the local vault).

The address, method and user name are remembered; **the password never is**.

The list at the bottom left changes the interface language (Français, English, Italiano); the window reopens right
away in the chosen language, keeping the address and user name you typed.

### Main window

- **Side panel**: "Available", "My servers" and "Files" tabs (`Ctrl+1`, `Ctrl+2`, `Ctrl+3`). Drag the splitter to
  change its width; `Ctrl+B` or a double-click on the splitter collapses it (the strip of tabs stays: a click on a
  tab opens it again). `F6` moves from the panel to the session. The window position and size, the panel width and
  whether it is collapsed are remembered.
- **Session tabs**: a dot shows the state of the session by its colour and by its shape: orange ring while
  connecting, green dot once connected, grey ring when the session has ended, red dot when it failed (the name is
  then dimmed). The tooltip gives the full name, the state and the mode: "Through the PSMP …: session managed by
  CyberArk" or "Direct emergency access (KeePass): outside CyberArk, written to urgence.log". A name that is too long
  is truncated; a name already open is numbered ("srv01 (2)"). When the tabs no longer fit, the strip scrolls (mouse
  wheel, the selected tab stays visible) and "⌄" lists them all with their state. `Ctrl+Tab` / `Ctrl+Shift+Tab`:
  next / previous tab; `Ctrl+F4` or `Ctrl+Shift+W`: close the tab.
- **Closing a connected session** (SSH, remote desktop, VNC) asks for confirmation, with a "Don't ask again when
  closing a session" box (the "Confirm before closing a connected session" setting, Settings › Terminal). On
  sign-out and on exit, a single window sums up what will be closed: sessions, transfers running, edited files not
  sent back.
- **Confirmations**: the buttons say the action ("Delete the account", "Replace the key and connect"…), "Cancel" is
  the default button, and the server, account or safe concerned is named. Values to compare (fingerprints) are shown
  in a fixed-width font with "Copy". Some irreversible actions (deleting an account, accepting a server key that
  changed) also require ticking a box.
- **Greyed-out buttons**: their tooltip says what is missing (selection, PSMP, SSH session for "Parallel"…). In
  emergency access, the buttons specific to CyberArk are hidden.
- **Status bar**: an ordinary message clears after 10 seconds; an error stays until the next message. The number of
  accounts only shows with the "Available" tab.

## 2. Find an account: "Available" tab

![“Available” tab filtered on several servers](captures/en/available.png)

- The search box ("Filter accounts…", `Ctrl+F`) filters on every field (server, user, safe, platform, domain…),
  several words allowed (`prd sql`).
- Instead of an empty list, the tab says what is going on: accounts loading, loading failed with its message and
  "Retry", no account available to your CyberArk user, or no account matching the filter, with "Clear the filter".
- "Group by" sorts accounts by safe, platform or target type.
- Right-click an account → "Export the displayed accounts (CSV)…" saves the accounts shown (filtered by the
  search) to CSV.
- **Safe members**: right-click an account (or a safe when accounts are grouped by safe, or a server in "My
  servers") → "Safe members". The window lists the users and groups of the safe with their rights (list, use,
  retrieve, add accounts, update, delete, manage members…), shows who can **add accounts**, and details every right
  of the selected member. While reading, it shows "Loading the members…"; on an error, the message is shown in the
  middle with "Retry". The PVWA only gives this list to an account with the "View Safe Members" right on the
  safe. `Ctrl+A` then `Ctrl+C` copies the table. With the "Manage safe members" right, the "Add a member…", "Edit
  the rights…" (or double-click) and "Remove…" buttons manage the members: name, type (user or group), directory
  ("Vault" or the LDAP domain), optional end date and the 22 rights, grouped as in the PVWA. The "Profile" list
  ticks at once the rights of a common use (read only, account user, account manager, full); sensitive rights are
  flagged and granting them asks for confirmation; "Changes: +n / −n" sums up what changes. Removing a member is
  confirmed ("Remove the member").
- **Add an account**: right-click an account (or a safe when accounts are grouped by safe) → "Add an account to the
  safe…". Safe, platform, address and user name are required; logon domain, account name, password, allowed machines
  and CPM management are optional. The account you clicked is used as a template (safe, platform, domain) and the
  cursor is put in the first empty required field. The
  account is created with the rights of your session: the "Add accounts" right on the safe is required, and usually
  "Update account content" to give the password. The list is then reloaded and the new account selected.
- **Import accounts (CSV)**: right-click an account or a safe → "Import accounts (CSV)…". A window asks for the file
  ("Save a template…" gives an example), the default safe and platform, then shows a preview of every line of the
  file (lines with an error in red, "Errors only" box) and the safes concerned; nothing is sent before the "Create N
  accounts" button. Required columns: address and user name (plus
  safe and platform, otherwise the defaults); optional: name, domain, password, allowed machines, CPM management
  (yes/no), reason. Separator `;`, `,` or tab, column names in English, French or Italian; a file made with "Export the
  displayed accounts" can be imported again. A second window then creates the accounts line by line and shows each line's status
  (created, refused with the PVWA message, not imported, not sent; "Stop" available). Closing it during the import
  asks for confirmation: "Continue" (the default) or "Stop the import" (the account being created is finished, the
  accounts already created stay in the CyberArk Vault). At the end it offers to save
  the result as CSV (without the passwords). The file's passwords are never shown; delete the file after the import.
- **Edit / delete an account**: right-click → "Edit the account…" (platform, address, user name, domain, name,
  allowed machines, CPM management; only the changed fields are sent) or "Delete the account…": the confirmation
  says that the account is deleted for every user and that its password can no longer be retrieved; you must tick
  "I understand that the current password will no longer be retrievable".
  Rights "Update account properties" and "Delete accounts".
- **Password status (CPM)**: an account's tooltip tells whether the CPM manages it, and the date of the last change,
  verification and reconciliation; a **⚠** marks an account whose last CPM operation failed.
- **Right-click → "Password"** ("Available" accounts and "My servers" servers):
  - "Verify (CPM)", "Change (CPM)…", "Reconcile (CPM)…" ask the CPM for the operation (confirmation to change and
    reconcile, with the "Change the password" and "Reconcile" buttons; right "Initiate CPM account management
    operations"). The CPM then handles it: `F5` shows the new status.
  - "Copy the password…": the window names the account and its safe, and says that the retrieval is recorded in the
    CyberArk audit and that the password stays 20 seconds in the clipboard. Reason and ticket are optional, unless
    the platform requires them; "Retrieve and copy" copies the password **without showing it**, then the status bar
    counts down the seconds before it is cleared ("Retrieve accounts" right).
- On the Home tab, **quick connect** (`Ctrl+K`; the cursor is there at startup) finds a server as you type: press
  Enter to connect. It says when no account matches, and only shows the first 50 results ("First 50 of N accounts:
  type more to narrow the search."). The **recent sessions** are dated ("Today 09:28", "Yesterday 18:02");
  right-click → "Remove from the list" (or `Del`) removes one. They stay greyed out until the accounts are loaded
  from the PVWA ("waiting for the PVWA accounts…"); they are those of the PVWA you are signed in to: the same account
  ID means another account on another PVWA.

## 3. Open a PSM session (remote desktop)

<img src="captures/en/psm-connect.png" alt="Advanced PSM connection: target machine, reason, ticket" width="520">

Double-click the account (or press Enter, or the "Connect" button); a Unix account opens over SSH through the PSMP
when its address is set, or as files only for an "SFTP" platform (see 4.), and "Advanced connection…" then lets you
choose the PSM. ZillaTerm requests the
connection from the PVWA and opens the session in Windows **Remote Desktop Connection** (`mstsc`), exactly like the
PVWA "Connect" button: the PVWA's RDP file is handed over as is. A component that opens a remote application
(RemoteApp) opens its windows on this computer's desktop.

- **PSM component**: deduced from the platform (`PSM-RDP` for Windows, `PSM-SSH` for Unix and network,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Tick "Remember this component" to keep it for the whole platform. Your
  PVWA may name its components differently (for example `WIN-PSM`): enter the name its "Connect" button offers; the
  list then offers the components already used, the platform's first. For all your Windows accounts, set the
  component once in **Settings › CyberArk** ("Windows accounts", for example `WIN-PSM`); a component remembered for a
  platform still comes first. Per-platform components are shown and edited in the same place ("Component per
  platform").
- **Domain accounts**: an account registered for its domain has no server. It is recognized by its domain platform,
  its allowed machines, or its address: its logon domain, a domain with other servers below it (`corp.local` when an
  account targets `srv01.corp.local`), or the domain of the PVWA or of the workstation. Never a session to the domain
  itself: the server is always asked, "Advanced connection" included. The "Choose the server" window asks which one to open the session on: the list offers
  the servers already used with this account (recent sessions, "My servers"), then its allowed machines; an account
  restricted to its machines refuses the others. "Keep this server in “My servers”", with the folder you want, adds
  it after a successful connection, named `account@server` (the choice is remembered for next time; the box
  disappears when the server is already there). "Advanced…" opens the full window with this server.
- **Reason and ticket**: if the PVWA refuses the request (reason required, component not configured…), its message
  is shown and you can fix it and try again.
- The "Advanced…" toolbar button (or right-click → "Advanced connection…") opens this window on demand. The cursor
  is put in the first usable field; over SSH or files only, the fields used only by the PSM are greyed out and
  their tooltip says so; without a component, the window asks you to choose one.

## 4. Open an SSH session through the PSMP

Set the PSMP once in **Settings › CyberArk** (see [PSMP by domain](#psmp-by-domain) if you have several).
Double-click (or Enter) then chooses from the name of the account's platform:

| Platform name | Opened by default |
| --- | --- |
| contains "SFTP" (`UnixSFTP`, `SFTP-Partners`…) | **files only** over SFTP through the PSMP (see below) |
| contains "SSH" (`UnixSSH`, `CiscoSSH`…), or another Unix platform | **SSH** through the PSMP |
| other | **PSM** (remote desktop) |

Right-click always offers the three (the default is in bold): "Connect (PSM)", "Connect over SSH (PSMP)" (or the
"SSH" button) and "Open the files (SFTP, PSMP)".

**Files only**: a single PSMP SFTP session, without a terminal. A tab shows its state; the files are in the "Files"
tab, with the same functions (checked transfers, queue, editor, compare, live follow, permissions), except what needs
a terminal (following the terminal folder, extracting a `.tar.gz` archive). Useful to just drop or fetch files, or
when the platform allows PSMP-SFTP but not the shell. Like every PSMP session, it is recorded and audited by
CyberArk.

The session opens **in a ZillaTerm tab**, with the standard PSMP login `<you>@<target account>[#domain]@<target
server>`. User names containing spaces (`John Smith`, `Local Admin`) are accepted. The side panel switches to the
server's "Files" tab (a collapsed panel stays collapsed).

### PSMP by domain

With one PSMP per domain, declare them in **Settings › CyberArk**: a **default PSMP** and the **PSMP by domain** list
("Add a PSMP", address, port; the domain served is the one of the address, and can be changed). Each server goes
through the PSMP of the domain **closest to its own**:

| Configured PSMPs | Server | PSMP used |
| --- | --- | --- |
| `psmp.xxx.corp.com`, `psmp.zzz.corp.com` | `srv01.xxx.corp.com` | `psmp.xxx.corp.com` |
| same | `srv02.zzz.corp.com` | `psmp.zzz.corp.com` |
| `psmp.xxx.corp.com` only | `srv03.zzz.xxx.corp.com` | `psmp.xxx.corp.com` (no PSMP for `zzz.xxx.corp.com`) |
| same | `srv04.other.org`, an IP address | the default PSMP; without one, the only PSMP of the list if there is just one |

Without a PSMP for a server (several PSMPs, no default, domain not covered), the connection is refused and the status
bar says so. "Which PSMP for the server" gives the answer before saving; two PSMPs for the same domain are refused.
Only the PSMPs of the list receive your CyberArk password, never an address deduced from a server name; the key of
each PSMP is checked on its first connection. The tab tooltip names the PSMP used.

<img src="captures/en/psmp-authentication.png" alt="Authentication question asked by the PSMP" width="49%"> <img src="captures/en/terminal-menu.png" alt="Right-click menu of the SSH terminal" width="49%">

- **Authentication**: if the PVWA provides an "MFA caching" key, no question is asked. Otherwise the PSMP questions
  (password, MFA code) are shown in a window that names the session concerned, with help depending on the question:
  probably the password of your CyberArk account (reused for the SFTP and SCP connections of the same tab, never
  saved), or the MFA code (asked again at each connection).
- **PSMP key**: on first connection, a window shows its SHA-256 fingerprint in a fixed-width font, with "Copy":
  compare it with the one published by your CyberArk team before "Trust and connect" ("Cancel connection" is the
  default button). The fingerprint is then remembered on this computer. If the key changes, a red banner warns of a
  possible interception, the remembered and the new fingerprints are shown, and "Replace the key and connect" is
  only possible after ticking "I confirmed this change with the CyberArk team". A refused key stops the connection
  ("Connection cancelled: the server key was not accepted.").
- **Terminal**: selecting copies, the mouse wheel or the scroll bar on the right goes through the history; once you
  have scrolled up, "↓ Back to the end" (or typing) takes you back to the end. AltGr works on international
  keyboards. Close the tab with its cross, a middle click or `Ctrl+F4` (confirmation if the session is connected).
- **End of session**: a banner at the top of the terminal gives the reason, with "Reconnect"; the last lines stay
  readable, selectable and copyable.
- **Right-click in the terminal** (or the keyboard's Menu key): copy, paste, select all, search, save the content,
  clear the history (on this computer only, nothing is sent to the server), font size, and the tab's actions
  (reconnect, duplicate, detach, parallel view, add to "My servers", close). To paste with a plain right-click
  instead, tick "Right-click in the terminal pastes the clipboard" in Settings; Shift+right-click then opens the menu.
- **Pasting several lines**: when the shell would run the lines one by one (no bracketed paste), a window shows the
  lines and asks for confirmation ("Paste", "Cancel" by default), with a "Don't warn again before pasting several
  lines" box (the "Warn before pasting several lines when the shell would run them one by one" setting, Settings ›
  Terminal).
- **Appearance**: colour palette and font size in Settings (Campbell, One Half, Solarized, dark or light);
  `Ctrl+wheel` enlarges or shrinks a terminal, `Ctrl+0` goes back to the default size.
- **Search** in the terminal, history included: `Ctrl+Shift+F` (or right-click in the terminal or on the tab).
  Matches are highlighted; `Enter` goes up to older ones, `Shift+Enter` goes down, `Esc` closes.
- **Save the content** of the terminal (history and screen) to a text file: `Ctrl+Shift+S` (or right-click in the
  terminal or on the tab). Only when you ask: the file may contain sensitive information.
- **Detach a tab** (another screen): drag the SSH tab out of the window, or right-click → "Detach to a new window".
  The terminal moves to a separate window and the session goes on. The tab keeps its place ("Show the window",
  "Bring it back here") and the Files tab works on this session when it is selected. Closing the separate window
  brings the terminal back to its tab without closing the session. Remote desktop tabs do not detach (use "Full
  screen"); PSM sessions already open in Windows Remote Desktop Connection, a window of its own.
- **Parallel view** (up to 8 sessions on screen): "Parallel" toolbar button (greyed out while no SSH session is
  open), or right-click an SSH tab → "Add to the
  parallel view". Tick the open SSH sessions to show together (8 at most): they are laid out as a grid in the
  "Parallel" tab, side by side up to 3, then on two rows. Each session has its title and state (a ring while
  connecting, a full dot afterwards); "⤢" (or a double-click on the title) enlarges it alone, "✕" sends it back to
  its tab. The session you type in has a thicker frame and the "⌨ Typing here" mark. The Files tab follows the
  session you work in. "Close the view" gives each terminal back to its tab without closing the sessions. Remote
  desktop sessions cannot go there.
  - **Simultaneous typing**: "Simultaneous typing" button of the view. What you type in a ticked session ("Receives
    the typing") is also sent to the other ticked, connected sessions: the same command on several servers. It is
    **off every time the view opens**; when on, the button turns amber with "ON (n)", an amber banner gives the
    number and names of the sessions receiving the typing, and an amber frame surrounds them; unticked sessions are
    dimmed and marked "excluded". A session added while it is on is not ticked; what is
    typed in an unticked session only goes to it. Each key is encoded by the session that receives it (arrows work
    in a shell as in vim). The mouse wheel is not copied, and pasting several lines into several sessions asks
    first.
  - **From "My servers"**: right-click a folder → "Open in the parallel view" connects its SSH servers (subfolders
    included) and puts them straight in the view; or pick servers with `Ctrl+click` (`Shift+click` for a range),
    then right-click → "Open the N servers in the parallel view". Beyond the free places (8 at most), a window asks
    which ones to open; Windows (PSM) servers are left out. Each connection stays a separate PSMP session, with its
    usual questions.
  - **Separate window**: "Separate window" button of the view, right-click the "Parallel" tab, or drag the tab out
    of the window. Closing it brings the view back to its tab without closing the sessions.
  - **Send files** to the sessions of the view: "Send files…" button (see the Files tab).

## 5. Browse and upload files: "Files" tab

The **Files** tab of the side panel (`Ctrl+3`) follows the active SSH tab and comes to the front when an SSH session
opens. It also serves the files-only sessions, which show it when they open: CyberArk accounts over
SFTP through the PSMP ([section 4](#4-open-an-ssh-session-through-the-psmp))
and KeePass SFTP, FTP, FTPS entries ([section 7](#7-emergency-access-outside-cyberark-keepass-databases)).

![Files tab sorted by date, next to the terminal](captures/en/main-window.png)

- **Tab badge**: on the "Files" tab, a badge gives the number of transfers running or waiting, or "!" for a failed
  or differing transfer you have not seen yet (showing the tab marks it as seen). The ZillaTerm button in the
  Windows taskbar also shows the activity or the failure.
- **Path bar**: current path, editable (type a path, then Enter). Double-click a folder to enter it, `..` to go up,
  "parent folder" (its icon differs from the "Upload" one) and "home folder" buttons.
- **Columns and sort**: click a column header (Name, Size, Modified, Permissions); click it again to reverse the
  order (an arrow shows it). Size and date start with the largest and the newest. Folders stay on top; the sort is
  kept from one folder and one session to the next. The Name column takes the width left by the others; when the
  panel is narrow, the Permissions column is hidden rather than cut (it comes back when the panel is widened).
- **Toolbar buttons**: Download, Edit, Rename, Permissions and Delete are greyed out, as in the menu, while the
  selection does not fit; their tooltip says what to select ("Select a single file (not a folder)."…).
- **Upload files**: drag them from Explorer onto the list (or the "Upload" button). Sent over **SFTP** by default
  (SCP can be chosen in Settings), folders included. Dropped on a folder row, they go into that folder: the row is
  highlighted and the status bar shows the destination ("Drop into server:/path"). If items already exist on the
  server (hidden ones included, even when they are not shown), a confirmation names the server and the items, and
  offers "Replace", "Skip existing" or "Cancel" (the default); a replaced file is rewritten in place and stays
  incomplete if the upload is cancelled or fails. If the server
  refuses that protocol for a file before receiving it (rule of the PSMP, read-only SFTP…), the other one takes over
  at once, with no question and no wait: the status bar and the summary show it with the server's answer, and so does
  the transfer history ("SCP (SFTP refused)"). Over SCP, after a refusal when a file is announced, files at least as large go
  straight over SFTP until the tab is closed.
- **Download**: "Download" button or right-click. One file asks where to save it; several go to a folder you choose,
  with one question ("Replace") for the files already there. A local file is replaced only once its download is complete: an
  interrupted or cancelled download leaves it as it was. "Download" only takes files: for a folder, the status bar
  reminds you to drag it to File Explorer or to the desktop.
- **Download by dragging**: drag files or folders from the list to Explorer or the desktop. Nothing is downloaded
  while dragging: on drop, a window shows the progress (Cancel stops it), then Explorer copies the files where you
  dropped them. Unix names are made valid for Windows (`\`, `:`, `..`, `CON`… replaced), never writing outside the
  drop folder; the temporary download folder is deleted afterwards.
- **Transfer queue**: uploads and downloads run one at a time, in the order requested; what you ask for during a
  transfer is added to the queue instead of being ignored. An upload goes to the folder shown when you dropped the
  files (or to the folder they were dropped on), and the overwrite confirmation also counts uploads still waiting.
  The "Transfers" panel, above the status bar, shows each item with its server (waiting, progress and file n/N,
  check, result): ✕ removes a waiting item, "Cancel" stops the running one, "Cancel all" cancels everything left.
  The results stay shown once the transfers are over: "✓ done · SHA-256 verified (n/n)", "⚠ done · not verified: n
  of N", or the failure in red; "Details" on a finished line shows the SHA-256 checksum of each file, and "Clear
  finished" removes the finished, failed and cancelled transfers from the list (the transfer history keeps them).
  A stopped transfer deletes the file being transferred, which is incomplete (on the
  server for an upload, on this computer for a download); files already transferred stay. Beware: if the upload was
  replacing an existing file and had started writing it, its old content is lost; stopped before any content, the
  server file stays as it was. Over SCP, stopping ends only that transfer: the next items go
  on over the same connection; a transfer that no longer moves (server not reading) stops 2 s after "Cancel" and the next
  ones go on over a new connection. A file sent over SCP gets the upload date on the server (as `scp` without `-p`, and as over SFTP). An error is shown in red in the queue
  and the queue goes on; at the end, a single summary in the status bar of the tab (three lines at most, full text
  in the tooltip). Browsing, deleting, permissions, the editor and dragging to
  Explorer get in between two files. Closing the tab with transfers running asks for confirmation ("Cancel the
  transfers and close" or "Keep transferring"); on sign-out and on exit, they are listed in the summary window.
- **Many files at once: .tar.gz archive**: from 200 dropped files (threshold in Settings, option "Offer a single
  .tar.gz archive"), ZillaTerm offers to send them in a single archive: one file to transfer and check instead of
  thousands, much faster through the PSMP. The archive is made on this computer (in the queue, can be cancelled),
  sent and checked (SHA-256), then deleted from this computer; each dropped item is at the root of the archive
  (permissions 0644 and 0755). Nothing is run on the server: an orange box shows up at the bottom of the Files tab
  with the extraction command, for example `cd '/opt/app' && /usr/bin/gzip -dc './deploy.tar.gz' | tar xf - && rm -f
  './deploy.tar.gz'` (the archive is deleted from the server once extracted). "Copy the command", or "Type it in the
  terminal", which types it at the prompt of the session without running it: check it, then press Enter. The box
  stays (for that session) until you close it. "Send the files one by one" keeps the usual upload; "Don't offer
  again" turns the option off.
  - **Every Unix** (Red Hat 5 to 9, HP-UX 11.11 and 11.31, Solaris, AIX…): the archive is in the standard POSIX tar
    format (ustar), read by every tar, and the command uses `gzip` and `tar xf` separately, with no GNU tar specific
    option. It can be typed in any shell (sh, ksh, bash, zsh, csh, tcsh).
  - **gzip** is looked for on the server (through SFTP) where each system installs it: `/bin`, `/usr/bin`,
    `/usr/contrib/bin` (HP-UX), `/usr/local/bin`, `/opt/freeware/bin` (AIX), `/usr/sfw/bin` and `/opt/csw/bin`
    (Solaris). Not found: the archive is sent uncompressed (`.tar`), extracted by `tar` alone.
  - A name over 100 characters (folders excluded) or a file over 8 GB does not fit this format: the files are then
    sent one by one, with a message.
- **Send to several servers**: button (arrow to three servers) or right-click → "Send to several servers…". Pick the
  files or folders, the destination folder (`~` = the home folder of the account on each server, e.g. `~/deploy`)
  and the SSH sessions to send to. ZillaTerm first checks on each server that the folder exists and what would be
  replaced (a single question for all), then queues one upload per server: same protocol, same SHA-256 check on each
  server, a single summary at the end.
- **Compare**: right-click a file → "Compare with…": the same path (or another one) on a server with an open SSH
  session, or a file of this computer; with two files selected, "Compare the 2 files". "Browse…" next to the path
  opens an explorer of the other server, on the same folder with the file preselected (or on the closest existing
  parent folder): double-click a folder to enter it, Backspace to go up, a path can also be typed; the chosen file
  replaces the path. The Files tab of that server stays on its folder. The same file on the same server is refused;
  with no other server open, a file of this computer is offered. The files are read **in
  memory** (50 MB at most each), without a copy on this computer. The window shows the lines side by side: removed
  in red on the left, added in green on the right. `F7` / `Shift+F7`: next / previous difference; "Ignore spaces";
  "Only the differences"; "Save the diff…" in the `diff -u` format. A binary file (or one over 10 MB) is compared by
  its size and SHA-256 checksum. With a comparison tool chosen in Settings (WinMerge, VS Code…), "Open in …" gives
  it two temporary copies, deleted when the window closes.
- **Transfer history**: "Transfers" toolbar button (up and down arrows with a clock, left of "Settings"), available
  even without a session. It lists the last 200 uploads and downloads (drag and drop included): date, direction,
  server, item, destination, number of files, protocol ("SCP (SFTP refused)" when the other protocol took over),
  result, written as in the queue; failures and differing files are in red, and the text of a column too narrow for
  it shows in a tooltip. "Uploads" / "Downloads" filter; "Checksums…" (or double-click)
  shows each file's size, SHA-256 checksums and result, and copies them in the `sha256sum -c` format to check again
  on the server; "Open the folder" for a download; "Clear the history", set apart from the other buttons, asks for
  confirmation (the files themselves are not touched).
- **Transfer check (SHA-256)**: every uploaded or downloaded file is checked. On upload (SCP or SFTP), the local
  file is hashed, then the file on the server is read again over SFTP and hashed. On download, the data received
  from the server is hashed, then the file written on this computer is read again. The status bar confirms "✓
  identical on both sides"; the checksums of each file are in "Details" in the queue and in the transfer history
  ("Transfers" button). If a file differs,
  the error is shown and the details open by themselves; a drag-and-drop download fails rather than deliver a wrong
  copy. A file that cannot be read again (permissions) is reported as "not checked". Reading an upload again doubles
  the data exchanged with the server.
- **Delete**: select, then Del (or right-click → "Delete (rm)"), with a confirmation that names the server and
  reminds you that there is no recycle bin on the server. Folders must be empty. A symbolic
  link is deleted itself, never the file or folder it points to.
- **Rename**: `F2`, right-click → "Rename…" or the toolbar button. A file is never overwritten: a name already taken
  is refused before anything is sent to the server (over SFTP as over FTP). "/", ".", ".." and control characters
  (line break, tab…) are refused, as for "New folder".
- **Edit a file**: **double-click** the file (or `Enter`, `F4`, right-click → "Edit", the pencil button). The file
  opens in the text editor chosen in Settings (Notepad by default). On double-click, an archive, an image, an
  executable or an office document is downloaded instead of opened, as is any file whose first bytes are binary. Every time you save, ZillaTerm offers to send it back to the
  server ("Send back" or "Not now"): sent over SFTP, the file's permissions are kept. If the file changed on the
  server since you opened it, a warning says so and asks for confirmation ("Replace with my version") before
  overwriting it.
- **Follow a file (tail -f)**: right-click one or several files → "Follow (tail -f)". A window shows the end of the
  file, then each new line as soon as it is written, like `tail -f`, reading the file over SFTP every second: no
  command runs on the server. A file truncated or replaced by a rotation is read again from the start; the last
  10,000 lines are kept.
  - **Colours and alerts**: errors (ERROR, FATAL, CRITICAL…) in red, warnings (WARN) in orange; words of your choice
    highlighted in yellow ("Highlight", separated by commas). "Alert on" (e.g. `ERROR, OutOfMemory, Connection
    refused`): every new line containing one of these words is marked, the "⚠ n alerts" counter goes up (a click
    goes to the next one) and the window's taskbar button flashes; a Windows notification can be shown, at most one
    every 30 s, with the number of lines and the file name only, **never the content of the lines** (it may show on
    the lock screen). These settings are kept for the next windows.
  - **Combined view**: several selected files open in a single window, and "Add to a follow window" adds a file from
    another tab, so from another server. Lines are interleaved in the order they arrive, prefixed and coloured by
    file (`[root@srv01 app.log]`); at the bottom, each file has its state and a button to stop following it.
  - **Filter and search**: filter (like `grep`), exclusion (like `grep -v`), context lines (like `grep -C 3`), as
    plain text or regular expressions. A filled-in filter or exclusion box turns amber, with ✕ to empty it, and the
    status bar shows "Filter: n / N lines shown". `Ctrl+F` searches the lines without filtering them (Enter / `F3`:
    next, `Shift+F3`: previous). Scrolling up stops following the end.
  - **Disconnections**: when the connection is lost, a marker says so; when the SSH tab reconnects (or with
    "Reconnect"), following resumes where it stopped, with the lines written in the meantime. Closing the tab stops
    following its files; the window keeps the lines received.
  - **Remembered files**: on a server of the "My servers" tab, followed files are remembered (the last 12); at the
    next connection, the follow button of the Files tab offers them: "Follow them all in one window" in one click,
    or a single one.
  - **Keep a trace**: "Save…" writes the displayed lines to a file on this computer; "Record continuously…" writes
    the lines already received, then each new line as it arrives, as long as the box is ticked; "Marker" inserts a
    `—— 14:32:05 ——` line to find a moment again (before a change, for example).
  - **Connection**: by default, following uses the SFTP connection of the Files tab; it then runs between two files
    of a transfer. The Settings option "Follow files (tail -f) in an independent session" gives it its own
    connection, one per window and server: it no longer waits for transfers, but it is one more PSMP session
    (recorded separately, and an MFA validation may be asked). It is closed with the window. A lost connection is
    never reopened in a loop.
- **Permissions**: right-click → "Permissions…" (or the padlock button). Read / write / execute boxes for owner,
  group and others, special bits (setuid, setgid, sticky) and the octal value (`644`, `1777`…), for one or several
  items. When the selected items do not all have the same permissions, a box left in the middle state leaves that
  permission unchanged on each item: only the changed permissions are applied. For a folder, "Apply to the folder
  contents too" propagates the permissions to subfolders and files; by
  default, execute (x) is only given to folders and to files that are already executable. The button then becomes
  "Apply recursively…" and a confirmation says what will happen; while it runs, "Stop" in the Files tab interrupts it
  (items already done keep their new permissions). Symbolic links are not followed and the owner is not changed.
- Also: new folder, download, copy path, show hidden files.
- **Follow the terminal folder**: when ticked, every `cd` in the terminal moves the browser to the same folder (see
  [How it works](#how-it-works)). After `sudo -i` or `su`, tick the box again at the shell prompt to re-enable
  tracking in that new shell.

<img src="captures/en/transfer-history.png" alt="Transfer history with the SHA-256 check of each file" width="820">

## 6. Organize your servers: "My servers" tab

![My servers organized in folders](captures/en/my-servers.png)

- **Add** an account: right-click in "Available" → "Add to My servers" then the folder you want, or drag the account
  onto the "My servers" tab, or the "Add" toolbar button. For a domain account, the server is asked (optional:
  without a server, it is asked at each connection).
- **Add an open session**: right-click the session tab (or in its terminal) → "Add to My servers" then the folder
  you want. The server keeps the connection type and the target machine; the entry is greyed out when it is already
  there.
- **Add a recent connection**: right-click in the recent sessions of the home page → "Add to My servers" then the
  folder you want. The server keeps the connection type (PSM, SSH or files only), the PSM component and the target machine
  used.
- **Folders**: right-click → new folder or subfolder, rename, delete; drag servers and folders to move them. Deleting a
  folder counts and deletes all its servers of this PVWA, even those hidden by the search; those of another PVWA stay.
  Removing a server or deleting a folder asks for confirmation (the accounts stay in "Available"). The "Properties /
  rename" and "Remove the server or delete the folder" buttons, at the top of the tab, are greyed out while nothing
  is selected.
- **Search**: box at the top of the tab (or `Ctrl+F` in the tab). It filters servers by name, server, user, folder,
  component, target machine, and the entries of unlocked KeePass databases; the folders of the results are expanded.
  `Enter` or `↓` selects the first result, `Esc` clears.
- **Several servers at once**: `Ctrl+click` adds or removes a server (or all those of a folder), `Shift+click` picks
  a range of servers; `Esc` or a plain click cancels. Right-click one of them → "Open the N servers in the parallel
  view" or "Connect to the N servers" (one tab each). Right-click a folder → "Open in the parallel view" or "Connect
  to the N servers".
- **Settings of each server** (right-click → "Properties…"):

| Setting | Effect |
| --- | --- |
| Name, folder | Display and position in the tree. |
| PSM, SSH via PSMP or files only (SFTP via PSMP) | Connection type opened on double-click (at first, from the platform). |
| PSM component | Component to use (empty: deduced from the platform). |
| Target machine | Server to open the session on, for a domain account. |
| Default reason | Access reason sent automatically to the PVWA. |
| SFTP start folder | The terminal **and** the file browser open directly in this folder. |

<img src="captures/en/server-properties.png" alt="Properties of a server in “My servers”" width="540">

Without a PSMP in the Settings, the SSH and files-only types are greyed out, as elsewhere; an incorrect value
is reported in the window. A server whose account is no longer visible in CyberArk is greyed out.

### Export, import, share

Three buttons at the top of the tab, left of the safe button (KeePass databases):

- **Export** saves "My servers" to a `.json` file: folders (even empty ones), name, CyberArk account (ID), connection
  type, component, target machine, default reason, SFTP start folder. No password and no followed file. Handy to move
  to another computer or to pass your list on.
- **Import** reads an exported file (or a shared list) and sums up before adding: servers added, servers already
  there (same account, type, component, target machine and folder: skipped), folders created, servers opened on a
  target machine (check them: the machine comes from the file). Nothing is removed or changed in "My servers". A file
  created for another PVWA is refused: its account IDs designate other accounts there.
- **Shared lists** (two-people icon): a list of servers in a file on a network share, which the whole team opens and
  completes.
  - "Create a shared list…": choose the location (network share) and the name shown to everyone; "Open a shared
    list…": add a list created by a colleague. Open lists are shown at the top of the tab (after the KeePass databases),
    with their folders; the search filters them too.
  - **Add**: right-click a server or a folder of "My servers" → "Share in a list" (the folder is kept), or drag a
    server, a folder or an account of "Available" onto the list or one of its folders (confirmation). The default
    reason stays personal: it is never shared.
  - **Remove**: right-click → "Remove from the shared list…" (or `Del`), after confirmation.
  - **Use**: double-click to connect; right-click for the advanced connection, the password, the safe members or
    "Copy into My servers". Everyone connects with their own CyberArk rights: an account you cannot see in the
    CyberArk Vault is greyed out. The tooltip shows the account as CyberArk describes it, the target machine, who added the server
    and when.
  - **Target machine**: a shared server that opens a domain account on a machine not among the account's allowed
    machines in CyberArk asks for confirmation on the first connection (anyone with write access to the share can
    change the list). "Copy into My servers" names such servers and asks before copying them.
  - **List of another PVWA**: a list created for another CyberArk Vault is shown, but its servers neither open nor get
    copied, and nothing can be added to it: sign in to that PVWA to use it.
  - **History**: right-click → "History of changes…". The "Changes" tab lists who added, removed or restored what,
    and when; the "Versions" tab keeps a copy of the list at each revision (the last 100, in the `name.versions` folder
    next to the file). On that tab, "Restore this version…" puts the list back in that state for everyone, after
    confirmation; the restore is itself recorded, so it can be undone.
  - Everyone's changes add up: the file is re-read and changed exclusively (a computer writing at the same time waits
    for its turn), and the list shown updates when a colleague changes it (`F5` re-reads it too). "Close the list"
    removes it from your tab without touching the file.
  - Rights: those of the network share. Read-only, the list can still be used but not changed.

## 7. Emergency access outside CyberArk: KeePass databases

When CyberArk is unavailable, ZillaTerm opens your KeePass databases (`.kdbx`) and connects **directly** to the
servers, over SSH, remote desktop or VNC, or to their files only (SFTP, FTP, FTPS), with the accounts they hold.

> These connections **do not go through the PSM**: no recording, no CyberArk rules. Every database opening,
> connection and change is written to the local log `%APPDATA%\ZillaTerm\urgence.log`.

![Emergency access: KeePass database unlocked in "My servers"](captures/en/keepass-vault.png)

- **Without CyberArk**: on the sign-in screen, "Emergency access (KeePass)" opens the main window without the PVWA
  (only the KeePass databases are shown; the "Available" tab and the buttons specific to CyberArk are hidden). With
  CyberArk, the databases also appear at the top of "My servers". The tooltip of a session tab opened from a
  database says so: "Direct emergency access (KeePass): outside CyberArk, written to urgence.log".
- **Add a database**: safe button of the "My servers" tab (or right-click → "Add a KeePass database…"). The "Add a
  KeePass database" window reminds you in a banner that these connections are outside CyberArk; "Browse…" picks the
  `.kdbx` file, then the name and an optional key file.
- **Unlock**: double-click the database. Master password and/or key file (every KeePass format). "Remember the
  master password in the local vault" saves typing it again (see below).
- **Connect**: double-click an entry. The protocol comes from its address (`ssh://server:22`, `rdp://server`,
  `vnc://server`, `sftp://`, `ftp://`, `ftpes://`, `ftps://`, or `server:3389`), a "Protocol" / "Port" field or a
  tag (`ssh`, `rdp`, `vnc`, `sftp`, `ftp`, `ftpes`, `ftps`); otherwise ZillaTerm asks for the protocol. The
  entry's password is used directly; it is never shown or written to disk.
  - **SSH**: terminal + Files tabs. On the first connection, the fingerprint of the server key is to be compared
    with the one given by its administrator, in the same window as for the PSMP (see
    [section 4](#4-open-an-ssh-session-through-the-psmp)).
  - **Remote desktop**: the tab follows its size (remote desktop resolution) and offers "Full screen"
    (`Ctrl+Alt+Break` to come back), "Disconnect" and "Reconnect".
  - **VNC** (`vnc://server`, port 5900; `vnc://server:1` means display 1, port 5901): desktop in a tab, fitted to
    the window or at real size ("Fit"), "Ctrl+Alt+Del" (after confirmation: depending on the machine, it opens the
    security screen or restarts some virtual machine consoles), "Send clipboard" and "Copy remote text" buttons: the
    clipboard is only exchanged through these buttons. VNC password authentication (8 characters at most, a limit
    of the protocol) or no authentication. **VNC encrypts nothing**: a banner says so; keep it for a trusted
    network.
  - **Files** (`sftp://`, `ftp://`, `ftpes://` for FTP with explicit TLS, `ftps://` for implicit TLS, port 990): a
    status tab, without a terminal, and the files in the "Files" tab with the same functions (transfers checked by
    SHA-256, queue, history, editor, compare, live follow, permissions if the server accepts `SITE CHMOD`).
    Right-click → "Open the files (SFTP, FTP)" does the same for an SSH entry, over SFTP. With `ftp://`, TLS
    encryption is tried first; if the server does not offer it, ZillaTerm asks before connecting in clear text
    ("Connect without encryption", once per session) and a banner reminds you. `ftpes://` and `ftps://` never fall
    back to clear text. An FTPS certificate that Windows does not trust (self-signed…) is shown with its subject, its
    issuer, its validity dates and its SHA-256 fingerprint (with "Copy"), then remembered for that server if you
    accept it; if it changes later, the remembered and the new fingerprints are shown, and you must tick "I confirmed
    this change with the server's administrator".
- **Edit the database**: right-click → "New entry…", "Edit…" (`F2`), "Delete" (`Del`, into the database's recycle
  bin, after confirmation). The server address is required: an entry without an address (neither in the Address
  box nor in its custom fields) is not saved. The rest of the database (attachments, fields, settings) is kept; the
  previous version of an entry goes to its history, like in KeePass.
- **Lock**: right-click → "Lock". Databases also lock on sign-out, on exit and when **Windows is locked**.

**Local vault**: the master passwords you choose to remember are kept in `%APPDATA%\ZillaTerm\coffre-local.dat`,
encrypted with a password of your own (at least 8 characters) and tied to your Windows account. That password is
asked when you unlock a KeePass database whose password is remembered; "Later" (only offered at that moment) lets you
type the database password instead. If the local vault is not open, the database opens anyway, and the status bar
says its master password was not remembered. Manage it in the **Settings**, Security page: "Create…", "Unlock…",
"Change password…", "Delete now…"; these actions apply at once, without "Save". It locks on sign-out (so that
"Emergency access" never reopens the remembered databases without a password), on exit and when Windows is locked.

## Shortcuts

| Where | Action | Shortcut |
| --- | --- | --- |
| Everywhere | Reload the accounts from the PVWA | `F5` |
| Everywhere | Filter the accounts (in "My servers": search a server) | `Ctrl+F` |
| Everywhere | Next / previous session tab | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Everywhere | Close the session tab | `Ctrl+F4` or `Ctrl+Shift+W` |
| Everywhere | "Available", "My servers", "Files" tabs of the side panel | `Ctrl+1`, `Ctrl+2`, `Ctrl+3` |
| Everywhere | Settings | `Ctrl+,` |
| Outside the terminal | Quick connect (Home tab) | `Ctrl+K` |
| Outside the terminal | Move from the side panel to the session and back | `F6` |
| Outside the terminal | Collapse / expand the side panel | `Ctrl+B` (or double-click the splitter) |
| Lists and trees | Open the session | Double-click or `Enter` |
| Lists, trees, tabs | Right-click menu | Menu key or `Shift+F10` |
| Search | Clear the filter | `Esc` |
| Home | Remove a recent session from the list | `Del` |
| My servers | Rename / remove or delete | `F2` / `Del` |
| My servers | Pick several servers (then right-click to open them together) | `Ctrl+click`, `Shift+click`; `Esc` cancels |
| Terminal | Copy | Mouse selection, or `Ctrl+Shift+C` |
| Terminal | Paste | `Shift+Insert` or `Ctrl+Shift+V` (right-click with the Settings option) |
| Terminal | Menu: copy, paste, select all, search, save, clear the history, font size, tab actions | Right-click or Menu key (Shift+right-click with the paste option) |
| Terminal | Scrollback | Mouse wheel, scroll bar, `Shift+Page Up` / `Shift+Page Down`; "↓ Back to the end" |
| Terminal | Search (history included) | `Ctrl+Shift+F`, then `Enter` / `Shift+Enter` |
| Terminal | Save the content to a file | `Ctrl+Shift+S` |
| Terminal | Font size / default size | `Ctrl+wheel` / `Ctrl+0` |
| Comparison | Next / previous difference | `F7` / `Shift+F7` |
| SSH or remote desktop tab | Close | Tab cross or middle click |
| SSH or remote desktop tab | Reconnect, duplicate (another session on the same account or entry), detach (SSH), close, close the other tabs | Right-click on the tab |
| SSH tab | Detach to a separate window (another screen) | Drag the tab out of the window |
| SSH tab | Add to the parallel view, or take it out | Right-click the tab |
| Remote desktop | Full screen / back | `Ctrl+Alt+Break` |
| Files | Open the folder or edit the file / edit / rename / parent folder / delete / refresh | Double-click or `Enter` / `F4` / `F2` / `Backspace` / `Del` / `F5` |
| Files | Sort by a column, then reverse | Click its header |
| KeePass database | Connect / edit / delete an entry | Double-click or `Enter` / `F2` / `Del` |

In a terminal, `Ctrl+K`, `Ctrl+B` and `F6` are sent to the server (`F6` to applications such as mc); `Ctrl+Tab`,
`Ctrl+F4`, `Ctrl+Shift+W` and `Ctrl+1/2/3` stay with ZillaTerm.

**Keyboard and accessibility**: the toolbar can be reached with `Tab` (the focus is visible), every menu and every
window has its access keys (`Alt` + underlined letter, without duplicates, in English, French and Italian), and the
menu of an item of "My servers" opens at the same place with a right-click, `Shift+F10` or the Menu key. Password
boxes (sign-in, KeePass database, local vault) warn when Caps Lock is on. Screen readers announce the names of list
and tree items and of icon buttons, the status bar messages and connection errors. In high contrast mode, the
interface takes the Windows system colours and follows their changes.

## Settings and configuration file

<img src="captures/en/settings.png" alt="Settings" width="480">

Settings toolbar button → "Settings…" (or `Ctrl+,`). The window, which can be resized, is organized in pages:
General, CyberArk, Terminal, Files, Security. "Save" applies the settings; an incorrect value shows the page of the
field concerned, with the cursor in it. Options with a side effect say so under their box ("⚠ Effect: …").

| Page | Setting | Purpose | Default |
| --- | --- | --- | --- |
| General | Interface language | Français, English, Italiano or system language; applied after signing out or at the next start | Windows language (English if it is not translated) |
| General | Central file | Team environment file on a network share, read at each start; its changes are shown before being applied (see [Shared environment](#shared-environment)) | empty |
| General | Look for a new version at startup | One request to GitHub at most once a day; a link in the status bar when a newer version exists (the "About" window recalls this setting) | no |
| CyberArk | Keep the PVWA session open | Light request every 4 minutes; paused while Windows is locked; ⚠ the PVWA session no longer closes by itself after inactivity | yes |
| CyberArk | Default PSMP, port | PSM for SSH server; when set (or a PSMP by domain), Unix accounts open over SSH by default (as files only for an "SFTP" platform); without any PSMP, SSH and SFTP are disabled | empty, 22 |
| CyberArk | PSMP by domain | Other PSMPs (address, port, domain served); each server goes through the one of the domain closest to its own (see [PSMP by domain](#psmp-by-domain)); "Which PSMP for the server" to check | none |
| CyberArk | Windows accounts component | PSM component of Windows accounts (domain or local) without a component remembered for their platform, for example `WIN-PSM` | empty = `PSM-RDP` |
| CyberArk | Component per platform | Platform (PVWA ID, for example `WinDomain`) / PSM component table: "Add a component", "Remove the line", editable cells; takes precedence over the Windows accounts component. "Remember this component for platform" (connection window) adds a line to it | empty |
| Terminal | SSH in ZillaTerm | Built-in terminal and Files tab; otherwise Windows Terminal | yes |
| Terminal | Terminal colours, font | Palette (Campbell, One Half, Solarized…) and font size of the SSH terminals | Campbell, 14 |
| Terminal | Warn before pasting several lines | Preview and confirmation when the shell would run the lines one by one | yes |
| Terminal | Confirm before closing a connected session | SSH, remote desktop, VNC; "Don't ask again" in the confirmation unticks this setting | yes |
| Terminal | Right-click in the terminal pastes the clipboard | Shift+right-click then opens the menu; ⚠ a stray right-click sends the clipboard to the shell | no |
| Terminal | Follow the terminal folder | Allows setting up folder tracking in the shell; ⚠ a command is added to `PROMPT_COMMAND` | yes |
| Files | File upload | Protocol tried first (SFTP or SCP); if the server refuses it, the other one takes over | SFTP |
| Files | Offer a single .tar.gz archive | Sending a single archive is offered from this number of files dropped at once | yes, 200 |
| Files | Follow in an independent session | Following a file (tail -f) opens its own SFTP connection (one more PSMP session) | no |
| Files | Text editor | Program opened by "Edit" in the Files tab | Notepad |
| Files | Comparison tool | Program offered in the comparison window, with its arguments (`{0}` = left file, `{1}` = right file) | none |
| Security | Local vault | Remembered KeePass master passwords: "Create…", "Unlock…", "Change password…", "Delete now…"; these actions apply at once, without "Save" | — |
| Security | Accepted server keys | Table of the fingerprints checked and accepted (server, type, fingerprint): PSMP, direct SSH and FTPS certificates of KeePass entries. "Forget the selected keys" removes the selected rows on save; the key will be asked again at the next connection | — |
| Settings button menu | Debug log | How connections unfold, in a file, without secrets (see [Security](#security)); "Show the debug log file" opens it in Explorer | no |

### Shared environment

To give ZillaTerm to a colleague with the team's configuration (PVWA address, sign-in method, default and
by-domain PSMPs, Windows accounts component and per-platform components, shared lists, PSMP keys, a few options),
with nothing personal and no password:

1. **Export**: "Settings" button → "Export the environment…" saves `ZillaTerm.env.json`.
2. **Next to the executable**: put this file next to `ZillaTerm.exe` (for example in the same zip). At start, when
   it is new or has changed, ZillaTerm offers it before the sign-in screen.
3. **Import**: "Settings" button → "Import an environment…", or "Import an environment…" on the sign-in screen.
4. **Central file**: Settings › General › "Central file" (a file on a network share, which can also be set in the
   environment itself). It is read at each start: when you change it, everyone sees the changes at their next start.

Each time, a window shows what will change ("old value → new value") and the SHA-256 fingerprint of the file; "Do not
apply" is the default. The PVWA and the PSMPs receive your CyberArk password: when the file changes their address or
adds a server key, "I have checked…" must be ticked before applying. A server key already accepted on the computer is
never replaced by a file (it is reported). An invalid file (http address, wrong component name…) is refused as a
whole. A file already offered is offered again only when it has changed. A setting left empty on the exporting PC is
not exported: it clears nothing on the importing one. Paths (shared lists, central file) are full: `C:\…` or
`\\server\…`. Your user name, "My servers" and your
recent sessions are never touched; shared lists are added without removing yours.

All preferences are saved in `%APPDATA%\ZillaTerm\settings.json`: language, PVWA address, sign-in method and user
name, the settings above, "My servers", their folders and the files followed on them (paths), recent sessions,
location of the KeePass databases and of their key files, and of the open shared lists, window position and size,
width and state of the side panel. This file contains **no password, token or private key**. To
start from scratch, close the application and delete it. It is written to a temporary file first, then put in place,
the previous one being kept as `settings.json.bak`: if the file ever cannot be read, it is set aside (never
overwritten), the backup is used and a message says so. ZillaTerm opens only once per Windows session: two
instances would overwrite each other's settings. The transfer history of the Files tab is next to it, in
`transfers.json` (file names and paths, SHA-256 checksums, never their content).

### Moving from CyberArkTerm to ZillaTerm

CyberArkTerm is now called ZillaTerm. The first time `ZillaTerm.exe` starts, the `%APPDATA%\CyberArkTerm` folder
(settings, "My servers", local vault, transfer history, emergency access log) is copied to `%APPDATA%\ZillaTerm`; the
old folder is kept: delete it, along with `CyberArkTerm.exe`, once you have moved to ZillaTerm. A
`CyberArkTerm.env.json` file next to the executable is still read, shared lists stay readable by both versions and the
master passwords of the local vault stay available. The old temporary folder (`%TEMP%\CyberArkTerm`) is emptied over the
following starts (files older than one day). Both versions cannot be open at the same time. CyberArkTerm reports the
first ZillaTerm version but cannot download it itself (renamed repository): download it once from the releases page.

## Security

- **HTTPS required** to the PVWA; certificate validation is never disabled.
- **No secret on disk**: CyberArk password, session token, MFA key and PSMP password stay in memory for the session.
  The PVWA session is closed (`Logoff`) on exit.
- PVWA session opened with `concurrentSession`: your PVWA web session, if any, is not closed.
- **Copying a password**: the PVWA response is read into a buffer wiped afterwards and decoded without going through
  a string; the password goes straight to the Windows clipboard, marked to be excluded from the history (`Win+V`),
  from cross-device sync and from clipboard monitoring tools, then cleared after 20 s if it is still there (tried
  again every second if another application keeps the clipboard open), and on sign-out, exit and Windows lock. It is
  never shown nor written to the debug log. A response that is not the password (HTML maintenance page, redirect to
  an SSO sign-in page, empty response) is refused instead of being copied.
- **Adding an account**: the password is read from the masked box without going through a string, sent once to the
  PVWA over HTTPS, then wiped from memory; it is neither saved nor written to the debug log.
- **Temporary folder**: `%TEMP%\ZillaTerm`, reserved for your Windows account (permissions limited to you alone);
  if it belongs to another account (TEMP pointing to a shared folder), `%LOCALAPPDATA%\ZillaTerm\Temp` is used
  instead. The paths below are relative to this folder.
- **PSM sessions**: the PVWA's RDP file (one-time PSM token) is written to the temporary folder for `mstsc`, which
  checks its signature, then deleted after 60 s or on exit.
- **Simultaneous typing** (parallel view): off every time the view opens, shown by the amber "ON (n)" button, an
  amber banner and frame that name the sessions concerned; excluded sessions are marked "excluded", an added session
  is not included by default, and pasting several lines into several
  sessions asks first. Each session stays a separate PSMP session, recorded as usual.
- **Pasting several lines** into a terminal whose shell would run them one by one: preview and confirmation before
  sending (setting on by default).
- **Confirmations**: buttons with an explicit verb in the application language, "Cancel" by default, server, account
  or safe named; closing a connected session is confirmed (setting on by default).
- **File comparison**: contents read in memory and wiped when the window closes; only the copies given to an
  external tool go through the disk (temporary folder, `compare`), deleted when the window closes and at the next
  start.
- **New version**: no request to the Internet without your action or the Settings option (off by default); only the
  addresses of the project repository are followed, the archive is kept only when its SHA-256 checksum is the one of
  `SHA256SUMS.txt`, and nothing is installed or started.
- **PSMP host keys pinned** on first use: the fingerprint is to be compared before accepting ("Cancel connection" by
  default); a changed key is flagged by a banner and only replaces the old one after a confirmation box is ticked
  (the same for servers reached in emergency access and for FTPS certificates). Accepted keys can be reviewed and
  forgotten in Settings › Security.
- **PVWA session keep-alive**: it avoids the idle timeout; nothing is sent while Windows is locked, and the option
  can be turned off in the Settings if your policy requires it.
- **KeePass databases**:
  - the master password is never saved, except in the local vault if you ask for it: Argon2id (64 MiB, 3 passes)
    then AES-256-GCM, key derivation settings authenticated, all protected by DPAPI (Windows account);
  - in memory, the database key and the entry passwords stay masked and are only revealed when connecting;
    databases lock on sign-out, on exit and when Windows is locked;
  - safe saving: the file is read again, the change is applied to its current version (changes made elsewhere are
    kept), the decrypted result is checked, a `.bak` copy is kept and the file is replaced in one step; an entry
    changed elsewhere in the meantime is not overwritten;
  - direct remote desktop: the password is only passed to the Remote Desktop control (no file, no credential
    manager), with network level authentication (NLA) and a warning if the server is not recognized;
  - VNC: the protocol encrypts neither the screen, nor the keystrokes, nor the clipboard (permanent banner); the
    password is not sent as is (challenge-response of the protocol); the clipboard is only exchanged on a click;
    the screen size announced by the server is bounded (8,192 pixels per side);
  - FTP: TLS is tried first, clear text only after your agreement (permanent banner), never for `ftpes://` and
    `ftps://`; under TLS, transfers are encrypted too (`PROT P`);
  - FTPS certificate: one that Windows trusts is accepted; otherwise its SHA-256 fingerprint is shown and pinned on
    the first agreement (like an SSH host key), a change is reported; refused, the connection stops before the
    user name is sent;
  - file names with control characters are refused (no FTP command injection);
  - `urgence.log`: date, Windows account, computer, action, database, entry, target; never a password. Every read of an
    entry's password is written there, reconnections and the Files tab's SFTP / SCP connections included; if the
    log cannot be written, the connection is not opened.
- **Debug log**, off by default (Settings button menu): `%LOCALAPPDATA%\ZillaTerm\debug.log`, 5 MB at most plus
  one `.1` generation. It records how PVWA, PSM, remote desktop and SSH connections unfold: request addresses and
  statuses, .rdp file settings, Remote Desktop control events and codes, SSH server version and algorithms, errors;
  for each refused upload protocol, the step (connection, scp command, file announcement), the server's answer and
  the protocol that took over. It contains server and account names, but **never** a password, session token, PSM session request (`PSM@…` masked), signature, request header or
  body, nor session content. The status bar shows it while it is on. Read it before passing it on, and delete it
  once the problem is solved.
- **Edited files**: the local copy opened in the editor is stored in the temporary folder (`edit`) and deleted when the
  SSH tab closes; a warning shows if changes were not sent back.
- **No command injection**: SCP paths and start folders are quoted for the remote shell; `ssh` / Windows Terminal
  arguments are validated and passed without a shell.
- CSV export protected against Excel formula injection.
- **Environment files** (`ZillaTerm.env.json`): no password or personal data, only known fields are read. A file
  is never applied without your consent: changes and SHA-256 fingerprint shown, a box to tick when the PVWA or a PSMP
  address changes or a server key is added. It never replaces a server key already accepted; https PVWA address
  required; a file over 1 MB is refused.
- **Server files and shared lists**: no password or token, only server, account and safe names, account IDs and
  connection settings (the default reason is never shared). They grant no access: everyone connects with their
  own CyberArk rights, and the tooltip shows the account as the CyberArk Vault describes it. A target machine coming from a
  shared list and not allowed for the account by CyberArk is confirmed before the first connection. The author
  written in the journal (CyberArk account and Windows account) is declarative: the network share's audit is
  authoritative. A file larger than 8 MB is refused.
- PSM and PSMP sessions opened by ZillaTerm are standard CyberArk sessions: they are recorded and audited by the
  PSM like the ones opened from the PVWA.

To report a vulnerability, see [SECURITY.md](../SECURITY.md) (private reporting, no public issue).

## How it works

### PVWA API calls

| Call | Purpose |
| --- | --- |
| `POST /PasswordVault/API/auth/{CyberArk\|LDAP\|RADIUS\|Windows}/Logon` | Sign in |
| `GET /PasswordVault/API/Accounts?offset=…&limit=1000` | Paged account list |
| `POST /PasswordVault/API/Accounts/{id}/PSMConnect` | RDP file of the PSM session |
| `POST /PasswordVault/API/Accounts` | Creates an account in a safe ("Add an account") |
| `POST /PasswordVault/API/Accounts` (once per line) | Imports accounts from a CSV |
| `PATCH` / `DELETE /PasswordVault/API/Accounts/{id}` | Edits (changed fields only) and deletes an account |
| `POST /PasswordVault/API/Accounts/{id}/Verify`, `/Change`, `/Reconcile` | Operations requested from the CPM |
| `POST /PasswordVault/API/Accounts/{id}/Password/Retrieve` | Copies the password (reason, ticket; "copy" usage in the audit) |
| `POST` / `PUT` / `DELETE /PasswordVault/API/Safes/{safe}/Members[/{member}]` | Adds a safe member, sets its rights, removes it |
| `GET /PasswordVault/API/Safes/{safe}/Members?offset=…&limit=1000` | Members of a safe and their rights ("Safe members" window) |
| `POST /PasswordVault/API/Users/Secret/SSHKeys/Cache` | Temporary "MFA caching" SSH key (if enabled) |
| `GET /PasswordVault/API/Accounts?offset=0&limit=1` | Session keep-alive (every 4 minutes) |
| `POST /PasswordVault/API/Auth/Logoff` | Sign out |

### Shared lists

JSON file (`"format": "CyberArkTerm.SharedServers"`, version 1): name, source PVWA, revision, folders, servers
(with who added them and when) and journal of changes (the last 1,000). Each change opens the file exclusively
(other computers retry for 5 s), re-reads it, copies the current revision to
`name.versions\name.r00012.20261006-101500.json` (revision and the date it was saved, 100 versions kept), applies
the change, increases the revision, records who, when and what, then rewrites the file (put back as it was if the
write fails). The display follows the file's changes (`FileSystemWatcher`) and re-reads it with `F5`. The "My
servers" export has the same format with `"format": "CyberArkTerm.Servers"`, without revision or journal.

### KeePass databases

Native reading and writing (no KeePass installed) of the **KDBX 3.1 and 4.x** formats: AES-256 or ChaCha20
encryption, AES-KDF (processor AES instructions) or Argon2d / Argon2id key derivation, XML 1.0 / 2.0 key files, 32
bytes, 64 hexadecimal characters or any file. The rewritten file keeps the original version, encryption and key
derivation, with new seeds on every save, the key derivation one included (as KeePass does: a derived key captured
once does not decrypt later versions). The test databases (`tests/ZillaTerm.Core.Tests/KeePass/Vaults`) come from
KeePassXC and pykeepass, and files written by ZillaTerm were checked in both tools.

### VNC sessions

Built-in client (RFB protocol 3.3, 3.7 and 3.8, RFC 6143; a newer server, such as RealVNC 4 or 5, is answered in
3.8), nothing to install: "none" or "VNC password" authentication (when the server offers both, the password if the
entry has one, otherwise none) (the protocol's DES, implemented in ZillaTerm because the Windows FIPS mode can forbid DES), Raw,
CopyRect and Hextile encodings, screen size changes, 32-bit pixels. The keyboard is sent as X11 "keysyms" (AltGr
characters are sent as characters), the wheel as buttons 4 and 5.

### FTP / FTPS files sessions

FluentFTP library (MIT licence). Passive mode: `PASV` over IPv4, the data connection always going to the server
itself (the address given in the reply is ignored: a server cannot point it at another machine), `EPSV` over IPv6;
binary, `PBSZ 0` and `PROT P` under TLS; certificate
checked by Windows, otherwise pinned (`ftps://server:port` among the accepted server keys, in Settings › Security). FTP has
no standard checksum: each upload is read back from the server and compared by SHA-256. Partial reads (`REST`) for
compare and live follow. After an interrupted transfer, the connection is reopened and the incomplete file deleted.
Overwriting a file writes it in place: it keeps its permissions. Symbolic links: the first 40 of a folder are
resolved (one round trip each); beyond that, a link shows as a file and opening it enters the folder if it is one.

### Remote desktop sessions

PSM sessions open with the RDP file returned by `PSMConnect`, handed as is to Remote Desktop Connection (`mstsc`):
it checks its signature and handles a desktop as well as a remote application (RemoteApp). The debug log records its
structure (token, signature and arguments masked). Versions 0.4 to 0.6 opened these sessions in a tab: a PSM that
only accepts remote applications did not work well there (place and size of the windows on the server, mouse), hence
the return to `mstsc`.

Remote desktop tabs (direct remote desktop from KeePass databases) host the Windows ActiveX control (`mstscax.dll`, the
most recent `MsRdpClient` class available), set up as a direct connection: network level authentication (NLA),
warning if the server is not recognized, redirections off except the clipboard. The remote desktop resolution
follows the tab size. Session ends and connection errors are explained in the tab with the Windows message and codes
(reason, extended reason). An integration test (`rdp-integration` workflow) opens a real session on the CI machine.

**One thread per remote desktop connection.** The control, its window and its events live on a separate thread (STA,
with its own message loop); the interface never waits for it. The tab holds a window of the interface thread, in
which that thread places the control's window. Before releasing the control, it takes the window out: a slow
disconnection or release no longer freezes the application. If the thread stops responding for 5 s, the tab bar says
so, and the rest of the application stays usable. Limit: Windows shares keyboard and mouse input between a window
and the windows it contains, even from another thread; a control that is stuck for good can still hold up a click in
its area or a focus change.

### PSMP sessions

Each SSH tab opens up to three connections to the PSMP, with the same login `<you>@<account>[#domain]@<target>`: the
terminal, the SFTP connection of the Files tab, and an SCP connection on the first SCP upload (chosen in Settings, or
taking over from a refused SFTP upload). Each one is a PSMP
session, recorded by the PSM. Sending to the server (typing, terminal size) and closing connections happen off the
interface thread, in order: a server or PSMP that stops reading does not freeze the application.

### Following the terminal folder

When an SSH session opens (if the option is on), ZillaTerm waits for the target server's shell to show its prompt
(up to 60 s: the PSMP sometimes takes several seconds to reach the target), then sends it a one-line command,
preceded by a space so it stays out of the history (bash, or zsh with `HIST_IGNORE_SPACE`). Nothing is sent if you
already started typing; the command can be sent again without duplicate effect (the "Follow" box):

- sets `PROMPT_COMMAND` (bash) or `precmd` (zsh) to emit the standard **OSC 7** sequence with the current folder at
  each prompt;
- with tcsh, the `cwdcmd` alias (only if it is not already defined), which emits the same sequence at each folder
  change;
- if a start folder is configured, a `cd` to that folder;
- erases the typed command so it does not stay on screen.

The built-in terminal decodes the OSC 7 sequence and the Files tab moves to that folder.

Every shell reads the command without error: each part runs only in the shell family it is written for. With csh,
ksh, sh or fish, following is not set up and nothing stays on screen.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| "The PVWA has no connection component “PSM-RDP” for this account" (`EPVWA093E Failed to get the relevant connection component`) | The account's platform uses a component with another name (for example `WIN-PSM`): the one offered by the PVWA "Connect" button, or the name after `/c` in a `psm /u … /a … /c …` command. Enter it in "Component"; "Remember this component for platform" is ticked for the next connections. |
| "TLS connection refused: this computer does not trust the PVWA certificate" | The certificate (or its issuing authority) is not in the workstation's Windows store. |
| "The PVWA must be reached over HTTPS" | Type the address without `http://` (or with `https://`). |
| "Your CyberArk session has expired" | PVWA inactivity timeout reached: sign in again. |
| "Password" → "Copy the password…": "The PVWA refused: … “Retrieve accounts” …" | Missing right on the safe, or reason / ticket required by the platform: type it. With dual control, make the request in the PVWA. |
| "Verify / Change / Reconcile": "The PVWA refused: … “Initiate CPM account management operations” …" | Ask for this right on the safe; "Safe members" shows your rights. |
| "Add an account": "The PVWA refused: your account needs the “Add accounts” right…" | Ask for this right on the safe (and "Update account content" to give the password), or create the account without a password. "Safe members" shows your rights. |
| "Safe members": "Your account cannot see the members of this safe" | The PVWA requires the "View Safe Members" right on the safe: ask a manager of the safe. |
| "Connection component … is not configured for platform …" | Choose the right component in "Advanced connection", tick "Remember" for the platform. |
| "You must specify a reason…" | Enter a reason in the window that opens (or a default reason in the server's properties, in "My servers"). |
| The account does not show up | You lack the "List accounts" permission on its safe, or the list needs reloading (`F5`). |
| The PSMP password is asked for each tab | MFA caching is not enabled on the PVWA: expected behavior (once per tab). |
| The Files tab shows "SFTP connection failed" | SFTP is not allowed on the PSMP or for this account: ask your CyberArk team. |
| An upload shows "SFTP (SCP refused)" or "SCP (SFTP refused)" | The PSMP or the server refused that protocol for this file: the other one took over and the file was checked as usual. The summary gives the server's answer. A PSMP that refuses SCP for a platform (error `118E Selected component PSMP-SCP does not contain the target settings definitions…` in its logs) lacks the PSMP-SCP connection component: your CyberArk team can add it to the platform, otherwise uploads go over SFTP. |
| The browser does not follow `cd` | The remote shell is not bash, zsh or tcsh (or tcsh already has its own `cwdcmd` alias), the option is off in Settings, or the prompt was not recognized: tick "Follow the terminal folder" again at the shell prompt. |
| "The key of the PSMP has changed" warning | Only continue ("I confirmed this change with the CyberArk team" box, then "Replace the key and connect") if your CyberArk team confirms a server change; otherwise, cancel and alert them. |
| "Connection cancelled: the server key was not accepted." | The fingerprint window was cancelled or closed: connect again and accept the key after comparing its fingerprint. |
| "Wrong master password or key file." | Check the password and the key file; a database protected by a YubiKey is not supported. |
| The KeePass database asks for the password despite "Remember" | Local vault locked ("Later" when unlocking) or master password changed elsewhere: type it, it is remembered again. |
| "The local vault file is damaged or was created by another Windows account." | The local vault does not follow a change of computer or account: delete it in the Settings and create it again. |
| "The entry … was changed or deleted in the KeePass database in the meantime" | Someone changed the same entry elsewhere: the database is reloaded, make the change again. |
| A Unix account opens with PSM, not SSH | PSMP address not set in the Settings, or account not recognized as Unix: right-click → "Connect over SSH (PSMP)". |
| An account opens as files only, not in a terminal | Its platform name contains "SFTP": right-click → "Connect over SSH (PSMP)", or "Properties…" in "My servers" to change the connection type. |
| "The shared list is being modified by someone else" | Another computer has been writing the list for more than 5 seconds, or keeps the file open: try again in a moment. |
| "you do not have the right to modify this file (rights of the network share)" | The share is read-only for you: ask its owner for write access. The list can still be used. |
| A shared list shows "(unreadable)" | Share unreachable or damaged file: the tooltip gives the error. If the file is damaged, copy the most recent version of the `name.versions` folder in its place. |
| Understanding a connection failure | Settings → Debug log, reproduce the problem, then Settings → "Show the debug log file". |
| A direct remote desktop tab (KeePass) shows "Remote Desktop control error" | Report the code shown (if the Remote Desktop control is missing from the computer, the connection goes through `mstsc`). |
| VNC: "The VNC server offers no authentication supported by ZillaTerm…" | The server requires its vendor's own authentication (Windows account, VeNCrypt encryption…): enable "VNC password" authentication on the server. |
| VNC: "No VNC answer from the server within 30 seconds" | Wrong port (5900 + display number) or a service other than VNC at this address. |
| FTP: "The FTP server does not offer encryption (TLS), required by this entry" | The server does not accept TLS: use `ftp://` (clear-text connection after confirmation) or SFTP if available. |
| FTPS: the file list does not show or a transfer times out | A firewall blocks the server's passive ports, or the server requires TLS session reuse on data connections (`522`, for example vsftpd's `require_ssl_reuse`): see the server's administrator. |
