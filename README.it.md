<img src="docs/icone.png" alt="" width="72" align="right">

# CyberArkTerm

[Français](README.md) · [English](README.en.md) · **Italiano**

[![build](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml/badge.svg)](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/muller-camille/CyberArkTerm)](https://github.com/muller-camille/CyberArkTerm/releases/latest)

**Client Windows multisessione per CyberArk.** CyberArkTerm si collega al tuo PVWA, elenca gli account a cui
hai accesso e apre le sessioni con un doppio clic: desktop remoto tramite **PSM**, oppure terminale SSH
tramite **PSM for SSH (PSMP)** con un **browser dei file** integrato per inviare file al server.

![Sessione SSH tramite il PSMP, con la scheda File che segue la cartella del terminale](docs/captures/it/main-window.png)

> Le schermate provengono da un ambiente dimostrativo (dati fittizi).

## Indice

- [Funzionalità](#funzionalità)
- [Installazione](#installazione)
- [Primi passi](#primi-passi)
- [Scorciatoie](#scorciatoie)
- [Impostazioni e file di configurazione](#impostazioni-e-file-di-configurazione)
- [Sicurezza](#sicurezza)
- [Funzionamento tecnico](#funzionamento-tecnico)
- [Risoluzione dei problemi](#risoluzione-dei-problemi)
- [Sviluppo](#sviluppo)
- [Limiti e sviluppi futuri](#limiti-e-sviluppi-futuri)
- [Licenza](#licenza)

## Funzionalità

| | |
| --- | --- |
| **Accesso a CyberArk** | Autenticazione CyberArk, LDAP, RADIUS (challenge / OTP compresi) o Windows (sessione corrente). |
| **Disponibili** | Tutti gli account visibili nel vault, raggruppati per safe, piattaforma o tipo di destinazione, con ricerca immediata. |
| **I miei server** | I tuoi server di lavoro, organizzati in cartelle e sottocartelle, ognuno con la propria configurazione. |
| **Sessioni PSM** | Desktop remoto tramite il PSM (come il pulsante «Connect» del PVWA): componente, macchina di destinazione, motivo, ticket. |
| **Sessioni SSH (PSMP)** | Terminale integrato in una scheda (compatibile xterm: colori, vim, less, top…), autenticazione MFA. |
| **Scheda File** | Browser SFTP del server: `ls`, navigazione, `rm`, invio di file per trascinamento in SCP, segue la cartella del terminale. |
| **Home** | Connessione rapida (digita un server, Invio), sessioni recenti. |
| **Esportazione** | Elenco degli account in CSV, apribile direttamente in Excel (separatore secondo la regione di Windows). |
| **Lingue** | Interfaccia in italiano, inglese e francese: lingua di Windows per impostazione predefinita, modificabile in qualsiasi momento. |

## Installazione

### Scaricare l'eseguibile

1. Apri l'[ultima versione](https://github.com/muller-camille/CyberArkTerm/releases/latest) nelle
   *Releases* del repository.
2. Scarica **`CyberArkTerm-<versione>-win-x64.zip`** ed estrailo (l'impronta SHA256 è in `SHA256SUMS.txt`).
3. Avvia `CyberArkTerm.exe`: un solo file, nessun runtime da installare, nessun diritto di amministratore
   richiesto.

Versioni di sviluppo: l'eseguibile di ogni compilazione è disponibile anche come artefatto
`CyberArkTerm-win-x64` nella scheda [Actions](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml).

L'eseguibile non è firmato: al primo avvio Windows SmartScreen può mostrare un avviso
(«Ulteriori informazioni» → «Esegui comunque»).

### Requisiti

**Postazione di lavoro**

- Windows 10 o 11 (x64).
- Il client Desktop remoto (`mstsc`, presente di default) per le sessioni PSM.
- Facoltativo: Windows Terminal e il «Client OpenSSH» di Windows, solo se scegli di aprire l'SSH fuori da
  CyberArkTerm.

**Lato CyberArk**

- PVWA **v10 o superiore** (API REST `/PasswordVault/API/...`), raggiungibile in **HTTPS** con un
  certificato considerato attendibile dalla postazione.
- Permesso **List accounts** sui safe interessati: l'applicazione mostra solo ciò che l'API ti consente di
  vedere.
- PSM configurato sulle piattaforme da usare (componenti `PSM-RDP`, `PSM-SSH`…).
- Per l'SSH: un **PSM for SSH (PSMP)**, con SFTP consentito per la scheda File (e SCP per l'invio in SCP).
- Facoltativo: **MFA caching** attivato sul PVWA, per non reinserire password e MFA sul PSMP.

## Primi passi

### 1. Accedere al vault

<img src="docs/captures/it/sign-in.png" alt="Finestra di accesso" width="440">

Inserisci l'indirizzo del PVWA (`pvwa.miodominio.local` è sufficiente: `https://` e `/PasswordVault` vengono
aggiunti), scegli il metodo di autenticazione, poi nome utente e password. Se il server RADIUS pone una
domanda (codice OTP), la finestra la mostra e attende la tua risposta.

Indirizzo, metodo e nome utente vengono memorizzati; **la password mai**.

L'elenco in basso a sinistra cambia la lingua dell'interfaccia (Français, English, Italiano); la finestra si
riapre subito nella lingua scelta, conservando l'indirizzo e il nome utente inseriti.

### 2. Trovare un account: scheda «Disponibili»

- La casella di ricerca filtra su tutti i campi (server, utente, safe, piattaforma, dominio…), anche con più
  parole (`prd sql`).
- «Raggruppa per» ordina gli account per safe, piattaforma o tipo di destinazione.
- La scheda «Tutti gli account» mostra lo stesso elenco come tabella ordinabile, esportabile in CSV.
- Nella scheda Home, la **connessione rapida** trova un server mentre digiti: Invio per connetterti.

### 3. Aprire una sessione PSM (desktop remoto)

Fai doppio clic sull'account (oppure Invio, oppure il pulsante «Connetti»). CyberArkTerm richiede la
connessione al PVWA e apre il Desktop remoto sul PSM, esattamente come il pulsante «Connect» del PVWA.

- **Componente PSM**: dedotto dalla piattaforma (`PSM-RDP` per Windows, `PSM-SSH` per Unix e rete,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Seleziona «Memorizza questo componente» per conservarlo per
  tutta la piattaforma.
- **Account di dominio**: la finestra chiede la macchina di destinazione, precompilata con le macchine
  autorizzate dell'account.
- **Motivo e ticket**: se il PVWA rifiuta la richiesta (motivo obbligatorio, componente non configurato…), il
  suo messaggio viene mostrato e puoi correggere e riprovare.
- Il pulsante «Avanzata…» (o clic destro → «Connessione avanzata…») apre questa finestra su richiesta.

### 4. Aprire una sessione SSH tramite il PSMP

Imposta una volta l'indirizzo del PSMP nelle **Impostazioni**. Poi clic destro → «Connetti in SSH» (o il
pulsante «SSH»). Con l'opzione «Doppio clic su un account Unix: connetti in SSH tramite PSMP» basta un
doppio clic.

La sessione si apre **in una scheda di CyberArkTerm**, con l'identificativo PSMP standard
`<tu>@<account di destinazione>[#dominio]@<server di destinazione>`. I nomi utente che contengono spazi
(`Mario Rossi`, `Admin Locale`) sono accettati.

<img src="docs/captures/it/psmp-authentication.png" alt="Domanda di autenticazione posta dal PSMP" width="640">

- **Autenticazione**: se il PVWA fornisce una chiave «MFA caching», non viene posta alcuna domanda.
  Altrimenti vengono mostrate le domande del PSMP (password, codice MFA); la password viene riutilizzata per
  le connessioni SFTP e SCP della stessa scheda, mai salvata.
- **Chiave del PSMP**: alla prima connessione ne viene mostrata l'impronta SHA256, da accettare; se in seguito
  cambia, viene mostrato un avviso.
- **Terminale**: la selezione copia, il clic destro incolla, la rotellina scorre la cronologia, AltGr funziona
  sulle tastiere internazionali. Chiudi la scheda con la croce o con un clic centrale.

### 5. Sfogliare e inviare file: scheda «File»

All'apertura di una sessione SSH, la scheda **File** appare sul lato e segue la scheda SSH attiva.

- **Barra del percorso**: percorso corrente, modificabile (digita un percorso e premi Invio). Doppio clic su
  una cartella per entrarvi, `..` per risalire, pulsanti «cartella superiore» e «cartella home».
- **Inviare file**: trascinali da Esplora file sull'elenco (o il pulsante «Invia»). Invio in **SCP** per
  impostazione predefinita (SFTP in opzione), cartelle comprese; conferma prima di sovrascrivere un file
  esistente.
- **Eliminare**: selezione poi Canc (o clic destro → «Elimina (rm)»), con conferma. Le cartelle devono essere
  vuote.
- Inoltre: nuova cartella, download, copia del percorso, visualizzazione dei file nascosti.
- **Segui la cartella del terminale**: se la casella è selezionata, ogni `cd` nel terminale sposta il browser
  nella stessa cartella (vedi [Funzionamento tecnico](#funzionamento-tecnico)).

### 6. Organizzare i server: scheda «I miei server»

![I miei server organizzati in cartelle](docs/captures/it/my-servers.png)

- **Aggiungere** un account: clic destro in «Disponibili» → «Aggiungi ai miei server» e poi la cartella
  desiderata, oppure trascina l'account sulla scheda «I miei server», oppure il pulsante «Aggiungi» della
  barra degli strumenti.
- **Cartelle**: clic destro → nuova cartella o sottocartella, rinomina, elimina; trascina server e cartelle per
  spostarli.
- **Configurazione propria di ogni server** (clic destro → «Proprietà…»):

| Impostazione | Effetto |
| --- | --- |
| Nome, cartella | Visualizzazione e posizione nell'albero. |
| PSM o SSH tramite PSMP | Tipo di connessione aperto con il doppio clic. |
| Componente PSM | Componente da usare (vuoto: dedotto dalla piattaforma). |
| Macchina di destinazione | Server su cui aprire la sessione per un account di dominio. |
| Motivo predefinito | Motivo di accesso inviato automaticamente al PVWA. |
| Cartella SFTP iniziale | Il terminale **e** il browser dei file si aprono direttamente in questa cartella. |

Un server il cui account non è più visibile in CyberArk appare in grigio.

## Scorciatoie

| Dove | Azione | Scorciatoia |
| --- | --- | --- |
| Ovunque | Ricaricare gli account dal PVWA | `F5` |
| Ovunque | Filtrare gli account | `Ctrl+F` |
| Elenchi e alberi | Aprire la sessione | Doppio clic o `Invio` |
| Ricerca | Cancellare il filtro | `Esc` |
| I miei server | Rinominare / rimuovere o eliminare | `F2` / `Canc` |
| Terminale | Copiare | Selezione con il mouse, o `Ctrl+Maiusc+C` |
| Terminale | Incollare | Clic destro, `Maiusc+Ins` o `Ctrl+Maiusc+V` |
| Terminale | Cronologia | Rotellina, `Maiusc+Pag su` / `Maiusc+Pag giù` |
| Scheda SSH | Chiudere | Croce della scheda o clic centrale |
| File | Aprire / cartella superiore / eliminare / aggiornare | `Invio` / `Backspace` / `Canc` / `F5` |

## Impostazioni e file di configurazione

![Impostazioni](docs/captures/it/settings.png)

| Impostazione | Ruolo | Predefinito |
| --- | --- | --- |
| Lingua dell'interfaccia | Français, English, Italiano o lingua del sistema; applicata dopo la disconnessione o al prossimo avvio | lingua di Windows (inglese se non è tradotta) |
| Indirizzo e porta PSMP | Server PSM for SSH; vuoto = SSH disattivato | vuoto, 22 |
| Doppio clic Unix = SSH | Apre gli account Unix in SSH anziché in PSM | no |
| SSH in CyberArkTerm | Terminale e scheda File integrati; altrimenti Windows Terminal | sì |
| Segui la cartella del terminale | Consente di attivare il monitoraggio della cartella nella shell | sì |
| Invio dei file | SCP o SFTP | SCP |
| Chiavi PSMP accettate | Impronte memorizzate (pulsante «Dimentica le chiavi») | — |
| Componenti memorizzati | Componente PSM scelto per piattaforma (pulsante «Dimentica») | — |

Tutte le preferenze sono salvate in `%APPDATA%\CyberArkTerm\settings.json`: lingua, indirizzo del PVWA,
metodo e nome utente di accesso, impostazioni qui sopra, «I miei server» e le loro cartelle, sessioni
recenti. Questo file **non contiene password, token né chiavi private**. Per ripartire da zero, chiudi
l'applicazione ed eliminalo.

## Sicurezza

- **HTTPS obbligatorio** verso il PVWA; la convalida dei certificati non viene mai disattivata.
- **Nessun segreto su disco**: password CyberArk, token di sessione, chiave MFA e password PSMP restano in
  memoria per la durata della sessione. Disconnessione dal PVWA (`Logoff`) alla chiusura.
- Sessione PVWA aperta con `concurrentSession`: l'eventuale sessione web del PVWA non viene chiusa.
- **File RDP** (token PSM monouso) scritti in `%TEMP%\CyberArkTerm` ed eliminati dopo 60 s o alla chiusura.
- **Chiavi host del PSMP fissate** al primo utilizzo, con avviso in caso di modifica.
- **Nessuna iniezione di comandi**: percorsi SCP e cartelle iniziali protetti tra apici per la shell remota;
  argomenti `ssh` / Windows Terminal convalidati e passati senza shell.
- Esportazione CSV protetta contro l'iniezione di formule Excel.
- Le sessioni PSM e PSMP aperte da CyberArkTerm sono sessioni CyberArk standard: vengono registrate e
  verificate dal PSM come quelle aperte dal PVWA.

Per segnalare una vulnerabilità, vedi [SECURITY.md](SECURITY.md) (segnalazione privata, nessuna issue
pubblica).

## Funzionamento tecnico

### API del PVWA utilizzate

| Chiamata | Uso |
| --- | --- |
| `POST /PasswordVault/API/auth/{CyberArk\|LDAP\|RADIUS\|Windows}/Logon` | Apertura della sessione |
| `GET /PasswordVault/API/Accounts?offset=…&limit=1000` | Elenco paginato degli account |
| `POST /PasswordVault/API/Accounts/{id}/PSMConnect` | File RDP della sessione PSM |
| `POST /PasswordVault/API/Users/Secret/SSHKeys/Cache` | Chiave SSH temporanea «MFA caching» (se attivata) |
| `POST /PasswordVault/API/Auth/Logoff` | Chiusura della sessione |

### Sessioni PSMP

Ogni scheda SSH apre fino a tre connessioni al PSMP, con lo stesso identificativo
`<tu>@<account>[#dominio]@<destinazione>`: il terminale, la connessione SFTP della scheda File e una
connessione SCP al primo invio in SCP. Ognuna è una sessione PSMP, registrata dal PSM.

### Monitoraggio della cartella del terminale

All'apertura di una sessione SSH (se l'opzione è attiva), CyberArkTerm invia alla shell un comando di una
riga, preceduto da uno spazio per non finire nella cronologia:

- definizione di `PROMPT_COMMAND` (bash) o `precmd` (zsh) che emette la sequenza standard **OSC 7** con la
  cartella corrente a ogni prompt;
- se è configurata una cartella iniziale, un `cd` verso quella cartella;
- cancellazione del comando digitato, perché non resti sullo schermo.

Il terminale integrato decodifica la sequenza OSC 7 e la scheda File si posiziona nella cartella indicata.

## Risoluzione dei problemi

| Sintomo | Causa probabile e soluzione |
| --- | --- |
| «Connessione TLS rifiutata: il certificato del PVWA non è considerato attendibile» | Il certificato (o l'autorità che lo ha emesso) non è nell'archivio Windows della postazione. |
| «Il PVWA deve essere raggiunto in HTTPS» | Inserisci l'indirizzo senza `http://` (o con `https://`). |
| «La sessione CyberArk è scaduta» | Timeout di inattività del PVWA superato: accedi di nuovo. |
| «Connection component … is not configured for platform …» | Scegli il componente corretto in «Connessione avanzata», seleziona «Memorizza» per la piattaforma. |
| «You must specify a reason…» | Inserisci un motivo nella finestra che si apre (o un motivo predefinito nelle proprietà del server). |
| L'account non compare | Non hai il permesso «List accounts» sul suo safe, oppure l'elenco va ricaricato (`F5`). |
| La password PSMP viene chiesta per ogni scheda | MFA caching non attivato sul PVWA: comportamento normale (una volta per scheda). |
| La scheda File indica «Connessione SFTP impossibile» | SFTP non è consentito sul PSMP o per questo account: rivolgiti al team CyberArk. |
| Il browser non segue i `cd` | La shell remota non è bash o zsh, oppure l'opzione è disattivata nelle Impostazioni. |
| Avviso «la chiave del PSMP è cambiata» | Prosegui solo se il team CyberArk conferma una modifica del server. |

## Sviluppo

### Struttura

| Progetto | Ruolo |
| --- | --- |
| `src/CyberArkTerm.Core` | Logica senza interfaccia, multipiattaforma: client dell'API PVWA, classificazione degli account, emulatore di terminale xterm, connessioni PSMP e browser SFTP/SCP (SSH.NET), cartelle di «I miei server», preferenze. |
| `src/CyberArkTerm.App` | Applicazione WPF: finestre, schede, controllo terminale, avvio di `mstsc`, icona (`Assets`). |
| `tests/CyberArkTerm.Core.Tests` | Test xUnit di Core (falso PVWA HTTP, terminale, PSMP, cartelle, traduzioni…). |

Dipendenza esterna: [SSH.NET](https://github.com/sshnet/SSH.NET) (licenza MIT).

### Traduzioni

I testi dell'interfaccia si trovano in `src/CyberArkTerm.Core/Localization/CoreStrings*.resx` e
`src/CyberArkTerm.App/Localization/Strings*.resx`: inglese nel file neutro, poi `.fr` e `.it`.
Le classi `*.Designer.cs` sono generate da Visual Studio (`PublicResXFileCodeGenerator`); un test verifica che
ogni lingua abbia tutte le chiavi, gli stessi parametri `{0}` e gli stessi tasti di scelta `_`.
Per aggiungere una lingua: copia i file `.resx` con il nuovo codice (`.de.resx`…), traducili, poi aggiungi il
codice a `UiLanguage.Supported`.

### Compilare e testare

Con l'[SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet test CyberArkTerm.sln
dotnet run --project src/CyberArkTerm.App
```

Il progetto si compila anche su Linux o macOS (`EnableWindowsTargeting`); l'applicazione funziona solo su
Windows.

### Pubblicare l'eseguibile

```powershell
# Autonomo (~65 MB): niente da installare sulla postazione
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

# Leggero: richiede il «.NET Desktop Runtime 10» sulla postazione
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
```

La CI ([`.github/workflows/build.yml`](.github/workflows/build.yml)) esegue i test e pubblica l'eseguibile
autonomo come artefatto `CyberArkTerm-win-x64` per ogni pull request e ogni push su `main`.

### Pubblicare una versione

Da GitHub: **Actions → release → Run workflow** su `main`, indicando il numero `X.Y.Z`; oppure invia un tag
`vX.Y.Z` su `main`. Il workflow [`release.yml`](.github/workflows/release.yml) esegue i test, compila
l'eseguibile con quel numero di versione, crea il tag se non esiste e pubblica la *Release* GitHub con lo zip
e `SHA256SUMS.txt`. Le note di versione vengono lette da `docs/releases/vX.Y.Z.md` se il file esiste.

## Limiti e sviluppi futuri

**Limiti attuali**

- **Privilege Cloud** (accesso tramite CyberArk Identity) e **SAML** non sono supportati.
- L'API Accounts non indica quali componenti PSM offre una piattaforma: il componente viene dedotto, poi può
  essere memorizzato.
- Le sessioni PSM (RDP) si aprono nella finestra Desktop remoto di Windows, non in una scheda.
- Il monitoraggio della cartella del terminale richiede bash o zsh sul server.
- PSM Gateway (HTML5), doppio controllo (dual control) e accesso esclusivo non sono supportati.

**Sviluppi futuri**

- Eseguibile firmato e programma di installazione MSI.
- Sessioni RDP in schede integrate.
- Più PVWA (profili di connessione), Privilege Cloud.

## Licenza

[MIT](LICENSE) © 2026 muller-camille
