# Guida all'uso di CyberArkTerm

[Français](guide.fr.md) · [English](guide.md) · **Italiano** · [← Torna al README](../README.it.md)

## Indice

- [1. Accedere al vault](#1-accedere-al-vault)
- [2. Trovare un account: scheda «Disponibili»](#2-trovare-un-account-scheda-disponibili)
- [3. Aprire una sessione PSM (desktop remoto)](#3-aprire-una-sessione-psm-desktop-remoto)
- [4. Aprire una sessione SSH tramite il PSMP](#4-aprire-una-sessione-ssh-tramite-il-psmp)
- [5. Sfogliare e inviare file: scheda «File»](#5-sfogliare-e-inviare-file-scheda-file)
- [6. Organizzare i server: scheda «I miei server»](#6-organizzare-i-server-scheda-i-miei-server)
- [7. Accesso di emergenza fuori da CyberArk: archivi KeePass](#7-accesso-di-emergenza-fuori-da-cyberark-archivi-keepass)
- [Scorciatoie](#scorciatoie)
- [Impostazioni e file di configurazione](#impostazioni-e-file-di-configurazione)
- [Sicurezza](#sicurezza)
- [Funzionamento tecnico](#funzionamento-tecnico)
- [Risoluzione dei problemi](#risoluzione-dei-problemi)

## 1. Accedere al vault

<img src="captures/it/sign-in.png" alt="Finestra di accesso" width="440">

Inserisci l'indirizzo del PVWA (`pvwa.miodominio.local` è sufficiente: `https://` e `/PasswordVault` vengono
aggiunti), scegli il metodo di autenticazione, poi nome utente e password. Se il server RADIUS pone una domanda
(codice OTP), la finestra la mostra e attende la tua risposta.

Indirizzo, metodo e nome utente vengono memorizzati; **la password mai**.

L'elenco in basso a sinistra cambia la lingua dell'interfaccia (Français, English, Italiano); la finestra si riapre
subito nella lingua scelta, conservando l'indirizzo e il nome utente inseriti.

## 2. Trovare un account: scheda «Disponibili»

![Scheda «Disponibili» filtrata su più server](captures/it/available.png)

- La casella di ricerca filtra su tutti i campi (server, utente, safe, piattaforma, dominio…), anche con più parole
  (`prd sql`).
- «Raggruppa per» ordina gli account per safe, piattaforma o tipo di destinazione.
- Clic destro su un account → «Esporta gli account visualizzati (CSV)…» salva in CSV gli account mostrati
  (filtrati dalla ricerca).
- **Membri di un safe**: clic destro su un account (o su un safe quando gli account sono raggruppati per safe, o su
  un server di «I miei server») → «Membri del safe». La finestra elenca gli utenti e i gruppi del safe con i loro
  diritti (elencare, usare, recuperare, aggiungere account, aggiornare, eliminare, gestire i membri…), indica chi
  può **aggiungere account** e mostra tutti i diritti del membro selezionato. Il PVWA fornisce questo elenco solo a
  un account con il diritto «View Safe Members» sul safe. `Ctrl+A` e poi `Ctrl+C` copia la tabella. Con il diritto
  «Gestire i membri del safe», i pulsanti «Aggiungi un membro…», «Modifica i diritti…» (o doppio clic) e «Rimuovi…»
  gestiscono i membri: nome, tipo (utente o gruppo), directory («Vault» o il dominio LDAP), eventuale data di fine e
  i 22 diritti, raggruppati come nel PVWA.
- **Aggiungere un account**: clic destro su un account (o su un safe quando gli account sono raggruppati per safe) →
  «Aggiungi un account al safe…». Safe, piattaforma, indirizzo e utente sono obbligatori; dominio di accesso, nome
  dell'account, password, macchine consentite e gestione da parte del CPM sono facoltativi. L'account cliccato fa da
  modello (safe, piattaforma, dominio). L'account viene creato con i diritti della tua sessione: serve il diritto
  «Aggiungere account» sul safe e, in genere, «Aggiornare il contenuto degli account» per fornire la password.
  L'elenco viene poi ricaricato e il nuovo account selezionato.
- **Importare account (CSV)**: clic destro su un account o un safe → «Importa account (CSV)…». Una piccola finestra chiede il file («Salva un modello…» fornisce un esempio), il safe
  e la piattaforma predefiniti e riassume cosa verrà creato; nulla viene inviato prima di «Importa». Colonne
  obbligatorie: indirizzo e utente (più safe e piattaforma, altrimenti i valori predefiniti); facoltative: nome,
  dominio, password, macchine consentite, gestione CPM (sì/no), motivo. Separatore `;`, `,` o tabulazione, nomi
  delle colonne in italiano, francese o inglese; un file prodotto da «Esporta gli account visualizzati» si può reimportare. Una seconda
  finestra crea poi gli account riga per riga e mostra lo stato di ciascuna (creato, rifiutato con il messaggio del
  PVWA, non importato, non inviato; «Interrompi» disponibile). Alla fine propone di salvare il risultato in CSV
  (senza le password). Le password del file non vengono mai mostrate; elimina il file dopo l'importazione.
- **Modificare / eliminare un account**: clic destro → «Modifica l'account…» (piattaforma, indirizzo, utente,
  dominio, nome, macchine consentite, gestione da parte del CPM; vengono inviati solo i campi modificati) oppure
  «Elimina l'account…» (dopo conferma). Diritti «Aggiornare le proprietà degli account» ed «Eliminare account».
- **Stato della password (CPM)**: il tooltip di un account indica se è gestito dal CPM e la data dell'ultimo cambio,
  dell'ultima verifica e dell'ultima riconciliazione; un **⚠** segnala un account la cui ultima operazione del CPM
  non è riuscita.
- **Clic destro → «Password»** (account di «Disponibili» e server di «I miei server»):
  - «Verifica», «Cambia…», «Riconcilia…» chiedono l'operazione al CPM (conferma per cambiare e riconciliare; diritto
    «Avviare le operazioni CPM»). Il CPM la esegue poi: `F5` per vedere il nuovo stato.
  - «Copia la password…»: motivo e ticket se la piattaforma li richiede, poi la password viene copiata negli appunti
    per 20 secondi, **senza essere mostrata** (diritto «Recuperare gli account»; il recupero viene registrato
    nell'audit del vault).
- Nella scheda Home, la **connessione rapida** trova un server mentre digiti: Invio per connetterti. Le **sessioni
  recenti** sono quelle del PVWA a cui sei connesso: su un altro PVWA lo stesso ID di account indica un altro account.

## 3. Aprire una sessione PSM (desktop remoto)

<img src="captures/it/psm-connect.png" alt="Connessione PSM avanzata: macchina di destinazione, motivo, ticket" width="520">

Fai doppio clic sull'account (oppure Invio, oppure il pulsante «Connetti»); un account Unix si apre in SSH tramite
il PSMP quando il suo indirizzo è impostato, o in soli file per una piattaforma «SFTP» (vedi 4.), e «Connessione
avanzata…» permette allora di scegliere il PSM. CyberArkTerm richiede la connessione al PVWA e apre la sessione in **Connessione Desktop remoto** di Windows
(`mstsc`), esattamente come il pulsante «Connect» del PVWA: il file RDP del PVWA le viene passato così com'è. Un
componente che apre un'applicazione remota (RemoteApp) apre le sue finestre sul desktop del computer.

- **Componente PSM**: dedotto dalla piattaforma (`PSM-RDP` per Windows, `PSM-SSH` per Unix e rete,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Seleziona «Memorizza questo componente» per conservarlo per tutta la
  piattaforma. Il tuo PVWA può chiamare i suoi componenti in un altro modo (ad esempio `WIN-PSM`): inserisci il nome
  proposto dal suo pulsante «Connect»; l'elenco propone poi i componenti già usati, quello della piattaforma per
  primo.
- **Account di dominio**: la finestra chiede la macchina di destinazione, precompilata con le macchine autorizzate
  dell'account.
- **Motivo e ticket**: se il PVWA rifiuta la richiesta (motivo obbligatorio, componente non configurato…), il suo
  messaggio viene mostrato e puoi correggere e riprovare.
- Il pulsante «Avanzata…» (o clic destro → «Connessione avanzata…») apre questa finestra su richiesta.

## 4. Aprire una sessione SSH tramite il PSMP

Imposta una volta l'indirizzo del PSMP nelle **Impostazioni**. Il doppio clic (o Invio) sceglie allora in base al
nome della piattaforma dell'account:

| Nome della piattaforma | Apertura predefinita |
| --- | --- |
| contiene «SFTP» (`UnixSFTP`, `SFTP-Partner`…) | **solo file** in SFTP tramite il PSMP (vedi sotto) |
| contiene «SSH» (`UnixSSH`, `CiscoSSH`…), o un'altra piattaforma Unix | **SSH** tramite il PSMP |
| altro | **PSM** (desktop remoto) |

Il clic destro propone sempre le tre (l'apertura predefinita è in grassetto): «Connetti (PSM)», «Connetti in SSH
(PSMP)» (o il pulsante «SSH») e «Apri i file (SFTP, PSMP)».

**Solo file**: una sola sessione PSMP SFTP, senza terminale. Una scheda ne mostra lo stato; i file sono nella scheda
«File», con le stesse funzioni (trasferimenti verificati, coda, editor, confronto, monitoraggio in tempo reale,
permessi), tranne ciò che richiede un terminale (monitoraggio della cartella del terminale, estrazione di un archivio
`.tar.gz`). Utile per depositare o recuperare file, o quando la piattaforma consente PSMP-SFTP ma non la shell. Come
ogni sessione PSMP, è registrata e verificata da CyberArk.

La sessione si apre **in una scheda di CyberArkTerm**, con l'identificativo PSMP standard `<tu>@<account di
destinazione>[#dominio]@<server di destinazione>`. I nomi utente che contengono spazi (`Mario Rossi`, `Admin
Locale`) sono accettati.

<img src="captures/it/psmp-authentication.png" alt="Domanda di autenticazione posta dal PSMP" width="49%"> <img src="captures/it/terminal-menu.png" alt="Menu del clic destro nel terminale SSH" width="49%">

- **Autenticazione**: se il PVWA fornisce una chiave «MFA caching», non viene posta alcuna domanda. Altrimenti
  vengono mostrate le domande del PSMP (password, codice MFA); la password viene riutilizzata per le connessioni
  SFTP e SCP della stessa scheda, mai salvata.
- **Chiave del PSMP**: alla prima connessione ne viene mostrata l'impronta SHA256, da accettare; se in seguito
  cambia, viene mostrato un avviso.
- **Terminale**: la selezione copia, la rotellina scorre la cronologia, AltGr funziona sulle tastiere
  internazionali. Chiudi la scheda con la croce o con un clic centrale.
- **Clic destro nel terminale** (o tasto Menu della tastiera): copia, incolla, seleziona tutto, cerca, salva il
  contenuto, cancella la cronologia (solo su questo computer, nulla viene inviato al server), dimensione del
  carattere, e le azioni della scheda (riconnetti, duplica, stacca, vista parallela, chiudi). Per incollare con un
  semplice clic destro, spunta «Il clic destro nel terminale incolla gli appunti» nelle Impostazioni; Maiusc+clic
  destro apre allora il menu.
- **Aspetto**: tavolozza di colori e dimensione del carattere nelle Impostazioni (Campbell, One Half, Solarized,
  scuri o chiari); `Ctrl+rotellina` ingrandisce o riduce un terminale, `Ctrl+0` torna alla dimensione predefinita.
- **Cercare** nel terminale, cronologia compresa: `Ctrl+Maiusc+F` (o clic destro nel terminale o sulla scheda). Le
  occorrenze sono evidenziate; `Invio` risale verso le più vecchie, `Maiusc+Invio` riscende, `Esc` chiude.
- **Salvare il contenuto** del terminale (cronologia e schermo) in un file di testo: `Ctrl+Maiusc+S` (o clic destro
  nel terminale o sulla scheda). Solo su tua richiesta: il file può contenere informazioni sensibili.
- **Staccare una scheda** (altro schermo): trascina la scheda SSH fuori dalla finestra, o clic destro → «Stacca in
  una nuova finestra». Il terminale passa in una finestra separata e la sessione continua. La scheda mantiene il suo
  posto («Mostra la finestra», «Riporta nella scheda») e la scheda File lavora su questa sessione quando è
  selezionata. Chiudere la finestra separata riporta il terminale nella sua scheda senza chiudere la sessione. Le
  schede Desktop remoto non si staccano (usa «Schermo intero»); le sessioni PSM si aprono già in Connessione Desktop
  remoto di Windows, una finestra a parte.
- **Vista parallela** (fino a 8 sessioni sullo schermo): pulsante «Parallelo» della barra degli strumenti, o clic
  destro su una scheda SSH → «Aggiungi alla vista parallela». Seleziona le sessioni SSH aperte da mostrare insieme
  (8 al massimo): vengono disposte a griglia nella scheda «Parallelo», affiancate fino a 3, poi su due righe. Ogni
  sessione ha il suo titolo e il suo stato; «⤢» (o doppio clic sul titolo) la ingrandisce da sola, «✕» la rimanda
  nella sua scheda. La scheda File segue la sessione in cui lavori. «Chiudi la vista» restituisce ogni terminale
  alla sua scheda senza chiudere le sessioni. Le sessioni Desktop remoto non possono esservi inserite.
  - **Digitazione simultanea**: pulsante «Digitazione simultanea» della vista. Ciò che digiti in una sessione
    selezionata («Riceve la digitazione») viene inviato anche alle altre sessioni selezionate e connesse: lo stesso
    comando su più server. È **disattivata a ogni apertura della vista**; quando è attiva, una fascia arancione
    indica il numero e il nome delle sessioni che ricevono la digitazione, e una cornice arancione le circonda. Una
    sessione aggiunta mentre è attiva non è selezionata; ciò che viene digitato in una sessione non selezionata va
    solo a lei. Ogni tasto viene codificato dalla sessione che lo riceve (le frecce funzionano in una shell come in
    vim). La rotellina non viene copiata, e incollare più righe in più sessioni chiede conferma.
  - **Da «I miei server»**: clic destro su una cartella → «Apri nella vista parallela» connette i suoi server SSH
    (sottocartelle comprese) e li mette direttamente nella vista; oppure scegli dei server con `Ctrl+clic`
    (`Maiusc+clic` per una serie), poi clic destro → «Apri i N server nella vista parallela». Oltre i posti liberi
    (8 al massimo), una finestra chiede quali aprire; i server Windows (PSM) vengono esclusi. Ogni connessione resta
    una sessione PSMP distinta, con le sue domande abituali.
  - **Finestra separata**: pulsante «Finestra separata» della vista, clic destro sulla scheda «Parallelo», o
    trascina la scheda fuori dalla finestra. Chiuderla riporta la vista nella sua scheda senza chiudere le sessioni.
  - **Inviare file** alle sessioni della vista: pulsante «Invia file…» (vedi la scheda File).

## 5. Sfogliare e inviare file: scheda «File»

All'apertura di una sessione SSH, la scheda **File** appare sul lato e segue la scheda SSH attiva. Serve anche alle
sessioni di soli file: account CyberArk in SFTP tramite il PSMP ([sezione 4](#4-aprire-una-sessione-ssh-tramite-il-psmp))
e voci KeePass SFTP, FTP, FTPS ([sezione 7](#7-accesso-di-emergenza-fuori-da-cyberark-archivi-keepass)).

![Scheda File ordinata per data, accanto al terminale](captures/it/main-window.png)

- **Barra del percorso**: percorso corrente, modificabile (digita un percorso e premi Invio). Doppio clic su una
  cartella per entrarvi, `..` per risalire, pulsanti «cartella superiore» e «cartella home».
- **Ordinamento**: clic sull'intestazione di una colonna (Nome, Dimensione, Modificato, Permessi); un secondo clic
  inverte l'ordine (lo indica una freccia). Dimensione e data partono dai più grandi e dai più recenti. Le cartelle
  restano in cima; l'ordinamento è mantenuto da una cartella e da una sessione all'altra.
- **Inviare file**: trascinali da Esplora file sull'elenco (o il pulsante «Invia»). Invio in **SFTP** per
  impostazione predefinita (SCP a scelta nelle Impostazioni), cartelle comprese; conferma prima di sovrascrivere un
  file esistente (file nascosti compresi, anche se non mostrati). Se il server rifiuta quel protocollo per un file prima di riceverlo (regola del PSMP, SFTP in sola
  lettura…), l'altro subentra subito, senza domande né attese: la barra di stato e il riepilogo lo indicano con la
  risposta del server, e così la Cronologia («SCP (SFTP rifiutato)»). In SCP, dopo un rifiuto all'annuncio di un
  file, i file grandi almeno altrettanto partono direttamente in SFTP fino alla chiusura della scheda.
- **Scaricare**: pulsante «Scarica» o clic destro. Un file chiede dove salvarlo; più file vanno in una cartella
  scelta, con una sola domanda per quelli già presenti. Un file locale viene sostituito solo a download completato: un
  download interrotto o annullato lo lascia com'era.
- **Scaricare trascinando**: trascina file o cartelle dall'elenco verso Esplora file o il desktop. Nulla viene
  scaricato durante il trascinamento: al rilascio, una finestra mostra l'avanzamento (Annulla lo interrompe), poi
  Esplora file copia i file dove li hai rilasciati. I nomi Unix vengono resi validi per Windows (`\`, `:`, `..`,
  `CON`… sostituiti), senza mai scrivere fuori dalla cartella di rilascio; la cartella temporanea del download viene
  poi eliminata.
- **Coda dei trasferimenti**: invii e download vengono eseguiti uno alla volta, nell'ordine delle richieste; ciò che
  chiedi durante un trasferimento si aggiunge alla coda invece di essere ignorato. Un invio va nella cartella
  mostrata al momento del rilascio, e la conferma di sovrascrittura tiene conto anche degli invii ancora in attesa.
  Un pannello sopra la barra di stato mostra ogni elemento (in attesa, avanzamento e file n/N, verifica, risultato):
  ✕ rimuove un elemento in attesa, «Annulla» interrompe quello in corso, «Annulla tutto» svuota la coda. Un
  trasferimento interrotto elimina il file in corso, incompleto (sul server per un invio, sul computer per un
  download); i file già trasferiti restano. Attenzione: se l'invio sostituiva un file esistente, il vecchio
  contenuto è perso. In SCP, l'interruzione riguarda solo quel trasferimento: gli elementi successivi proseguono
  sulla stessa connessione; un trasferimento che non avanza più (server che non legge più) si ferma 2 s dopo
  «Annulla» e i successivi partono su una nuova connessione. Un file inviato via SCP prende sul server la data dell'invio (come `scp` senza `-p`, e come in
  SFTP). Un errore viene mostrato nella coda e la coda prosegue; alla fine, un unico riepilogo. Navigazione,
  eliminazione, permessi, editor e trascinamento verso Esplora file passano tra due file. Chiudere la scheda o
  l'applicazione con trasferimenti in corso chiede conferma.
- **Molti file insieme: archivio .tar.gz**: da 200 file rilasciati (soglia nelle Impostazioni, opzione «Proporre un
  archivio .tar.gz»), CyberArkTerm propone di inviarli in un unico archivio: un solo file da trasferire e verificare
  invece di migliaia, molto più veloce tramite il PSMP. L'archivio viene creato sul computer (nella coda,
  annullabile), inviato e verificato (SHA-256), poi eliminato dal computer; ogni elemento rilasciato è alla radice
  dell'archivio (permessi 0644 e 0755). Nulla viene eseguito sul server: un riquadro arancione compare in fondo alla
  scheda File con il comando di estrazione, per esempio `cd '/opt/app' && /usr/bin/gzip -dc './deploy.tar.gz' | tar
  xf - && rm -f './deploy.tar.gz'` (l'archivio viene eliminato dal server dopo l'estrazione). «Copia il comando»,
  oppure «Scrivi nel terminale», che lo digita al prompt della sessione senza eseguirlo: controllalo, poi premi
  Invio. Il riquadro resta visibile (per quella sessione) finché non lo chiudi. «Invia i file uno per uno» mantiene
  l'invio abituale; «Non proporre più» disattiva l'opzione.
  - **Tutti gli Unix** (Red Hat da 5 a 9, HP-UX 11.11 e 11.31, Solaris, AIX…): l'archivio è nel formato tar POSIX
    standard (ustar), letto da tutti i tar, e il comando usa `gzip` e `tar xf` separatamente, senza opzioni proprie
    di GNU tar. Si digita in qualsiasi shell (sh, ksh, bash, zsh, csh, tcsh).
  - **gzip** viene cercato sul server (tramite SFTP) dove ogni sistema lo installa: `/bin`, `/usr/bin`,
    `/usr/contrib/bin` (HP-UX), `/usr/local/bin`, `/opt/freeware/bin` (AIX), `/usr/sfw/bin` e `/opt/csw/bin`
    (Solaris). Non trovato: l'archivio viene inviato senza compressione (`.tar`), estratto dal solo `tar`.
  - Un nome oltre 100 caratteri (cartelle escluse) o un file oltre 8 GB non rientra in questo formato: i file
    vengono allora inviati uno per uno, con un messaggio.
- **Inviare a più server**: pulsante (freccia verso tre server) o clic destro → «Invia a più server…». Scegli i file
  o le cartelle, la cartella di destinazione (`~` = la cartella personale dell'account su ogni server, ad es.
  `~/deploy`) e le sessioni SSH destinatarie. CyberArkTerm verifica prima su ogni server che la cartella esista e
  cosa verrebbe sostituito (una sola domanda per tutti), poi mette in coda un invio per server: stesso protocollo,
  stessa verifica SHA-256 su ogni server, un solo riepilogo alla fine.
- **Confrontare**: clic destro su un file → «Confronta con…»: lo stesso percorso (o un altro) su un server con una
  sessione SSH aperta, o un file di questo computer; con due file selezionati, «Confronta i 2 file». «Sfoglia…»
  accanto al percorso apre un esploratore dell'altro server, sulla stessa cartella con il file preselezionato (o
  sulla cartella superiore più vicina che esiste): doppio clic su una cartella per entrarvi, Backspace per risalire,
  si può anche digitare un percorso; il file scelto sostituisce il percorso. La scheda File di quel server resta
  sulla sua cartella. I file vengono
  letti **in memoria** (50 MB al massimo ciascuno), senza copia sul computer. La finestra mostra le righe
  affiancate: rimosse in rosso a sinistra, aggiunte in verde a destra. `F7` / `Maiusc+F7`: differenza successiva /
  precedente; «Ignora gli spazi»; «Solo le differenze»; «Salva il diff…» nel formato `diff -u`. Un file binario (o
  oltre 10 MB) viene confrontato per dimensione e checksum SHA-256. Con uno strumento di confronto scelto nelle
  Impostazioni (WinMerge, VS Code…), «Apri in …» gli passa due copie temporanee, eliminate alla chiusura della
  finestra.
- **Cronologia dei trasferimenti**: pulsante «Cronologia» della barra degli strumenti (frecce su e giù con un
  orologio, a sinistra di «Impostazioni»), disponibile anche senza sessione. Elenca gli ultimi 200 invii e download
  (trascinamento compreso): data, direzione, server, elemento, destinazione, numero di file, protocollo («SCP (SFTP
  rifiutato)» quando l'altro protocollo è subentrato), risultato. Filtro
  «Invii» / «Download»; «Checksum…» (o doppio clic) mostra per ogni file la dimensione, i checksum SHA-256 e il
  risultato, e li copia nel formato di `sha256sum -c` per riverificare sul server; «Apri la cartella» per un
  download; «Cancella la cronologia».
- **Verifica dei trasferimenti (SHA-256)**: ogni file inviato o scaricato viene verificato. All'invio (SCP o SFTP),
  il file locale viene sottoposto a hash, poi il file arrivato sul server viene riletto via SFTP e sottoposto a
  hash. Al download, i dati ricevuti dal server vengono sottoposti a hash, poi il file scritto sul computer viene
  riletto. La barra di stato conferma «✓ identico su entrambi i lati»; i checksum di ogni file sono nella
  **Cronologia** (pulsante della barra degli strumenti). Se un file è diverso, l'errore viene mostrato e il
  dettaglio si apre da solo; un download per trascinamento fallisce invece di consegnare una copia errata. Un file
  che non può essere riletto (permessi) è segnalato «non verificato». La rilettura di un invio raddoppia il volume
  scambiato con il server.
- **Eliminare**: selezione poi Canc (o clic destro → «Elimina (rm)»), con conferma. Le cartelle devono essere vuote.
  Un collegamento simbolico viene eliminato esso stesso, mai il file o la cartella a cui punta.
- **Modificare un file**: **doppio clic** sul file (o `Invio`, `F4`, clic destro → «Modifica», il pulsante matita).
  Il file si apre nell'editor di testo scelto nelle Impostazioni (Blocco note per impostazione predefinita). Con il
  doppio clic, un archivio, un'immagine, un eseguibile o un documento d'ufficio viene scaricato invece di essere
  aperto, come ogni file i cui primi byte sono binari. A ogni salvataggio,
  CyberArkTerm propone di rinviarlo al server: invio in SFTP, permessi del file conservati. Se il file è cambiato
  sul server dopo l'apertura, un avviso chiede conferma prima di sovrascriverlo.
- **Seguire un file (tail -f)**: clic destro su uno o più file → «Segui (tail -f)». Una finestra mostra la fine del
  file, poi ogni nuova riga appena viene scritta, come `tail -f`, leggendo il file via SFTP ogni secondo: nessun
  comando viene eseguito sul server. Un file troncato o sostituito da una rotazione viene riletto dall'inizio;
  vengono conservate le ultime 10.000 righe.
  - **Colori e avvisi**: errori (ERROR, FATAL, CRITICAL…) in rosso, avvisi (WARN) in arancione; parole a scelta
    evidenziate in giallo («Evidenzia», separate da virgole). «Avviso se» (ad es. `ERROR, OutOfMemory, Connection
    refused`): ogni nuova riga che contiene una di queste parole è segnata, il contatore «⚠ n avvisi» aumenta (un
    clic va alla successiva) e il pulsante della finestra lampeggia nella barra delle applicazioni; può essere
    mostrata una notifica di Windows, al massimo una ogni 30 s, con il numero di righe e il nome del file soltanto,
    **mai il contenuto delle righe** (può apparire sulla schermata di blocco). Queste impostazioni sono conservate
    per le finestre successive.
  - **Vista combinata**: più file selezionati si aprono in un'unica finestra, e «Aggiungi a una finestra di
    monitoraggio» vi aggiunge un file di un'altra scheda, quindi di un altro server. Le righe si alternano
    nell'ordine di arrivo, con prefisso e colore del file (`[root@srv01 app.log]`); in basso, ogni file ha il suo
    stato e un pulsante per smettere di seguirlo.
  - **Filtro e ricerca**: filtro (come `grep`), esclusione (come `grep -v`), righe di contesto (come `grep -C 3`),
    in testo semplice o con espressioni regolari. `Ctrl+F` cerca nelle righe senza filtrarle (Invio / `F3`:
    successivo, `Maiusc+F3`: precedente). Scorrere verso l'alto smette di seguire la fine.
  - **Interruzioni**: se la connessione si perde, un segno lo indica; quando la scheda SSH si riconnette (o con
    «Riconnetti»), il monitoraggio riprende da dove si era fermato, con le righe scritte nel frattempo. Chiudere la
    scheda smette di seguire i suoi file; la finestra conserva le righe ricevute.
  - **File memorizzati**: su un server della scheda «I miei server», i file seguiti sono memorizzati (gli ultimi
    12); alla connessione successiva, il pulsante di monitoraggio della scheda File li propone: «Seguili tutti in
    una finestra» con un clic, o uno solo.
  - **Tenere traccia**: «Salva…» scrive le righe visualizzate in un file di questo computer; «Registra in continuo…»
    scrive le righe già ricevute e poi ogni nuova riga appena arriva, finché la casella è selezionata; «Segno»
    inserisce una riga `—— 14:32:05 ——` per ritrovare un momento (prima di un intervento, ad esempio).
  - **Connessione**: per impostazione predefinita, il monitoraggio usa la connessione SFTP della scheda File e passa
    tra due file di un trasferimento. L'opzione «Segui i file (tail -f) in una sessione indipendente» delle
    Impostazioni gli dà una connessione propria, una per finestra e per server: non attende più i trasferimenti, ma
    è una sessione PSMP in più (registrata separatamente, e può essere richiesta una convalida MFA). Viene chiusa
    con la finestra. Una connessione persa non viene mai riaperta in ciclo.
- **Permessi**: clic destro → «Permessi…» (o il pulsante lucchetto). Caselle lettura / scrittura / esecuzione per
  proprietario, gruppo e altri, bit speciali (setuid, setgid, sticky) e valore ottale (`644`, `1777`…), per uno o
  più elementi. Per una cartella, «Applica anche al contenuto» propaga i permessi a sottocartelle e file; per
  impostazione predefinita, l'esecuzione (x) viene data solo alle cartelle e ai file già eseguibili. I link
  simbolici non vengono seguiti e il proprietario non viene modificato.
- Inoltre: nuova cartella, download, copia del percorso, visualizzazione dei file nascosti.
- **Segui la cartella del terminale**: se la casella è selezionata, ogni `cd` nel terminale sposta il browser nella
  stessa cartella (vedi [Funzionamento tecnico](#funzionamento-tecnico)). Dopo `sudo -i` o `su`, riseleziona la
  casella al prompt della shell per riattivare il monitoraggio nella nuova shell.

<img src="captures/it/transfer-history.png" alt="Cronologia dei trasferimenti con la verifica SHA-256 di ogni file" width="820">

## 6. Organizzare i server: scheda «I miei server»

![I miei server organizzati in cartelle](captures/it/my-servers.png)

- **Aggiungere** un account: clic destro in «Disponibili» → «Aggiungi ai miei server» e poi la cartella desiderata,
  oppure trascina l'account sulla scheda «I miei server», oppure il pulsante «Aggiungi» della barra degli strumenti.
- **Aggiungere una connessione recente**: clic destro nelle sessioni recenti della home → «Aggiungi ai miei server»
  e poi la cartella desiderata. Il server mantiene il tipo di connessione (PSM, SSH o solo file), il componente PSM e la
  macchina di destinazione usati.
- **Cartelle**: clic destro → nuova cartella o sottocartella, rinomina, elimina; trascina server e cartelle per
  spostarli. Eliminare una cartella conta ed elimina tutti i suoi server di questo PVWA, anche quelli nascosti dalla
  ricerca; quelli di un altro PVWA restano.
- **Cercare**: campo in cima alla scheda (o `Ctrl+F` nella scheda). Filtra i server per nome, server, utente,
  cartella, componente, macchina di destinazione, e le voci degli archivi KeePass sbloccati; le cartelle dei
  risultati vengono espanse. `Invio` o `↓` seleziona il primo risultato, `Esc` cancella.
- **Più server alla volta**: `Ctrl+clic` aggiunge o toglie un server (o tutti quelli di una cartella), `Maiusc+clic`
  sceglie una serie di server; `Esc` o un clic semplice annulla. Clic destro su uno di essi → «Apri i N server nella
  vista parallela» o «Connettiti ai N server» (una scheda ciascuno). Clic destro su una cartella → «Apri nella vista
  parallela» o «Connettiti ai N server».
- **Configurazione propria di ogni server** (clic destro → «Proprietà…»):

| Impostazione | Effetto |
| --- | --- |
| Nome, cartella | Visualizzazione e posizione nell'albero. |
| PSM, SSH tramite PSMP o solo file (SFTP tramite PSMP) | Tipo di connessione aperto con il doppio clic (all'inizio, in base alla piattaforma). |
| Componente PSM | Componente da usare (vuoto: dedotto dalla piattaforma). |
| Macchina di destinazione | Server su cui aprire la sessione per un account di dominio. |
| Motivo predefinito | Motivo di accesso inviato automaticamente al PVWA. |
| Cartella SFTP iniziale | Il terminale **e** il browser dei file si aprono direttamente in questa cartella. |

<img src="captures/it/server-properties.png" alt="Proprietà di un server in «I miei server»" width="540">

Un server il cui account non è più visibile in CyberArk appare in grigio.

### Esportare, importare, condividere

Tre pulsanti in alto nella scheda, a sinistra del pulsante archivio KeePass:

- **Esporta** salva «I miei server» in un file `.json`: cartelle (anche vuote), nome, account CyberArk (ID), tipo di
  connessione, componente, macchina di destinazione, motivo predefinito, cartella SFTP iniziale. Nessuna password né
  file seguito. Utile per cambiare computer o passare il proprio elenco.
- **Importa** legge un file esportato (o un elenco condiviso) e riassume prima di aggiungere: server aggiunti, server
  già presenti (stesso account, tipo, componente, macchina di destinazione e cartella: ignorati), cartelle create,
  server aperti su una macchina di destinazione (da verificare: la macchina viene dal file). Niente viene rimosso o
  modificato in «I miei server». Un file creato per un altro PVWA viene rifiutato: i suoi ID di account vi indicano
  altri account.
- **Elenchi condivisi** (icona con due persone): un elenco di server in un file su una condivisione di rete, che tutto
  il team apre e completa.
  - «Crea un elenco condiviso…»: scegli la posizione (condivisione di rete) e il nome mostrato a tutti; «Apri un
    elenco condiviso…»: aggiungi un elenco creato da un collega. Gli elenchi aperti compaiono in cima alla scheda
    (dopo gli archivi KeePass), con le loro cartelle; anche la ricerca li filtra.
  - **Aggiungere**: clic destro su un server o una cartella di «I miei server» → «Condividi in un elenco» (la
    cartella viene mantenuta), o trascina un server, una cartella o un account di «Disponibili» sull'elenco o su una
    sua cartella (conferma). Il motivo predefinito resta personale: non viene mai condiviso.
  - **Rimuovere**: clic destro → «Rimuovi dall'elenco condiviso…» (o `Canc`), dopo conferma.
  - **Usare**: doppio clic per connettersi; clic destro per la connessione avanzata, la password, i membri del safe o
    «Copia in I miei server». Ognuno si connette con i propri diritti CyberArk: un account che non vedi nel vault
    appare in grigio. La descrizione comandi mostra l'account come lo descrive CyberArk, la macchina di destinazione,
    chi ha aggiunto il server e quando.
  - **Macchina di destinazione**: un server condiviso che apre un account di dominio su una macchina non presente tra
    le macchine consentite dell'account in CyberArk chiede conferma alla prima connessione (chiunque abbia diritto di
    scrittura sulla condivisione può modificare l'elenco). «Copia in I miei server» elenca questi server e chiede
    conferma prima di copiarli.
  - **Elenco di un altro PVWA**: un elenco creato per un altro vault viene mostrato, ma i suoi server non si aprono e
    non si copiano, e non vi si può aggiungere nulla: accedi a quel PVWA per usarlo.
  - **Cronologia**: clic destro → «Cronologia delle modifiche…». La scheda «Modifiche» elenca chi ha aggiunto,
    rimosso o ripristinato cosa, e quando; la scheda «Versioni» conserva una copia dell'elenco a ogni revisione (le
    ultime 100, nella cartella `nome.versions` accanto al file). «Ripristina questa versione…» riporta l'elenco in
    quello stato; il ripristino viene a sua volta registrato, quindi può essere annullato.
  - Le modifiche di ognuno si sommano: il file viene riletto e modificato in esclusiva (un computer che scrive nello
    stesso momento attende il proprio turno), e l'elenco mostrato si aggiorna quando un collega lo modifica (`F5` lo
    rilegge anche). «Chiudi l'elenco» lo toglie dalla tua scheda senza toccare il file.
  - Diritti: quelli della condivisione di rete. In sola lettura, l'elenco resta utilizzabile ma non modificabile.

## 7. Accesso di emergenza fuori da CyberArk: archivi KeePass

Quando CyberArk non è disponibile, CyberArkTerm apre i tuoi archivi KeePass (`.kdbx`) e si connette **direttamente**
ai server, in SSH, in desktop remoto o in VNC, o ai soli loro file (SFTP, FTP, FTPS), con gli account che
contengono.

> Queste connessioni **non passano dal PSM**: nessuna registrazione, nessuna regola CyberArk. Ogni apertura di
> archivio, connessione e modifica è annotata nel registro locale `%APPDATA%\CyberArkTerm\urgence.log`.

![Accesso di emergenza: archivio KeePass sbloccato in «I miei server»](captures/it/keepass-vault.png)

- **Senza CyberArk**: nella schermata di accesso, «Accesso di emergenza (KeePass)» apre la finestra principale senza
  PVWA (sono mostrati solo gli archivi KeePass). Con CyberArk, gli archivi compaiono anche in cima a «I miei
  server».
- **Aggiungere un archivio**: pulsante cassaforte della scheda «I miei server» (o clic destro → «Aggiungi un
  archivio KeePass…»): file `.kdbx`, nome, eventuale file chiave.
- **Sbloccare**: doppio clic sull'archivio. Password principale e/o file chiave (tutti i formati di KeePass).
  «Memorizza la password principale nel vault locale» evita di ridigitarla (vedi sotto).
- **Connettersi**: doppio clic su una voce. Il protocollo viene dal suo indirizzo (`ssh://server:22`,
  `rdp://server`, `vnc://server`, `sftp://`, `ftp://`, `ftpes://`, `ftps://`, o `server:3389`), da un campo
  «Protocol» / «Port» o da un'etichetta (`ssh`, `rdp`, `vnc`, `sftp`, `ftp`, `ftpes`, `ftps`); altrimenti
  CyberArkTerm chiede il protocollo. La password della voce è usata direttamente; non è mai mostrata né scritta su
  disco.
  - **SSH**: schede terminale + File.
  - **Desktop remoto**: la scheda ne segue la dimensione (risoluzione del desktop remoto) e propone «Schermo intero»
    (`Ctrl+Alt+Pausa` per tornare), «Disconnetti» e «Riconnetti».
  - **VNC** (`vnc://server`, porta 5900; `vnc://server:1` indica lo schermo 1, porta 5901): desktop in una scheda,
    adattato alla finestra o a dimensione reale («Adatta»), pulsanti «Ctrl+Alt+Canc», «Invia gli appunti» e «Copia
    il testo remoto»: gli appunti sono scambiati solo tramite questi pulsanti. Autenticazione con password VNC (8
    caratteri al massimo, limite del protocollo) o senza autenticazione. **VNC non cifra nulla**: un banner lo
    ricorda; riservalo a una rete fidata.
  - **File** (`sftp://`, `ftp://`, `ftpes://` per FTP con TLS esplicito, `ftps://` per TLS implicito, porta 990):
    una scheda di stato, senza terminale, e i file nella scheda «File» con le stesse funzioni (trasferimenti
    verificati con SHA-256, coda, cronologia, editor, confronto, monitoraggio in tempo reale, permessi se il server
    accetta `SITE CHMOD`). Clic destro → «Apri i file (SFTP, FTP)» fa lo stesso per una voce SSH, in SFTP. Con
    `ftp://`, la cifratura TLS è tentata per prima; se il server non la propone, CyberArkTerm chiede prima di
    connettersi in chiaro (una volta per sessione) e un banner lo ricorda. `ftpes://` e `ftps://` non passano mai
    in chiaro. Un certificato FTPS che Windows non approva (autofirmato…) è mostrato con la sua impronta SHA-256,
    poi memorizzato per quel server se lo accetti.
- **Modificare l'archivio**: clic destro → «Nuova voce…», «Modifica…» (`F2`), «Elimina» (`Canc`, nel cestino
  dell'archivio). Il resto dell'archivio (allegati, campi, impostazioni) è conservato; la versione precedente di una
  voce va nella sua cronologia, come in KeePass.
- **Bloccare**: clic destro → «Blocca». Gli archivi si bloccano anche alla disconnessione, alla chiusura e al
  **blocco di Windows**.

**Vault locale**: le password principali che scegli di memorizzare sono conservate in
`%APPDATA%\CyberArkTerm\coffre-local.dat`, cifrato con una tua password (chiesta quando sblocchi un archivio KeePass
la cui password è memorizzata, «Più tardi» per digitare invece la password dell'archivio) e legato al tuo account
Windows. Gestione nelle **Impostazioni**: crea, sblocca, cambia password, elimina. Si blocca alla disconnessione
(così «Accesso di emergenza» non riapre mai gli archivi memorizzati senza password), alla chiusura e al blocco di
Windows.

## Scorciatoie

| Dove | Azione | Scorciatoia |
| --- | --- | --- |
| Ovunque | Ricaricare gli account dal PVWA | `F5` |
| Ovunque | Filtrare gli account (in «I miei server»: cercare un server) | `Ctrl+F` |
| Elenchi e alberi | Aprire la sessione | Doppio clic o `Invio` |
| Ricerca | Cancellare il filtro | `Esc` |
| I miei server | Rinominare / rimuovere o eliminare | `F2` / `Canc` |
| I miei server | Scegliere più server (poi clic destro per aprirli insieme) | `Ctrl+clic`, `Maiusc+clic`; `Esc` annulla |
| Terminale | Copiare | Selezione con il mouse, o `Ctrl+Maiusc+C` |
| Terminale | Incollare | `Maiusc+Ins` o `Ctrl+Maiusc+V` (clic destro con l'opzione delle Impostazioni) |
| Terminale | Menu: copia, incolla, seleziona tutto, cerca, salva, cancella la cronologia, carattere, azioni della scheda | Clic destro o tasto Menu (Maiusc+clic destro con l'opzione di incolla) |
| Terminale | Cronologia | Rotellina, `Maiusc+Pag su` / `Maiusc+Pag giù` |
| Terminale | Cercare (cronologia compresa) | `Ctrl+Maiusc+F`, poi `Invio` / `Maiusc+Invio` |
| Terminale | Salvare il contenuto in un file | `Ctrl+Maiusc+S` |
| Terminale | Dimensione del carattere / predefinita | `Ctrl+rotellina` / `Ctrl+0` |
| Confronto | Differenza successiva / precedente | `F7` / `Maiusc+F7` |
| Scheda SSH o Desktop remoto | Chiudere | Croce della scheda o clic centrale |
| Scheda SSH o Desktop remoto | Riconnettere, duplicare (altra sessione sullo stesso account o sulla stessa voce), staccare (SSH), chiudere, chiudere le altre schede | Clic destro sulla scheda |
| Scheda SSH | Staccare in una finestra separata (altro schermo) | Trascinare la scheda fuori dalla finestra |
| Scheda SSH | Aggiungere alla vista parallela, o toglierla | Clic destro sulla scheda |
| Desktop remoto | Schermo intero / ritorno | `Ctrl+Alt+Pausa` |
| File | Aprire la cartella o modificare il file / modificare / cartella superiore / eliminare / aggiornare | Doppio clic o `Invio` / `F4` / `Backspace` / `Canc` / `F5` |
| File | Ordinare per una colonna, poi invertire | Clic sulla sua intestazione |
| Archivio KeePass | Connettere / modificare / eliminare una voce | Doppio clic o `Invio` / `F2` / `Canc` |

## Impostazioni e file di configurazione

<img src="captures/it/settings.png" alt="Impostazioni" width="480">

| Impostazione | Ruolo | Predefinito |
| --- | --- | --- |
| Lingua dell'interfaccia | Français, English, Italiano o lingua del sistema; applicata dopo la disconnessione o al prossimo avvio | lingua di Windows (inglese se non è tradotta) |
| Indirizzo e porta PSMP | Server PSM for SSH; se impostato, gli account Unix si aprono in SSH per impostazione predefinita (in soli file per una piattaforma «SFTP»); vuoto = SSH e SFTP disattivati | vuoto, 22 |
| Mantenere aperta la sessione PVWA | Richiesta leggera ogni 4 minuti; sospesa quando Windows è bloccato | sì |
| Cercare una nuova versione all'avvio | Una richiesta a GitHub al massimo una volta al giorno; un link nella barra di stato se esiste una versione più recente | no |
| Vault locale | Password principali KeePass memorizzate: crea, sblocca, cambia password, elimina | — |
| Registro di debug | Menu del pulsante Impostazioni: svolgimento delle connessioni in un file, senza segreti (vedi [Sicurezza](#sicurezza)); «Mostra il file del registro» lo apre in Esplora risorse | no |
| SSH in CyberArkTerm | Terminale e scheda File integrati; altrimenti Windows Terminal | sì |
| Segui la cartella del terminale | Consente di attivare il monitoraggio della cartella nella shell | sì |
| Invio dei file | Protocollo provato per primo (SFTP o SCP); se il server lo rifiuta, subentra l'altro | SFTP |
| Editor di testo | Programma aperto da «Modifica» nella scheda File | Blocco note |
| Strumento di confronto | Programma proposto nella finestra di confronto, con i suoi argomenti (`{0}` = file di sinistra, `{1}` = di destra) | nessuno |
| Colori del terminale, carattere | Tavolozza (Campbell, One Half, Solarized…) e dimensione del carattere dei terminali SSH | Campbell, 14 |
| Monitoraggio in una sessione indipendente | Seguire un file (tail -f) apre una propria connessione SFTP (una sessione PSMP in più) | No |
| Chiavi dei server accettate | Impronte memorizzate: PSMP, server SSH e certificati FTPS delle voci KeePass (pulsante «Dimentica le chiavi») | — |
| Componenti memorizzati | Componente PSM scelto per piattaforma (pulsante «Dimentica») | — |

Tutte le preferenze sono salvate in `%APPDATA%\CyberArkTerm\settings.json`: lingua, indirizzo del PVWA, metodo e
nome utente di accesso, impostazioni qui sopra, «I miei server», le loro cartelle e i file seguiti su di essi
(percorsi), sessioni recenti, posizione degli archivi KeePass e dei loro file chiave, e degli elenchi condivisi aperti. Questo file **non contiene
password, token né chiavi private**. Per ripartire da zero, chiudi l'applicazione ed eliminalo. Viene scritto
prima in un file temporaneo e poi messo al suo posto, mantenendo il precedente come `settings.json.bak`: se il file
diventa illeggibile, viene messo da parte (mai sovrascritto), si riprende il backup e un messaggio lo segnala.
CyberArkTerm si apre una sola volta per sessione di Windows: due istanze si sovrascriverebbero a vicenda le
impostazioni. La cronologia dei
trasferimenti della scheda File è accanto, in `transfers.json` (nomi e percorsi dei file, checksum SHA-256, mai il
loro contenuto).

## Sicurezza

- **HTTPS obbligatorio** verso il PVWA; la convalida dei certificati non viene mai disattivata.
- **Nessun segreto su disco**: password CyberArk, token di sessione, chiave MFA e password PSMP restano in memoria
  per la durata della sessione. Disconnessione dal PVWA (`Logoff`) alla chiusura.
- Sessione PVWA aperta con `concurrentSession`: l'eventuale sessione web del PVWA non viene chiusa.
- **Copia di una password**: la risposta del PVWA viene letta in un buffer cancellato subito dopo e decodificata
  senza passare da una stringa; la password va direttamente negli appunti di Windows, contrassegnata per essere
  esclusa dalla cronologia (`Win+V`), dalla sincronizzazione tra dispositivi e dagli strumenti di monitoraggio degli
  appunti, poi cancellata dopo 20 s se è ancora presente, oltre che alla disconnessione, alla chiusura e al blocco
  di Windows. Non viene mai mostrata né scritta nel registro di debug.
- **Aggiunta di un account**: la password viene letta dal campo mascherato senza passare da una stringa, inviata una
  sola volta al PVWA in HTTPS, poi cancellata dalla memoria; non viene né salvata né scritta nel registro di debug.
- **Sessioni PSM**: il file RDP del PVWA (token PSM monouso) viene scritto in `%TEMP%\CyberArkTerm` per `mstsc`, che
  ne verifica la firma, poi eliminato dopo 60 s o alla chiusura.
- **Digitazione simultanea** (vista parallela): disattivata a ogni apertura della vista, segnalata da una fascia e
  una cornice arancioni che nominano le sessioni interessate; una sessione aggiunta non vi è inclusa d'ufficio, e
  incollare più righe in più sessioni chiede conferma. Ogni sessione resta una sessione PSMP distinta, registrata
  come di consueto.
- **Confronto di file**: contenuti letti in memoria e cancellati alla chiusura della finestra; solo le copie date a
  uno strumento esterno passano dal disco (`%TEMP%\CyberArkTerm\compare`), eliminate alla chiusura della finestra e
  all'avvio successivo.
- **Nuova versione**: nessuna richiesta verso Internet senza una tua azione o l'opzione delle Impostazioni
  (disattivata per impostazione predefinita); vengono seguiti solo gli indirizzi del repository del progetto,
  l'archivio viene conservato solo se il suo checksum SHA-256 è quello di `SHA256SUMS.txt`, e nulla viene installato
  né avviato.
- **Chiavi host del PSMP fissate** al primo utilizzo, con avviso in caso di modifica (lo stesso per i server
  raggiunti in accesso di emergenza).
- **Mantenimento della sessione PVWA**: evita la scadenza per inattività; non viene inviato nulla mentre Windows è
  bloccato, e l'opzione si disattiva nelle Impostazioni se la tua politica lo richiede.
- **Archivi KeePass**:
  - la password principale non è mai salvata, tranne nel vault locale se lo chiedi: Argon2id (64 MiB, 3 passate) poi
    AES-256-GCM, parametri di derivazione autenticati, il tutto protetto da DPAPI (account Windows);
  - in memoria, la chiave dell'archivio e le password delle voci restano mascherate e sono rivelate solo al momento
    della connessione; archivi bloccati alla disconnessione, alla chiusura e al blocco di Windows;
  - salvataggio sicuro: il file viene riletto, la modifica è applicata alla sua versione attuale (le modifiche fatte
    altrove sono conservate), il risultato decifrato è verificato, una copia `.bak` è conservata e il file è
    sostituito in un solo passo; una voce modificata altrove nel frattempo non viene sovrascritta;
  - desktop remoto diretto: la password è passata solo al controllo Desktop remoto (nessun file, nessun gestore
    credenziali), con autenticazione a livello di rete (NLA) e avviso se il server non è riconosciuto;
  - VNC: il protocollo non cifra né lo schermo, né i tasti, né gli appunti (banner permanente); la password non è
    inviata così com'è (sfida-risposta del protocollo); gli appunti sono scambiati solo con un clic; la dimensione
    dello schermo annunciata dal server è limitata (8.192 pixel per lato);
  - FTP: TLS tentato per primo, connessione in chiaro solo dopo il tuo consenso (banner permanente), mai per
    `ftpes://` e `ftps://`; con TLS, anche i trasferimenti sono cifrati (`PROT P`);
  - certificato FTPS: quello che Windows approva è accettato; altrimenti la sua impronta SHA-256 è mostrata e
    fissata al primo consenso (come una chiave host SSH), un cambiamento è segnalato; rifiutato, la connessione si
    ferma prima dell'invio del nome utente;
  - nomi di file con caratteri di controllo rifiutati (nessuna iniezione di comandi FTP);
  - `urgence.log`: data, account Windows, computer, azione, archivio, voce, destinazione; mai una password.
- **Registro di debug**, disattivato per impostazione predefinita (menu del pulsante Impostazioni):
  `%LOCALAPPDATA%\CyberArkTerm\debug.log`, al massimo 5 MB più una generazione `.1`. Registra lo svolgimento delle
  connessioni PVWA, PSM, desktop remoto e SSH: indirizzi e stati delle richieste, impostazioni del file .rdp, eventi
  e codici del controllo Desktop remoto, versione e algoritmi del server SSH, errori; per ogni protocollo di invio
  rifiutato, il passo (connessione, comando scp, annuncio del file), la risposta del server e il protocollo
  subentrato. Contiene nomi di server e di account, ma **mai** password, token di
  sessione, richiesta di sessione PSM (`PSM@…` mascherata), firma, intestazione o corpo delle richieste, né il
  contenuto delle sessioni. La barra di stato lo segnala finché è attivo. Rileggilo prima di trasmetterlo, ed
  eliminalo una volta risolto il problema.
- **File modificati**: la copia locale aperta nell'editor si trova in `%TEMP%\CyberArkTerm\edit` e viene eliminata
  alla chiusura della scheda SSH; un avviso segnala le modifiche non rinviate.
- **Nessuna iniezione di comandi**: percorsi SCP e cartelle iniziali protetti tra apici per la shell remota;
  argomenti `ssh` / Windows Terminal convalidati e passati senza shell.
- Esportazione CSV protetta contro l'iniezione di formule Excel.
- **File di server ed elenchi condivisi**: nessuna password né token, solo nomi di server, account e safe, ID degli
  account e impostazioni di connessione (il motivo predefinito non viene mai condiviso). Non danno alcun accesso:
  ognuno si connette con i propri diritti CyberArk, e la descrizione comandi mostra l'account come lo descrive il
  vault. Una macchina di destinazione proveniente da un elenco condiviso e non consentita per l'account da CyberArk
  viene confermata prima della prima connessione. L'autore annotato nel registro (account CyberArk e account
  Windows) è dichiarativo: fa fede l'audit della condivisione di rete. Un file di oltre 8 MB viene rifiutato.
- Le sessioni PSM e PSMP aperte da CyberArkTerm sono sessioni CyberArk standard: vengono registrate e verificate dal
  PSM come quelle aperte dal PVWA.

Per segnalare una vulnerabilità, vedi [SECURITY.md](../SECURITY.md) (segnalazione privata, nessuna issue pubblica).

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

### Elenchi condivisi

File JSON (`"format": "CyberArkTerm.SharedServers"`, versione 1): nome, PVWA di origine, revisione, cartelle,
server (con chi li ha aggiunti e quando) e registro delle modifiche (le ultime 1.000). Ogni modifica apre il file
in esclusiva (gli altri computer riprovano per 5 s), lo rilegge, copia la revisione corrente in
`nome.versions\nome.r00012.20261006-101500.json` (revisione e data del suo salvataggio, 100 versioni conservate),
applica la modifica, aumenta la revisione, annota chi, quando e cosa, poi riscrive il file (rimesso com'era se la
scrittura fallisce). La visualizzazione segue le modifiche del file (`FileSystemWatcher`) e lo rilegge con `F5`.
L'esportazione di «I miei server» ha lo stesso formato con `"format": "CyberArkTerm.Servers"`, senza revisione né
registro.

### Archivi KeePass

Lettura e scrittura native (senza KeePass installato) dei formati **KDBX 3.1 e 4.x**: cifratura AES-256 o ChaCha20,
derivazione della chiave AES-KDF (istruzioni AES del processore) o Argon2d / Argon2id, file chiave XML 1.0 / 2.0, 32
byte, 64 caratteri esadecimali o file qualsiasi. Il file riscritto mantiene la versione, la cifratura e la
derivazione della chiave originali, con nuovi semi a ogni salvataggio. Gli archivi di test
(`tests/CyberArkTerm.Core.Tests/KeePass/Vaults`) provengono da KeePassXC e pykeepass, e i file scritti da
CyberArkTerm sono stati verificati in entrambi gli strumenti.

### Sessioni VNC

Client integrato (protocollo RFB 3.3, 3.7 e 3.8, RFC 6143; a un server più recente, come RealVNC 4 o 5, si risponde
in 3.8), nulla da installare: autenticazione «nessuna» o «password VNC» (se il server le propone entrambe: la password
se la voce ne ha una, altrimenti nessuna) (DES del protocollo, implementato in CyberArkTerm perché la modalità FIPS di Windows può vietare DES),
codifiche Raw, CopyRect e Hextile, cambio di dimensione dello schermo, pixel a 32 bit. La tastiera è inviata come
«keysym» X11 (i caratteri AltGr sono inviati come caratteri), la rotellina come pulsanti 4 e 5.

### Sessioni di file FTP / FTPS

Libreria FluentFTP (licenza MIT). Modalità passiva (`EPSV` / `PASV`), binaria, `PBSZ 0` e `PROT P` con TLS;
certificato verificato da Windows, altrimenti fissato (`ftps://server:porta` tra le chiavi dei server accettate,
nelle Impostazioni). FTP non ha una somma di controllo standard: ogni invio è riletto dal server e confrontato con
SHA-256. Lettura parziale (`REST`) per il confronto e il monitoraggio in tempo reale. Dopo un trasferimento
interrotto, la connessione è riaperta e il file incompleto eliminato. Un file sostituito viene riscritto sul posto:
conserva i suoi permessi. Collegamenti simbolici: i primi 40 di una cartella vengono risolti (un'andata e ritorno
ciascuno); oltre, un collegamento appare come un file, e aprirlo entra nella cartella se è una cartella.

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
errori di connessione sono spiegati nella scheda con il messaggio e i codici di Windows (motivo, motivo esteso). Un
test di integrazione (workflow `rdp-integration`) apre una vera sessione sul computer di CI.

**Un thread per connessione desktop remoto.** Il controllo, la sua finestra e i suoi eventi vivono su un thread a
parte (STA, con il proprio ciclo di messaggi); l'interfaccia non lo attende mai. La scheda contiene una finestra del
thread dell'interfaccia, in cui quel thread colloca la finestra del controllo. Prima di rilasciare il controllo, ve
la toglie: una disconnessione o un rilascio lento non blocca più l'applicazione. Se il thread non risponde per 5 s,
la barra della scheda lo segnala, e il resto dell'applicazione resta utilizzabile. Limite: Windows condivide
tastiera e mouse tra una finestra e quelle che contiene, anche di un altro thread; un controllo bloccato
definitivamente può ancora trattenere un clic nella sua area o un cambio di focus.

### Sessioni PSMP

Ogni scheda SSH apre fino a tre connessioni al PSMP, con lo stesso identificativo
`<tu>@<account>[#dominio]@<destinazione>`: il terminale, la connessione SFTP della scheda File e una connessione SCP
al primo invio in SCP (scelto nelle Impostazioni, o subentrato a un invio SFTP rifiutato). Ognuna è una sessione PSMP, registrata dal PSM. Gli invii al server (digitazione, dimensione
del terminale) e la chiusura delle connessioni avvengono fuori dal thread dell'interfaccia, in ordine: un server o
un PSMP che non legge più non blocca l'applicazione.

### Monitoraggio della cartella del terminale

All'apertura di una sessione SSH (se l'opzione è attiva), CyberArkTerm attende che la shell del server di
destinazione mostri il prompt (fino a 60 s: il PSMP a volte impiega diversi secondi a raggiungere la destinazione),
poi le invia un comando di una riga, preceduto da uno spazio per non finire nella cronologia (bash, o zsh con
`HIST_IGNORE_SPACE`). Non viene inviato nulla se hai già iniziato a digitare; il comando può essere reinviato senza
effetti doppi (casella «Segui»):

- definizione di `PROMPT_COMMAND` (bash) o `precmd` (zsh) che emette la sequenza standard **OSC 7** con la cartella
  corrente a ogni prompt;
- con tcsh, l'alias `cwdcmd` (solo se non è già definito), che emette la stessa sequenza a ogni cambio di cartella;
- se è configurata una cartella iniziale, un `cd` verso quella cartella;
- cancellazione del comando digitato, perché non resti sullo schermo.

Il terminale integrato decodifica la sequenza OSC 7 e la scheda File si posiziona nella cartella indicata.

Tutte le shell leggono il comando senza errori: ogni parte viene eseguita solo dalla famiglia di shell a cui è
destinata. Con csh, ksh, sh o fish il monitoraggio non viene installato e sullo schermo non resta nulla.

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
| Un invio indica «SFTP (SCP rifiutato)» o «SCP (SFTP rifiutato)» | Il PSMP o il server ha rifiutato quel protocollo per questo file: l'altro è subentrato e il file è stato verificato come al solito. Il riepilogo riporta la risposta del server. Un PSMP che rifiuta SCP per una piattaforma (errore `118E Selected component PSMP-SCP does not contain the target settings definitions…` nei suoi log) non ha il componente di connessione PSMP-SCP: il tuo team CyberArk può aggiungerlo alla piattaforma, altrimenti gli invii passano in SFTP. |
| Il browser non segue i `cd` | La shell remota non è bash, zsh o tcsh (o tcsh ha già un proprio alias `cwdcmd`), l'opzione è disattivata nelle Impostazioni, oppure il prompt non è stato riconosciuto: riseleziona «Segui la cartella del terminale» al prompt della shell. |
| Avviso «la chiave del PSMP è cambiata» | Prosegui solo se il team CyberArk conferma una modifica del server. |
| «Password principale o file chiave errati.» | Controlla la password e il file chiave; un archivio protetto da YubiKey non è supportato. |
| L'archivio KeePass chiede la password nonostante «Memorizza» | Vault locale bloccato («Più tardi» allo sblocco) o password principale cambiata altrove: digitala, viene memorizzata di nuovo. |
| «Il file del vault locale è danneggiato o è stato creato da un altro account Windows.» | Il vault locale non segue un cambio di computer o di account: eliminalo nelle Impostazioni e ricrealo. |
| «La voce … è stata modificata o eliminata nell'archivio nel frattempo» | Qualcuno ha cambiato la stessa voce altrove: l'archivio viene ricaricato, rifai la modifica. |
| Un account Unix si apre con il PSM e non in SSH | Indirizzo del PSMP non impostato nelle Impostazioni, oppure account non riconosciuto come Unix: clic destro → «Connetti in SSH». |
| Un account si apre in soli file e non in un terminale | Il nome della sua piattaforma contiene «SFTP»: clic destro → «Connetti in SSH (PSMP)», o «Proprietà…» in «I miei server» per cambiare il tipo di connessione. |
| «L'elenco condiviso è in corso di modifica da parte di qualcun altro» | Un altro computer scrive l'elenco da più di 5 secondi, o tiene il file aperto: riprova tra un momento. |
| «non hai il diritto di modificare questo file (diritti della condivisione di rete)» | La condivisione è in sola lettura per te: chiedi il diritto di scrittura al suo responsabile. L'elenco resta utilizzabile. |
| Un elenco condiviso mostra «(illeggibile)» | Condivisione irraggiungibile o file danneggiato: la descrizione comandi riporta l'errore. Se il file è danneggiato, copia al suo posto la versione più recente della cartella `nome.versions`. |
| Capire un errore di connessione | Impostazioni → Registro di debug, riproduci il problema, poi Impostazioni → «Mostra il file del registro». |
| Una scheda di desktop remoto diretto (KeePass) mostra «Errore del controllo Desktop remoto» | Segnala il codice mostrato (se il controllo Desktop remoto manca dal computer, la connessione passa da `mstsc`). |
| VNC: «Il server VNC non propone alcuna autenticazione supportata da CyberArkTerm…» | Il server richiede un'autenticazione propria del suo produttore (account Windows, cifratura VeNCrypt…): attiva l'autenticazione «password VNC» sul server. |
| VNC: «Nessuna risposta VNC dal server entro 30 secondi» | Porta sbagliata (5900 + numero dello schermo) o servizio diverso da VNC a questo indirizzo. |
| FTP: «Il server FTP non propone la cifratura (TLS), richiesta da questa voce» | Il server non accetta TLS: usa `ftp://` (connessione in chiaro dopo conferma) o SFTP se disponibile. |
| FTPS: l'elenco dei file non appare o un trasferimento scade | Un firewall blocca le porte passive del server, o il server esige la ripresa della sessione TLS sulle connessioni dati (`522`, per esempio `require_ssl_reuse` di vsftpd): rivolgiti all'amministratore del server. |
