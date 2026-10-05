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
| **Sessioni PSM** | Desktop remoto tramite il PSM (come il pulsante «Connect» del PVWA), in Connessione Desktop remoto di Windows: componente, macchina di destinazione, motivo, ticket. |
| **Sessioni SSH (PSMP)** | Terminale integrato in una scheda (compatibile xterm: colori, vim, less, top…), autenticazione MFA. |
| **Scheda File** | Browser SFTP del server: `ls`, navigazione, `rm`, invio di file per trascinamento in SCP, verifica SHA-256 di ogni file trasferito, modifica nel tuo editor di testo, permessi (`chmod`), segue la cartella del terminale. |
| **Accesso di emergenza (KeePass)** | Senza CyberArk: archivi KeePass (.kdbx) in «I miei server», connessioni SSH e desktop remoto dirette, creazione e modifica delle voci, registro locale. |
| **Sessione PVWA mantenuta** | Una richiesta leggera ogni 4 minuti evita la scadenza mentre lavori (sospesa quando Windows è bloccato). |
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
- Il client Desktop remoto di Windows (presente di default): Connessione Desktop remoto (`mstsc`) per le sessioni
  PSM, il suo controllo integrato per il desktop remoto diretto degli archivi KeePass.
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
- Il pulsante «Esporta» della barra degli strumenti salva in CSV gli account mostrati (filtrati dalla ricerca).
- **Membri di un safe**: clic destro su un account (o su un safe quando gli account sono raggruppati per safe, o
  su un server di «I miei server») → «Membri del safe». La finestra elenca gli utenti e i gruppi del safe con i
  loro diritti (elencare, usare, recuperare, aggiungere account, aggiornare, eliminare, gestire i membri…), indica
  chi può **aggiungere account** e mostra tutti i diritti del membro selezionato. Il PVWA fornisce questo elenco
  solo a un account con il diritto «View Safe Members» sul safe. `Ctrl+A` e poi `Ctrl+C` copia la tabella. Con il
  diritto «Gestire i membri del safe», i pulsanti «Aggiungi un membro…», «Modifica i diritti…» (o doppio clic) e
  «Rimuovi…» gestiscono i membri: nome, tipo (utente o gruppo), directory («Vault» o il dominio LDAP), eventuale data
  di fine e i 22 diritti, raggruppati come nel PVWA.
- **Aggiungere un account**: clic destro su un account (o su un safe quando gli account sono raggruppati per safe)
  → «Aggiungi un account al safe…». Safe, piattaforma, indirizzo e utente sono obbligatori; dominio di accesso,
  nome dell'account, password, macchine consentite e gestione da parte del CPM sono facoltativi. L'account cliccato
  fa da modello (safe, piattaforma, dominio). L'account viene creato con i diritti della tua sessione: serve il
  diritto «Aggiungere account» sul safe e, in genere, «Aggiornare il contenuto degli account» per fornire la
  password. L'elenco viene poi ricaricato e il nuovo account selezionato.
- **Importare account (CSV)**: pulsante «Importa» della barra degli strumenti, o clic destro su un account o un
  safe → «Importa account (CSV)…». Una piccola finestra chiede il file («Salva un modello…» fornisce un esempio), il
  safe e la piattaforma predefiniti e riassume cosa verrà creato; nulla viene inviato prima di «Importa». Colonne
  obbligatorie: indirizzo e utente (più safe e piattaforma, altrimenti i valori predefiniti); facoltative: nome,
  dominio, password, macchine consentite, gestione CPM (sì/no), motivo. Separatore `;`, `,` o tabulazione, nomi delle
  colonne in italiano, francese o inglese; un file prodotto da «Esporta» si può reimportare. Una seconda finestra crea
  poi gli account riga per riga e mostra lo stato di ciascuna (creato, rifiutato con il messaggio del PVWA, non
  importato, non inviato; «Interrompi» disponibile). Alla fine propone di salvare il risultato in CSV (senza le
  password). Le password del file non vengono mai mostrate; elimina il file dopo l'importazione.
- **Modificare / eliminare un account**: clic destro → «Modifica l'account…» (piattaforma, indirizzo, utente,
  dominio, nome, macchine consentite, gestione da parte del CPM; vengono inviati solo i campi modificati) oppure
  «Elimina l'account…» (dopo conferma). Diritti «Aggiornare le proprietà degli account» ed «Eliminare account».
- **Stato della password (CPM)**: il tooltip di un account indica se è gestito dal CPM e la data dell'ultimo
  cambio, dell'ultima verifica e dell'ultima riconciliazione; un **⚠** segnala un account la cui ultima operazione
  del CPM non è riuscita.
- **Clic destro → «Password»** (account di «Disponibili» e server di «I miei server»):
  - «Verifica», «Cambia…», «Riconcilia…» chiedono l'operazione al CPM (conferma per cambiare e riconciliare;
    diritto «Avviare le operazioni CPM»). Il CPM la esegue poi: `F5` per vedere il nuovo stato.
  - «Copia la password…»: motivo e ticket se la piattaforma li richiede, poi la password viene copiata negli
    appunti per 20 secondi, **senza essere mostrata** (diritto «Recuperare gli account»; il recupero viene
    registrato nell'audit del vault).
- Nella scheda Home, la **connessione rapida** trova un server mentre digiti: Invio per connetterti.

### 3. Aprire una sessione PSM (desktop remoto)

Fai doppio clic sull'account (oppure Invio, oppure il pulsante «Connetti»); un account Unix si apre in SSH tramite
il PSMP quando il suo indirizzo è impostato (vedi 4.), e «Connessione avanzata…» permette allora di scegliere il
PSM. CyberArkTerm richiede la connessione al PVWA e apre la sessione in **Connessione Desktop remoto** di Windows
(`mstsc`), esattamente come il pulsante «Connect» del PVWA: il file RDP del PVWA le viene passato così com'è. Un
componente che apre un'applicazione remota (RemoteApp) apre le sue finestre sul desktop del computer.

- **Componente PSM**: dedotto dalla piattaforma (`PSM-RDP` per Windows, `PSM-SSH` per Unix e rete,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Seleziona «Memorizza questo componente» per conservarlo per
  tutta la piattaforma. Il tuo PVWA può chiamare i suoi componenti in un altro modo (ad esempio `WIN-PSM`):
  inserisci il nome proposto dal suo pulsante «Connect»; l'elenco propone poi i componenti già usati, quello
  della piattaforma per primo.
- **Account di dominio**: la finestra chiede la macchina di destinazione, precompilata con le macchine
  autorizzate dell'account.
- **Motivo e ticket**: se il PVWA rifiuta la richiesta (motivo obbligatorio, componente non configurato…), il
  suo messaggio viene mostrato e puoi correggere e riprovare.
- Il pulsante «Avanzata…» (o clic destro → «Connessione avanzata…») apre questa finestra su richiesta.

### 4. Aprire una sessione SSH tramite il PSMP

Imposta una volta l'indirizzo del PSMP nelle **Impostazioni**: gli account Unix si aprono allora in SSH per
impostazione predefinita (doppio clic o Invio). Per un altro account, clic destro → «Connetti in SSH» (o il
pulsante «SSH»).

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
- **Scaricare trascinando**: trascina file o cartelle dall'elenco verso Esplora file o il desktop. Nulla viene
  scaricato durante il trascinamento: al rilascio, una finestra mostra l'avanzamento (Annulla lo interrompe), poi
  Esplora file copia i file dove li hai rilasciati. I nomi Unix vengono resi validi per Windows (`\`, `:`, `..`,
  `CON`… sostituiti), senza mai scrivere fuori dalla cartella di rilascio; la cartella temporanea del download
  viene poi eliminata.
- **Verifica dei trasferimenti (SHA-256)**: ogni file inviato o scaricato viene verificato. All'invio (SCP o
  SFTP), il file locale viene sottoposto a hash, poi il file arrivato sul server viene riletto via SFTP e
  sottoposto a hash. Al download, i dati ricevuti dal server vengono sottoposti a hash, poi il file scritto sul
  computer viene riletto. La barra di stato conferma «✓ identico su entrambi i lati»; «Checksum…» mostra per
  ogni file la dimensione, le due somme e il risultato, e copia le somme nel formato di `sha256sum -c` per
  riverificare sul server. Se un file è diverso, l'errore viene mostrato e il dettaglio si apre; un download per
  trascinamento fallisce invece di consegnare una copia errata. Un file che non può essere riletto (permessi) è
  segnalato «non verificato». La rilettura di un invio raddoppia il volume scambiato con il server.
- **Eliminare**: selezione poi Canc (o clic destro → «Elimina (rm)»), con conferma. Le cartelle devono essere
  vuote.
- **Modificare un file**: selezionalo, poi `F4` (o clic destro → «Modifica», o il pulsante matita). Il file si
  apre nell'editor di testo scelto nelle Impostazioni (Blocco note per impostazione predefinita). A ogni
  salvataggio, CyberArkTerm propone di rinviarlo al server: invio in SFTP, permessi del file conservati. Se il
  file è cambiato sul server dopo l'apertura, un avviso chiede conferma prima di sovrascriverlo.
- **Permessi**: clic destro → «Permessi…» (o il pulsante lucchetto). Caselle lettura / scrittura / esecuzione
  per proprietario, gruppo e altri, bit speciali (setuid, setgid, sticky) e valore ottale (`644`, `1777`…), per
  uno o più elementi. Per una cartella, «Applica anche al contenuto» propaga i permessi a sottocartelle e file;
  per impostazione predefinita, l'esecuzione (x) viene data solo alle cartelle e ai file già eseguibili. I link
  simbolici non vengono seguiti e il proprietario non viene modificato.
- Inoltre: nuova cartella, download, copia del percorso, visualizzazione dei file nascosti.
- **Segui la cartella del terminale**: se la casella è selezionata, ogni `cd` nel terminale sposta il browser
  nella stessa cartella (vedi [Funzionamento tecnico](#funzionamento-tecnico)). Dopo `sudo -i` o `su`,
  riseleziona la casella al prompt della shell per riattivare il monitoraggio nella nuova shell.

### 6. Organizzare i server: scheda «I miei server»

![I miei server organizzati in cartelle](docs/captures/it/my-servers.png)

- **Aggiungere** un account: clic destro in «Disponibili» → «Aggiungi ai miei server» e poi la cartella
  desiderata, oppure trascina l'account sulla scheda «I miei server», oppure il pulsante «Aggiungi» della
  barra degli strumenti.
- **Aggiungere una connessione recente**: clic destro nelle sessioni recenti della home → «Aggiungi ai miei
  server» e poi la cartella desiderata. Il server mantiene il tipo di connessione (PSM o SSH), il componente PSM
  e la macchina di destinazione usati.
- **Cartelle**: clic destro → nuova cartella o sottocartella, rinomina, elimina; trascina server e cartelle per
  spostarli.
- **Cercare**: campo in cima alla scheda (o `Ctrl+F` nella scheda). Filtra i server per nome, server, utente,
  cartella, componente, macchina di destinazione, e le voci degli archivi KeePass sbloccati; le cartelle dei
  risultati vengono espanse. `Invio` o `↓` seleziona il primo risultato, `Esc` cancella.
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

### 7. Accesso di emergenza fuori da CyberArk: archivi KeePass

Quando CyberArk non è disponibile, CyberArkTerm apre i tuoi archivi KeePass (`.kdbx`) e si connette
**direttamente** ai server, in SSH o in desktop remoto, con gli account che contengono.

> Queste connessioni **non passano dal PSM**: nessuna registrazione, nessuna regola CyberArk. Ogni apertura di
> archivio, connessione e modifica è annotata nel registro locale `%APPDATA%\CyberArkTerm\urgence.log`.

![Accesso di emergenza: archivio KeePass sbloccato in «I miei server»](docs/captures/it/keepass-vault.png)

- **Senza CyberArk**: nella schermata di accesso, «Accesso di emergenza (KeePass)» apre la finestra principale
  senza PVWA (sono mostrati solo gli archivi KeePass). Con CyberArk, gli archivi compaiono anche in cima a «I miei
  server».
- **Aggiungere un archivio**: pulsante cassaforte della scheda «I miei server» (o clic destro → «Aggiungi un
  archivio KeePass…»): file `.kdbx`, nome, eventuale file chiave.
- **Sbloccare**: doppio clic sull'archivio. Password principale e/o file chiave (tutti i formati di KeePass).
  «Memorizza la password principale nel vault locale» evita di ridigitarla (vedi sotto).
- **Connettersi**: doppio clic su una voce. Il protocollo viene dal suo indirizzo (`ssh://server:22`,
  `rdp://server`, `server:3389`), da un campo «Protocol» / «Port» o da un'etichetta `ssh` / `rdp`; altrimenti
  CyberArkTerm chiede SSH o desktop remoto. La password della voce è usata direttamente (schede terminale + File
  in SSH, scheda desktop remoto in RDP); non è mai mostrata né scritta su disco. La scheda desktop remoto ne
  segue la dimensione (risoluzione del desktop remoto) e propone «Schermo intero» (`Ctrl+Alt+Pausa` per
  tornare), «Disconnetti» e «Riconnetti».
- **Modificare l'archivio**: clic destro → «Nuova voce…», «Modifica…» (`F2`), «Elimina» (`Canc`, nel cestino
  dell'archivio). Il resto dell'archivio (allegati, campi, impostazioni) è conservato; la versione precedente di
  una voce va nella sua cronologia, come in KeePass.
- **Bloccare**: clic destro → «Blocca». Gli archivi si bloccano anche alla disconnessione, alla chiusura e al
  **blocco di Windows**.

**Vault locale**: le password principali che scegli di memorizzare sono conservate in
`%APPDATA%\CyberArkTerm\coffre-local.dat`, cifrato con una tua password (chiesta quando sblocchi un archivio KeePass la
cui password è memorizzata, «Più tardi» per digitare invece la password dell'archivio) e legato al tuo account
Windows. Gestione nelle **Impostazioni**: crea, sblocca, cambia
password, elimina.

## Scorciatoie

| Dove | Azione | Scorciatoia |
| --- | --- | --- |
| Ovunque | Ricaricare gli account dal PVWA | `F5` |
| Ovunque | Filtrare gli account (in «I miei server»: cercare un server) | `Ctrl+F` |
| Elenchi e alberi | Aprire la sessione | Doppio clic o `Invio` |
| Ricerca | Cancellare il filtro | `Esc` |
| I miei server | Rinominare / rimuovere o eliminare | `F2` / `Canc` |
| Terminale | Copiare | Selezione con il mouse, o `Ctrl+Maiusc+C` |
| Terminale | Incollare | Clic destro, `Maiusc+Ins` o `Ctrl+Maiusc+V` |
| Terminale | Cronologia | Rotellina, `Maiusc+Pag su` / `Maiusc+Pag giù` |
| Scheda SSH o Desktop remoto | Chiudere | Croce della scheda o clic centrale |
| Scheda SSH o Desktop remoto | Riconnettere, duplicare (altra sessione sullo stesso account o sulla stessa voce), chiudere, chiudere le altre schede | Clic destro sulla scheda |
| Desktop remoto | Schermo intero / ritorno | `Ctrl+Alt+Pausa` |
| File | Aprire / modificare / cartella superiore / eliminare / aggiornare | `Invio` / `F4` / `Backspace` / `Canc` / `F5` |
| Archivio KeePass | Connettere / modificare / eliminare una voce | Doppio clic o `Invio` / `F2` / `Canc` |

## Impostazioni e file di configurazione

![Impostazioni](docs/captures/it/settings.png)

| Impostazione | Ruolo | Predefinito |
| --- | --- | --- |
| Lingua dell'interfaccia | Français, English, Italiano o lingua del sistema; applicata dopo la disconnessione o al prossimo avvio | lingua di Windows (inglese se non è tradotta) |
| Indirizzo e porta PSMP | Server PSM for SSH; se impostato, gli account Unix si aprono in SSH per impostazione predefinita; vuoto = SSH disattivato | vuoto, 22 |
| Mantenere aperta la sessione PVWA | Richiesta leggera ogni 4 minuti; sospesa quando Windows è bloccato | sì |
| Vault locale | Password principali KeePass memorizzate: crea, sblocca, cambia password, elimina | — |
| Registro di debug | Menu del pulsante Impostazioni: svolgimento delle connessioni in un file, senza segreti (vedi [Sicurezza](#sicurezza)); «Mostra il file del registro» lo apre in Esplora risorse | no |
| SSH in CyberArkTerm | Terminale e scheda File integrati; altrimenti Windows Terminal | sì |
| Segui la cartella del terminale | Consente di attivare il monitoraggio della cartella nella shell | sì |
| Invio dei file | SCP o SFTP | SCP |
| Editor di testo | Programma aperto da «Modifica» nella scheda File | Blocco note |
| Chiavi PSMP accettate | Impronte memorizzate (pulsante «Dimentica le chiavi») | — |
| Componenti memorizzati | Componente PSM scelto per piattaforma (pulsante «Dimentica») | — |

Tutte le preferenze sono salvate in `%APPDATA%\CyberArkTerm\settings.json`: lingua, indirizzo del PVWA,
metodo e nome utente di accesso, impostazioni qui sopra, «I miei server» e le loro cartelle, sessioni
recenti, posizione degli archivi KeePass e dei loro file chiave. Questo file **non contiene password, token né
chiavi private**. Per ripartire da zero, chiudi
l'applicazione ed eliminalo.

## Sicurezza

- **HTTPS obbligatorio** verso il PVWA; la convalida dei certificati non viene mai disattivata.
- **Nessun segreto su disco**: password CyberArk, token di sessione, chiave MFA e password PSMP restano in
  memoria per la durata della sessione. Disconnessione dal PVWA (`Logoff`) alla chiusura.
- Sessione PVWA aperta con `concurrentSession`: l'eventuale sessione web del PVWA non viene chiusa.
- **Copia di una password**: la risposta del PVWA viene letta in un buffer cancellato subito dopo e decodificata
  senza passare da una stringa; la password va direttamente negli appunti di Windows, contrassegnata per essere
  esclusa dalla cronologia (`Win+V`), dalla sincronizzazione tra dispositivi e dagli strumenti di monitoraggio degli
  appunti, poi cancellata dopo 20 s se è ancora presente, oltre che alla disconnessione, alla chiusura e al blocco
  di Windows. Non viene mai mostrata né scritta nel registro di debug.
- **Aggiunta di un account**: la password viene letta dal campo mascherato senza passare da una stringa, inviata una
  sola volta al PVWA in HTTPS, poi cancellata dalla memoria; non viene né salvata né scritta nel registro di debug.
- **Sessioni PSM**: il file RDP del PVWA (token PSM monouso) viene scritto in `%TEMP%\CyberArkTerm` per
  `mstsc`, che ne verifica la firma, poi eliminato dopo 60 s o alla chiusura.
- **Chiavi host del PSMP fissate** al primo utilizzo, con avviso in caso di modifica (lo stesso per i server
  raggiunti in accesso di emergenza).
- **Mantenimento della sessione PVWA**: evita la scadenza per inattività; non viene inviato nulla mentre Windows
  è bloccato, e l'opzione si disattiva nelle Impostazioni se la tua politica lo richiede.
- **Archivi KeePass**:
  - la password principale non è mai salvata, tranne nel vault locale se lo chiedi: Argon2id (64 MiB, 3 passate)
    poi AES-256-GCM, parametri di derivazione autenticati, il tutto protetto da DPAPI (account Windows);
  - in memoria, la chiave dell'archivio e le password delle voci restano mascherate e sono rivelate solo al
    momento della connessione; archivi bloccati alla disconnessione, alla chiusura e al blocco di Windows;
  - salvataggio sicuro: il file viene riletto, la modifica è applicata alla sua versione attuale (le modifiche
    fatte altrove sono conservate), il risultato decifrato è verificato, una copia `.bak` è conservata e il file
    è sostituito in un solo passo; una voce modificata altrove nel frattempo non viene sovrascritta;
  - desktop remoto diretto: la password è passata solo al controllo Desktop remoto (nessun file, nessun gestore
    credenziali), con autenticazione a livello di rete (NLA) e avviso se il server non è riconosciuto;
  - `urgence.log`: data, account Windows, computer, azione, archivio, voce, destinazione; mai una password.
- **Registro di debug**, disattivato per impostazione predefinita (menu del pulsante Impostazioni):
  `%LOCALAPPDATA%\CyberArkTerm\debug.log`, al massimo 5 MB più una generazione `.1`. Registra lo svolgimento delle
  connessioni PVWA, PSM, desktop remoto e SSH: indirizzi e stati delle richieste, impostazioni del file .rdp,
  eventi e codici del controllo Desktop remoto, errori. Contiene nomi di server e di account, ma **mai** password,
  token di sessione, richiesta di sessione PSM (`PSM@…` mascherata), firma, intestazione o corpo delle richieste,
  né il contenuto delle sessioni. La barra di stato lo segnala finché è attivo. Rileggilo prima di trasmetterlo,
  ed eliminalo una volta risolto il problema.
- **File modificati**: la copia locale aperta nell'editor si trova in `%TEMP%\CyberArkTerm\edit` e viene
  eliminata alla chiusura della scheda SSH; un avviso segnala le modifiche non rinviate.
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
| `POST /PasswordVault/API/Accounts` | Creazione di un account in un safe («Aggiungi un account») |
| `POST /PasswordVault/API/Accounts` (una volta per riga) | Importazione di account da un CSV |
| `PATCH` / `DELETE /PasswordVault/API/Accounts/{id}` | Modifica (solo i campi cambiati) ed eliminazione di un account |
| `POST /PasswordVault/API/Accounts/{id}/Verify`, `/Change`, `/Reconcile` | Operazioni richieste al CPM |
| `POST /PasswordVault/API/Accounts/{id}/Password/Retrieve` | Copia della password (motivo, ticket; uso «copy» nell'audit) |
| `POST` / `PUT` / `DELETE /PasswordVault/API/Safes/{safe}/Members[/{membro}]` | Aggiunta, diritti e rimozione di un membro del safe |
| `GET /PasswordVault/API/Safes/{safe}/Members?offset=…&limit=1000` | Membri di un safe e i loro diritti (finestra «Membri del safe») |
| `POST /PasswordVault/API/Users/Secret/SSHKeys/Cache` | Chiave SSH temporanea «MFA caching» (se attivata) |
| `GET /PasswordVault/API/Accounts?offset=0&limit=1` | Mantenimento della sessione (ogni 4 minuti) |
| `POST /PasswordVault/API/Auth/Logoff` | Chiusura della sessione |

### Archivi KeePass

Lettura e scrittura native (senza KeePass installato) dei formati **KDBX 3.1 e 4.x**: cifratura AES-256 o
ChaCha20, derivazione della chiave AES-KDF (istruzioni AES del processore) o Argon2d / Argon2id, file chiave XML
1.0 / 2.0, 32 byte, 64 caratteri esadecimali o file qualsiasi. Il file riscritto mantiene la versione, la cifratura
e la derivazione della chiave originali, con nuovi semi a ogni salvataggio. Gli archivi di test
(`tests/CyberArkTerm.Core.Tests/KeePass/Vaults`) provengono da KeePassXC e pykeepass, e i file scritti da
CyberArkTerm sono stati verificati in entrambi gli strumenti.

### Sessioni Desktop remoto

Le sessioni PSM si aprono con il file RDP restituito da `PSMConnect`, passato così com'è a Connessione Desktop
remoto (`mstsc`): ne verifica la firma e gestisce sia il desktop sia l'applicazione remota (RemoteApp). Il registro
di debug ne annota la struttura (token, firma e argomenti mascherati). Le versioni dalla 0.4 alla 0.6 aprivano
queste sessioni in una scheda: un PSM che accetta solo l'applicazione remota non vi funzionava bene (posizione e
dimensione delle finestre sul server, mouse), da qui il ritorno a `mstsc`.

Le schede Desktop remoto (desktop remoto diretto degli archivi KeePass) ospitano il controllo ActiveX di Windows
(`mstscax.dll`, la classe `MsRdpClient` più recente disponibile), impostato come una connessione diretta:
autenticazione a livello di rete (NLA), avviso se il server non è riconosciuto, reindirizzamenti disattivati tranne
gli appunti. La risoluzione del desktop remoto segue la dimensione della scheda. Le chiusure di sessione e gli
errori di connessione sono spiegati nella scheda con il messaggio e i codici di Windows (motivo, motivo esteso).
Un test di integrazione (workflow `rdp-integration`) apre una vera sessione sul computer di CI.

**Un thread per connessione desktop remoto.** Il controllo, la sua finestra e i suoi eventi vivono su un thread a
parte (STA, con il proprio ciclo di messaggi); l'interfaccia non lo attende mai. La scheda contiene una finestra del
thread dell'interfaccia, in cui quel thread colloca la finestra del controllo. Prima di rilasciare il controllo, ve
la toglie: una disconnessione o un rilascio lento non blocca più l'applicazione. Se il thread non risponde per 5 s,
la barra della scheda lo segnala, e il resto dell'applicazione resta utilizzabile. Limite: Windows condivide
tastiera e mouse tra una finestra e quelle che contiene, anche di un altro thread; un controllo bloccato
definitivamente può ancora trattenere un clic nella sua area o un cambio di focus.

### Sessioni PSMP

Ogni scheda SSH apre fino a tre connessioni al PSMP, con lo stesso identificativo
`<tu>@<account>[#dominio]@<destinazione>`: il terminale, la connessione SFTP della scheda File e una
connessione SCP al primo invio in SCP. Ognuna è una sessione PSMP, registrata dal PSM.
Gli invii al server (digitazione, dimensione del terminale) e la chiusura delle connessioni avvengono fuori dal
thread dell'interfaccia, in ordine: un server o un PSMP che non legge più non blocca l'applicazione.

### Monitoraggio della cartella del terminale

All'apertura di una sessione SSH (se l'opzione è attiva), CyberArkTerm attende che la shell del server di
destinazione mostri il prompt (fino a 60 s: il PSMP a volte impiega diversi secondi a raggiungere la
destinazione), poi le invia un comando di una riga, preceduto da uno spazio per non finire nella cronologia.
Non viene inviato nulla se hai già iniziato a digitare; il comando può essere reinviato senza effetti doppi
(casella «Segui»):

- definizione di `PROMPT_COMMAND` (bash) o `precmd` (zsh) che emette la sequenza standard **OSC 7** con la
  cartella corrente a ogni prompt;
- se è configurata una cartella iniziale, un `cd` verso quella cartella;
- cancellazione del comando digitato, perché non resti sullo schermo.

Il terminale integrato decodifica la sequenza OSC 7 e la scheda File si posiziona nella cartella indicata.

## Risoluzione dei problemi

| Sintomo | Causa probabile e soluzione |
| --- | --- |
| «Il PVWA non ha un componente di connessione «PSM-RDP» per questo account» (`EPVWA093E Failed to get the relevant connection component`) | La piattaforma dell'account usa un componente con un altro nome (ad esempio `WIN-PSM`): quello proposto dal pulsante «Connect» del PVWA, o il nome dopo `/c` in un comando `psm /u … /a … /c …`. Inseriscilo in «Componente»; «Memorizza questo componente per la piattaforma» è selezionata per le connessioni successive. |
| «Connessione TLS rifiutata: il certificato del PVWA non è considerato attendibile» | Il certificato (o l'autorità che lo ha emesso) non è nell'archivio Windows della postazione. |
| «Il PVWA deve essere raggiunto in HTTPS» | Inserisci l'indirizzo senza `http://` (o con `https://`). |
| «La sessione CyberArk è scaduta» | Timeout di inattività del PVWA superato: accedi di nuovo. |
| «Password» → «Copia»: «Il PVWA rifiuta: … «Recuperare gli account» …» | Diritto mancante sul safe, o motivo / ticket richiesto dalla piattaforma: inseriscilo. Con la doppia convalida, fai la richiesta nel PVWA. |
| «Verifica / Cambia / Riconcilia»: «Il PVWA rifiuta: … «Avviare le operazioni CPM» …» | Chiedi questo diritto sul safe; «Membri del safe» mostra i tuoi diritti. |
| «Aggiungi un account»: «Il PVWA rifiuta: il tuo account deve avere il diritto «Aggiungere account»…» | Chiedi questo diritto sul safe (e «Aggiornare il contenuto degli account» per fornire la password), oppure crea l'account senza password. «Membri del safe» mostra i tuoi diritti. |
| «Membri del safe»: «Il tuo account non può vedere i membri di questo safe» | Il PVWA richiede il diritto «View Safe Members» sul safe: chiedilo a un gestore del safe. |
| «Connection component … is not configured for platform …» | Scegli il componente corretto in «Connessione avanzata», seleziona «Memorizza» per la piattaforma. |
| «You must specify a reason…» | Inserisci un motivo nella finestra che si apre (o un motivo predefinito nelle proprietà del server). |
| L'account non compare | Non hai il permesso «List accounts» sul suo safe, oppure l'elenco va ricaricato (`F5`). |
| La password PSMP viene chiesta per ogni scheda | MFA caching non attivato sul PVWA: comportamento normale (una volta per scheda). |
| La scheda File indica «Connessione SFTP impossibile» | SFTP non è consentito sul PSMP o per questo account: rivolgiti al team CyberArk. |
| Il browser non segue i `cd` | La shell remota non è bash o zsh, l'opzione è disattivata nelle Impostazioni, oppure il prompt non è stato riconosciuto: riseleziona «Segui la cartella del terminale» al prompt della shell. |
| Avviso «la chiave del PSMP è cambiata» | Prosegui solo se il team CyberArk conferma una modifica del server. |
| «Password principale o file chiave errati.» | Controlla la password e il file chiave; un archivio protetto da YubiKey non è supportato. |
| L'archivio KeePass chiede la password nonostante «Memorizza» | Vault locale bloccato («Più tardi» allo sblocco) o password principale cambiata altrove: digitala, viene memorizzata di nuovo. |
| «Il file del vault locale è danneggiato o è stato creato da un altro account Windows.» | Il vault locale non segue un cambio di computer o di account: eliminalo nelle Impostazioni e ricrealo. |
| «La voce … è stata modificata o eliminata nell'archivio nel frattempo» | Qualcuno ha cambiato la stessa voce altrove: l'archivio viene ricaricato, rifai la modifica. |
| Un account Unix si apre con il PSM e non in SSH | Indirizzo del PSMP non impostato nelle Impostazioni, oppure account non riconosciuto come Unix: clic destro → «Connetti in SSH». |
| Capire un errore di connessione | Impostazioni → Registro di debug, riproduci il problema, poi Impostazioni → «Mostra il file del registro». |
| Una scheda di desktop remoto diretto (KeePass) mostra «Errore del controllo Desktop remoto» | Segnala il codice mostrato (se il controllo Desktop remoto manca dal computer, la connessione passa da `mstsc`). |

## Sviluppo

### Struttura

| Progetto | Ruolo |
| --- | --- |
| `src/CyberArkTerm.Core` | Logica senza interfaccia, multipiattaforma: client dell'API PVWA, classificazione degli account, emulatore di terminale xterm, connessioni PSMP e browser SFTP/SCP (SSH.NET), cartelle di «I miei server», archivi KeePass (KDBX), vault locale, preferenze. |
| `src/CyberArkTerm.App` | Applicazione WPF: finestre, schede, controllo terminale, controllo Desktop remoto (schede RDP), avvio di `mstsc`, icona (`Assets`). |
| `tests/CyberArkTerm.Core.Tests` | Test xUnit di Core (falso PVWA HTTP, terminale, PSMP, cartelle, traduzioni…). |
| `tests/CyberArkTerm.App.Tests` | Test Windows dell'applicazione (vero controllo Desktop remoto, DPAPI). |

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
Windows. I test di `tests/CyberArkTerm.App.Tests` (tra cui un test del vero controllo Desktop remoto) si
eseguono solo su Windows; altrove, esegui `dotnet test tests/CyberArkTerm.Core.Tests`.

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
- Archivi KeePass: cifratura Twofish e chiavi YubiKey non supportate; nessuna creazione di archivio (crealo con
  KeePass o KeePassXC); allegati conservati ma non mostrati.
- Il monitoraggio della cartella del terminale richiede bash o zsh sul server.
- PSM Gateway (HTML5), doppio controllo (dual control) e accesso esclusivo non sono supportati.

**Sviluppi futuri**

- Eseguibile firmato e programma di installazione MSI.
- Più PVWA (profili di connessione), Privilege Cloud.

## Licenza

[MIT](LICENSE) © 2026 muller-camille
