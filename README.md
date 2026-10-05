<img src="docs/icone.png" alt="" width="72" align="right">

# CyberArkTerm

**Français** · [English](README.en.md) · [Italiano](README.it.md)

[![build](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml/badge.svg)](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/muller-camille/CyberArkTerm)](https://github.com/muller-camille/CyberArkTerm/releases/latest)

**Client Windows multi-sessions pour CyberArk.** CyberArkTerm se connecte à votre PVWA, liste les comptes
auxquels vous avez accès et ouvre vos sessions en un double-clic : bureau à distance via **PSM**, ou terminal
SSH via **PSM for SSH (PSMP)** avec un **navigateur de fichiers** intégré pour déposer des fichiers sur le
serveur.

![Session SSH via le PSMP, avec l'onglet Fichiers qui suit le dossier du terminal](docs/captures/ecran-principal.png)

> Les captures proviennent d'un environnement de démonstration (données fictives).

## Sommaire

- [Fonctionnalités](#fonctionnalités)
- [Installation](#installation)
- [Prise en main](#prise-en-main)
- [Raccourcis](#raccourcis)
- [Paramètres et fichier de configuration](#paramètres-et-fichier-de-configuration)
- [Sécurité](#sécurité)
- [Fonctionnement technique](#fonctionnement-technique)
- [Dépannage](#dépannage)
- [Développement](#développement)
- [Limites et pistes](#limites-et-pistes)
- [Licence](#licence)

## Fonctionnalités

| | |
| --- | --- |
| **Connexion CyberArk** | Authentification CyberArk, LDAP, RADIUS (challenge / OTP compris) ou Windows (session courante). |
| **Disponibles** | Tous les comptes visibles dans le coffre, groupés par safe, plateforme ou type de cible, avec recherche instantanée. |
| **Courants** | Vos serveurs de travail, rangés en dossiers et sous-dossiers, chacun avec sa propre configuration. |
| **Sessions PSM** | Bureau à distance via le PSM (comme le bouton « Connect » du PVWA), dans la Connexion Bureau à distance de Windows : composant, machine cible, motif, ticket. |
| **Sessions SSH (PSMP)** | Terminal intégré en onglet (compatible xterm : couleurs, vim, less, top…), authentification MFA. |
| **Onglet Fichiers** | Navigateur SFTP du serveur : `ls`, navigation, `rm`, dépôt de fichiers par glisser-déposer en SCP, vérification SHA-256 de chaque fichier transféré, modification dans votre éditeur de texte, droits (`chmod`), suivi du dossier du terminal. |
| **Accès d'urgence (KeePass)** | Sans CyberArk : coffres KeePass (.kdbx) dans « Courants », connexions SSH et bureau à distance directes, création et modification des entrées, journal local. |
| **Session PVWA maintenue** | Une requête légère toutes les 4 minutes évite l'expiration pendant le travail (suspendue quand Windows est verrouillé). |
| **Accueil** | Connexion rapide (tapez un serveur, Entrée), sessions récentes. |
| **Export** | Liste des comptes en CSV, ouvrable directement dans Excel (séparateur selon la région Windows). |
| **Langues** | Interface en français, anglais et italien : langue de Windows par défaut, modifiable à tout moment. |

## Installation

### Télécharger l'exécutable

1. Ouvrez la [dernière version](https://github.com/muller-camille/CyberArkTerm/releases/latest) dans les
   *Releases* du dépôt.
2. Téléchargez **`CyberArkTerm-<version>-win-x64.zip`** et décompressez-le (l'empreinte SHA256 est dans
   `SHA256SUMS.txt`).
3. Lancez `CyberArkTerm.exe` : un seul fichier, aucun runtime à installer, aucun droit administrateur requis.

Version de développement : l'exécutable de chaque compilation est aussi disponible en artefact
`CyberArkTerm-win-x64` dans l'onglet [Actions](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml).

L'exécutable n'est pas signé : au premier lancement, Windows SmartScreen peut afficher un avertissement
(« Informations complémentaires » → « Exécuter quand même »).

### Prérequis

**Poste de travail**

- Windows 10 ou 11 (x64).
- Le client Bureau à distance de Windows (présent par défaut) : la Connexion Bureau à distance (`mstsc`) pour les
  sessions PSM, son contrôle intégré pour le bureau à distance direct des coffres KeePass.
- Facultatif : Windows Terminal et le « Client OpenSSH » de Windows, uniquement si vous choisissez d'ouvrir
  le SSH hors de CyberArkTerm.

**Côté CyberArk**

- PVWA **v10 ou supérieur** (API REST `/PasswordVault/API/...`), joignable en **HTTPS** avec un certificat
  approuvé par le poste.
- Droit **List accounts** sur les safes concernés : l'application n'affiche que ce que l'API vous laisse voir.
- PSM configuré sur les plateformes à utiliser (composants `PSM-RDP`, `PSM-SSH`…).
- Pour le SSH : un **PSM for SSH (PSMP)**, avec SFTP autorisé pour l'onglet Fichiers (et SCP pour le dépôt
  en SCP).
- Facultatif : **MFA caching** activé sur le PVWA, pour éviter de ressaisir mot de passe et MFA au PSMP.

## Prise en main

### 1. Se connecter au coffre

<img src="docs/captures/connexion.png" alt="Fenêtre de connexion" width="440">

Saisissez l'adresse du PVWA (`pvwa.mondomaine.local` suffit : `https://` et `/PasswordVault` sont ajoutés),
choisissez la méthode d'authentification, puis votre identifiant et votre mot de passe. Si le serveur RADIUS
pose une question (code OTP), la fenêtre l'affiche et attend votre réponse.

L'adresse, la méthode et l'identifiant sont mémorisés ; **le mot de passe ne l'est jamais**.

La liste en bas à gauche change la langue de l'interface (Français, English, Italiano) ; la fenêtre se
rouvre aussitôt dans la langue choisie, sans perdre l'adresse ni l'identifiant saisis.

### 2. Trouver un compte : onglet « Disponibles »

![Onglet Disponibles filtré sur un compte de domaine](docs/captures/disponibles.png)

- La zone de recherche filtre sur tous les champs (serveur, utilisateur, safe, plateforme, domaine…),
  plusieurs mots possibles (`prd sql`).
- « Grouper par » range les comptes par safe, plateforme ou type de cible.
- Le bouton « Exporter » de la barre d'outils enregistre en CSV les comptes affichés (filtrés par la recherche).
- **Membres d'un safe** : clic droit sur un compte (ou sur un safe quand les comptes sont groupés par safe, ou sur
  un serveur de « Courants ») → « Membres du safe ». La fenêtre liste les utilisateurs et groupes du safe avec
  leurs droits (lister, utiliser, récupérer, ajouter des comptes, modifier, supprimer, gérer les membres…), indique
  qui peut **ajouter des comptes**, et détaille tous les droits du membre sélectionné. Le PVWA ne donne cette
  liste qu'à un compte qui a le droit « View Safe Members » sur le safe. `Ctrl+A` puis `Ctrl+C` copie le tableau.
  Avec le droit « Gérer les membres du safe », les boutons « Ajouter un membre… », « Modifier les droits… » (ou
  double-clic) et « Retirer… » gèrent les membres : nom, type (utilisateur ou groupe), annuaire (« Vault » ou le
  domaine LDAP), date de fin éventuelle et les 22 droits, regroupés comme dans le PVWA.
- **Ajouter un compte** : clic droit sur un compte (ou sur un safe quand les comptes sont groupés par safe) →
  « Ajouter un compte dans le safe… ». Safe, plateforme, adresse et utilisateur sont obligatoires ; domaine de
  connexion, nom du compte, mot de passe, machines autorisées et gestion par le CPM sont facultatifs. Le compte
  cliqué sert de modèle (safe, plateforme, domaine). Le compte est créé avec les droits de votre session : il faut
  le droit « Ajouter des comptes » sur le safe, et en général « Modifier le contenu des comptes » pour fournir le
  mot de passe. La liste est rechargée ensuite et le nouveau compte sélectionné.
- **Importer des comptes (CSV)** : bouton « Importer » de la barre d'outils, ou clic droit sur un compte ou un safe →
  « Importer des comptes (CSV)… ». Une petite fenêtre demande le fichier (« Enregistrer un modèle… » donne un
  exemple), le safe et la plateforme par défaut, et résume ce qui sera créé ; rien n'est envoyé avant « Importer ».
  Colonnes obligatoires : adresse et utilisateur (plus safe et plateforme, sinon les valeurs par défaut) ;
  facultatives : nom, domaine, mot de passe, machines autorisées, gestion CPM (oui/non), motif. Séparateur `;`, `,` ou
  tabulation, noms de colonnes en français, anglais ou italien ; un fichier produit par « Exporter » se réimporte.
  Une seconde fenêtre crée ensuite les comptes ligne par ligne et affiche l'état de chacune (créé, refusé avec le
  message du PVWA, non importé, non envoyé ; « Arrêter » possible). À la fin, elle propose d'enregistrer le résultat
  en CSV (sans les mots de passe). Les mots de passe du fichier ne sont jamais affichés ; supprimez le fichier après
  l'import.
- **Modifier / supprimer un compte** : clic droit → « Modifier le compte… » (plateforme, adresse, utilisateur,
  domaine, nom, machines autorisées, gestion par le CPM ; seuls les champs changés sont envoyés) ou « Supprimer le
  compte… » (après confirmation). Droits « Modifier les propriétés des comptes » et « Supprimer des comptes ».
- **État du mot de passe (CPM)** : l'info-bulle d'un compte indique s'il est géré par le CPM, la date du dernier
  changement, de la dernière vérification et de la dernière réconciliation ; un **⚠** signale un compte dont la
  dernière opération du CPM a échoué.
- **Clic droit → « Mot de passe »** (comptes de « Disponibles » et serveurs de « Courants ») :
  - « Vérifier », « Changer… », « Réconcilier… » demandent l'opération au CPM (confirmation pour changer et
    réconcilier ; droit « Lancer les opérations CPM »). Le CPM la traite ensuite : `F5` pour voir le nouvel état.
  - « Copier le mot de passe… » : motif et ticket si la plateforme l'exige, puis le mot de passe est copié dans le
    presse-papiers pendant 20 secondes, **sans être affiché** (droit « Récupérer les comptes » ; la récupération est
    inscrite dans l'audit du coffre).
- Sur l'accueil, la **connexion rapide** trouve un serveur au fil de la frappe : Entrée pour s'y connecter.

### 3. Ouvrir une session PSM (bureau à distance)

Double-cliquez sur le compte (ou Entrée, ou bouton « Se connecter ») ; un compte Unix s'ouvre en SSH via le PSMP
quand son adresse est renseignée (voir 4.), et « Connexion avancée… » permet alors de choisir le PSM. CyberArkTerm
demande la connexion au PVWA et ouvre la session dans la **Connexion Bureau à distance** de Windows (`mstsc`),
exactement comme le bouton « Connect » du PVWA : le fichier RDP du PVWA lui est donné tel quel. Un composant en
application distante (RemoteApp) ouvre ses fenêtres sur le bureau du poste.

![Connexion à un compte de domaine : machine cible, motif exigé par le PVWA](docs/captures/connexion-psm.png)

- **Composant PSM** : déduit de la plateforme (`PSM-RDP` pour Windows, `PSM-SSH` pour Unix et réseau,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`…). Cochez « Mémoriser ce composant » pour le conserver pour
  toute la plateforme. Votre PVWA peut nommer ses composants autrement (par exemple `WIN-PSM`) : saisissez le nom
  que propose son bouton « Connect » ; la liste propose ensuite les composants déjà utilisés, celui de la
  plateforme en premier.
- **Comptes de domaine** : la fenêtre demande la machine cible, pré-remplie avec les machines autorisées du
  compte.
- **Motif et ticket** : si le PVWA refuse la demande (motif obligatoire, composant non configuré…), son
  message s'affiche et vous pouvez corriger puis réessayer.
- Le bouton « Connexion… » (ou clic droit → « Connexion avancée… ») ouvre cette fenêtre à la demande.

### 4. Ouvrir une session SSH via le PSMP

Renseignez une fois l'adresse du PSMP dans **Paramètres** : les comptes Unix s'ouvrent alors en SSH par défaut
(double-clic ou Entrée). Pour un autre compte, clic droit → « Se connecter en SSH » (ou bouton « SSH »).

La session s'ouvre **dans un onglet de CyberArkTerm**, avec l'identifiant PSMP standard
`<vous>@<compte cible>[#domaine]@<serveur cible>`. Les noms d'utilisateur contenant des espaces
(`Jean Dupont`, `Admin Local`) sont acceptés.

<img src="docs/captures/authentification-psmp.png" alt="Question d'authentification posée par le PSMP" width="640">

- **Authentification** : si le PVWA fournit une clé « MFA caching », aucune question n'est posée. Sinon, les
  questions du PSMP (mot de passe, code MFA) s'affichent ; le mot de passe est réutilisé pour les connexions
  SFTP et SCP du même onglet, jamais enregistré.
- **Clé du PSMP** : à la première connexion, son empreinte SHA256 est affichée et doit être acceptée ; si elle
  change ensuite, une alerte s'affiche.
- **Terminal** : la sélection copie, le clic droit colle, la molette remonte l'historique,
  AltGr fonctionne sur clavier français. Fermez l'onglet avec la croix ou un clic molette.

### 5. Parcourir et déposer des fichiers : onglet « Fichiers »

À l'ouverture d'une session SSH, l'onglet **Fichiers** s'affiche sur le côté et suit l'onglet SSH actif.

![Dépôt d'un fichier par glisser-déposer depuis l'Explorateur](docs/captures/depot-glisser-deposer.png)

- **Barre de navigation** : chemin courant, modifiable (tapez un chemin puis Entrée). Double-clic sur un
  dossier pour y entrer, `..` pour remonter, boutons « dossier parent » et « dossier personnel ».
- **Déposer des fichiers** : glissez-les depuis l'Explorateur sur la liste (ou bouton « Envoyer »). Envoi en
  **SCP** par défaut (SFTP en option), dossiers compris ; confirmation avant d'écraser un fichier existant.
- **Télécharger en glissant** : glissez des fichiers ou des dossiers de la liste vers l'Explorateur ou le
  bureau. Rien n'est téléchargé pendant le glissement : au dépôt, une fenêtre montre la progression (Annuler
  l'interrompt), puis l'Explorateur copie les fichiers là où vous les avez déposés. Les noms Unix sont rendus
  valides pour Windows (`\`, `:`, `..`, `CON`… remplacés), sans jamais écrire hors du dossier de dépôt ; le
  dossier temporaire du téléchargement est effacé ensuite.
- **File d'attente des transferts** : envois et téléchargements s'exécutent un par un, dans l'ordre des demandes ;
  ce qui est demandé pendant un transfert s'ajoute à la file au lieu d'être ignoré. La destination d'un envoi est le
  dossier affiché au moment du dépôt, et la confirmation d'écrasement tient compte des envois encore en attente. Un
  panneau au-dessus de la barre d'état montre chaque élément (en attente, avancement et fichier n/N, vérification,
  résultat) : ✕ retire un élément en attente, « Annuler » arrête celui en cours, « Tout annuler » vide la file.
  Un transfert arrêté supprime le fichier en cours, incomplet (sur le serveur pour un envoi, sur le poste pour un
  téléchargement) ; les fichiers déjà transférés restent. Attention : si l'envoi remplaçait un fichier existant,
  son ancien contenu est perdu. En SCP, l'arrêt ferme la connexion SCP ; l'envoi SCP suivant en rouvre une (nouvelle
  session PSMP). Une erreur est affichée dans la file et la file continue ; à la fin, un seul bilan. La navigation,
  la suppression, les droits, l'éditeur et le glisser vers l'Explorateur passent entre deux fichiers. Fermer
  l'onglet ou l'application avec des transferts en cours demande confirmation.
- **Beaucoup de fichiers d'un coup : archive .tar.gz** : à partir de 200 fichiers déposés (seuil réglable, option
  « Proposer une archive .tar.gz » des Paramètres), CyberArkTerm propose de les envoyer dans une seule archive :
  un fichier à transférer et à vérifier au lieu de milliers, beaucoup plus rapide via le PSMP. L'archive est créée
  sur le poste (dans la file, annulable), envoyée et vérifiée (SHA-256), puis supprimée du poste ; chaque élément
  déposé est à la racine de l'archive (droits 0644 et 0755). Rien n'est exécuté sur le serveur : « Copier la
  commande d'extraction » (barre d'état) donne la commande à coller dans le terminal, par exemple
  `cd '/opt/app' && gzip -dc './deploy.tar.gz' | tar -xf - && rm -f './deploy.tar.gz'` (l'archive est supprimée
  du serveur une fois extraite). « Envoyer les fichiers un par un » garde l'envoi habituel ; « Ne plus proposer »
  décoche l'option.
- **Historique des transferts** : bouton horloge dans l'en-tête de l'onglet Fichiers, disponible même sans
  session. Il liste les 200 derniers envois et téléchargements (y compris par glisser-déposer) : date, sens,
  serveur, élément, destination, nombre de fichiers, résultat. Filtre « Envois » / « Téléchargements » ;
  « Sommes de contrôle… » (ou double-clic) montre les sommes SHA-256 de chaque fichier, à recopier pour revérifier
  plus tard ; « Ouvrir le dossier » pour un téléchargement ; « Effacer l'historique ».
- **Vérification des transferts (SHA-256)** : chaque fichier envoyé ou téléchargé est vérifié. À l'envoi (SCP ou
  SFTP), le fichier local est haché, puis le fichier arrivé sur le serveur est relu par SFTP et haché. Au
  téléchargement, les données reçues du serveur sont hachées, puis le fichier écrit sur le poste est relu. La barre
  d'état confirme « ✓ identique des deux côtés » ; « Sommes de contrôle… » montre, pour chaque fichier, la taille,
  les deux sommes et le résultat, et copie les sommes au format de `sha256sum -c` pour revérifier sur le serveur.
  Si un fichier diffère, l'erreur est affichée et le détail s'ouvre ; un téléchargement par glisser-déposer
  échoue plutôt que de livrer une copie fausse. Un fichier qui ne peut pas être relu (droits) est signalé
  « non vérifié ». La relecture d'un envoi double le volume échangé avec le serveur.
- **Supprimer** : sélection puis Suppr (ou clic droit → « Supprimer (rm) »), avec confirmation. Les dossiers
  doivent être vides.
- **Modifier un fichier** : sélection puis `F4` (ou clic droit → « Modifier », ou bouton crayon). Le fichier
  s'ouvre dans l'éditeur de texte choisi dans les Paramètres (Bloc-notes par défaut). À chaque enregistrement,
  CyberArkTerm propose de le renvoyer sur le serveur : envoi en SFTP, droits du fichier conservés. Si le
  fichier a changé sur le serveur depuis son ouverture, une alerte demande confirmation avant de l'écraser.
- **Droits** : clic droit → « Droits… » (ou bouton cadenas). Cases lecture / écriture / exécution pour le
  propriétaire, le groupe et les autres, bits spéciaux (setuid, setgid, sticky) et valeur octale (`644`,
  `1777`…), pour un ou plusieurs éléments. Pour un dossier, l'option « Appliquer aussi au contenu » propage
  les droits aux sous-dossiers et fichiers ; par défaut, l'exécution (x) n'est donnée qu'aux dossiers et aux
  fichiers déjà exécutables. Les liens symboliques ne sont pas suivis, le propriétaire n'est pas modifié.
- Aussi : nouveau dossier, téléchargement, copie du chemin, affichage des fichiers cachés.
- **Suivre le dossier du terminal** : quand la case est cochée, chaque `cd` dans le terminal déplace le
  navigateur dans le même dossier (voir [Fonctionnement technique](#fonctionnement-technique)). Après un
  `sudo -i` ou un `su`, recochez la case à l'invite du shell pour réactiver le suivi dans ce nouveau shell.

### 6. Organiser ses serveurs : onglet « Courants »

![Serveurs courants rangés en dossiers](docs/captures/courants.png)

- **Ajouter** un compte : clic droit dans « Disponibles » → « Ajouter aux serveurs courants » puis le
  dossier voulu, ou glissez le compte sur l'onglet « Courants », ou bouton « Courant » de la barre d'outils.
- **Ajouter une session récente** : clic droit dans « Sessions récentes » sur l'accueil → « Ajouter aux
  serveurs courants » puis le dossier voulu. Le serveur garde le type de connexion (PSM ou SSH), le composant
  PSM et la machine cible utilisés.
- **Dossiers** : clic droit → nouveau dossier ou sous-dossier, renommer, supprimer ; glissez serveurs et
  dossiers pour les déplacer.
- **Rechercher** : champ en haut de l'onglet (ou `Ctrl+F` dans l'onglet). Il filtre les serveurs par nom, serveur,
  utilisateur, dossier, composant, machine cible, ainsi que les entrées des coffres KeePass déverrouillés ; les
  dossiers des résultats sont dépliés. `Entrée` ou `↓` sélectionne le premier résultat, `Échap` efface.
- **Configuration propre à chaque serveur** (clic droit → « Propriétés… ») :

<img src="docs/captures/proprietes-serveur.png" alt="Propriétés d'un serveur courant" width="800">

| Réglage | Effet |
| --- | --- |
| Nom, dossier | Affichage et rangement dans l'arbre. |
| PSM ou SSH via PSMP | Type de connexion ouvert au double-clic. |
| Composant PSM | Composant à utiliser (vide : déduit de la plateforme). |
| Machine cible | Serveur sur lequel ouvrir la session pour un compte de domaine. |
| Motif par défaut | Motif d'accès envoyé automatiquement au PVWA. |
| Dossier SFTP de départ | Le terminal **et** le navigateur de fichiers s'ouvrent directement dans ce dossier. |

Un serveur dont le compte n'est plus visible dans CyberArk apparaît grisé.

### 7. Accès d'urgence hors CyberArk : coffres KeePass

Quand CyberArk est indisponible, CyberArkTerm ouvre vos coffres KeePass (`.kdbx`) et se connecte **directement**
aux serveurs, en SSH ou en bureau à distance, avec les comptes qu'ils contiennent.

> Ces connexions **ne passent pas par le PSM** : ni enregistrement, ni règles CyberArk. Chaque ouverture de
> coffre, connexion et modification est notée dans le journal local `%APPDATA%\CyberArkTerm\urgence.log`.

![Accès d'urgence : coffre KeePass déverrouillé dans « Courants »](docs/captures/coffre-keepass.png)

- **Sans CyberArk** : sur l'écran de connexion, « Accès d'urgence (KeePass) » ouvre la fenêtre principale sans
  PVWA (seuls les coffres KeePass y figurent). Avec CyberArk, les coffres apparaissent aussi en tête de l'onglet
  « Courants ».
- **Ajouter un coffre** : bouton coffre-fort de l'onglet « Courants » (ou clic droit → « Ajouter un coffre
  KeePass… ») : fichier `.kdbx`, nom, fichier clé éventuel.
- **Déverrouiller** : double-clic sur le coffre. Mot de passe maître et/ou fichier clé (tous les formats de
  KeePass). « Mémoriser le mot de passe maître dans le coffre local » évite de le ressaisir (voir ci-dessous).
- **Se connecter** : double-clic sur une entrée. Le protocole vient de son adresse (`ssh://serveur:22`,
  `rdp://serveur`, `serveur:3389`), d'un champ « Protocol » / « Port » ou d'une étiquette `ssh` / `rdp` ; sinon
  CyberArkTerm demande SSH ou bureau à distance. Le mot de passe de l'entrée est utilisé directement (onglet
  terminal + Fichiers en SSH, onglet bureau à distance en RDP) ; il n'est jamais affiché ni écrit sur disque.
  L'onglet bureau à distance suit sa taille (résolution du bureau distant), propose « Plein écran »
  (`Ctrl+Alt+Pause` pour revenir), « Déconnecter » et « Reconnecter ».
- **Modifier le coffre** : clic droit → « Nouvelle entrée… », « Modifier… » (`F2`), « Supprimer » (`Suppr`,
  vers la corbeille du coffre). Les autres données du coffre (pièces jointes, champs, réglages) sont gardées ;
  l'ancienne version d'une entrée va dans son historique, comme dans KeePass.
- **Verrouiller** : clic droit → « Verrouiller ». Les coffres se verrouillent aussi à la déconnexion, à la
  fermeture et au **verrouillage de Windows**.

**Coffre local** : les mots de passe maîtres que vous choisissez de mémoriser sont gardés dans
`%APPDATA%\CyberArkTerm\coffre-local.dat`, chiffré avec un mot de passe à vous (demandé au déverrouillage
d'un coffre KeePass dont le mot de passe est mémorisé, « Plus tard » pour saisir plutôt le mot de passe du coffre)
et lié à votre compte Windows. Gestion dans les **Paramètres** :
créer, déverrouiller, changer le mot de passe, supprimer.

## Raccourcis

| Où | Action | Raccourci |
| --- | --- | --- |
| Partout | Recharger les comptes depuis le PVWA | `F5` |
| Partout | Filtrer les comptes (dans « Courants » : rechercher un serveur) | `Ctrl+F` |
| Listes et arbres | Ouvrir la session | Double-clic ou `Entrée` |
| Recherche | Effacer le filtre | `Échap` |
| Courants | Renommer / retirer ou supprimer | `F2` / `Suppr` |
| Terminal | Copier | Sélection à la souris, ou `Ctrl+Maj+C` |
| Terminal | Coller | Clic droit, `Maj+Inser` ou `Ctrl+Maj+V` |
| Terminal | Historique | Molette, `Maj+Page préc.` / `Maj+Page suiv.` |
| Onglet SSH ou Bureau à distance | Fermer | Croix de l'onglet ou clic molette |
| Onglet SSH ou Bureau à distance | Reconnecter, dupliquer (autre session sur le même compte ou la même entrée), fermer, fermer les autres onglets | Clic droit sur l'onglet |
| Bureau à distance | Plein écran / retour | `Ctrl+Alt+Pause` |
| Fichiers | Ouvrir / modifier / dossier parent / supprimer / actualiser | `Entrée` / `F4` / `Retour arrière` / `Suppr` / `F5` |
| Coffre KeePass | Se connecter / modifier / supprimer une entrée | Double-clic ou `Entrée` / `F2` / `Suppr` |

## Paramètres et fichier de configuration

![Paramètres](docs/captures/parametres.png)

| Paramètre | Rôle | Défaut |
| --- | --- | --- |
| Langue de l'interface | Français, English, Italiano ou langue du système ; appliquée après déconnexion ou au prochain démarrage | langue de Windows (anglais si elle n'est pas traduite) |
| Garder la session PVWA ouverte | Requête légère toutes les 4 minutes ; suspendue quand Windows est verrouillé | oui |
| Coffre local | Mots de passe maîtres KeePass mémorisés : créer, déverrouiller, changer le mot de passe, supprimer | — |
| Journal de débogage | Menu du bouton Paramètres : déroulement des connexions dans un fichier, sans secret (voir [Sécurité](#sécurité)) ; « Afficher le fichier du journal » l'ouvre dans l'Explorateur | non |
| Adresse et port PSMP | Serveur PSM for SSH ; renseigné, les comptes Unix s'ouvrent en SSH par défaut ; vide = SSH désactivé | vide, 22 |
| SSH dans CyberArkTerm | Terminal et onglet Fichiers intégrés ; sinon Windows Terminal | oui |
| Suivre le dossier du terminal | Autorise l'installation du suivi de dossier dans le shell | oui |
| Dépôt de fichiers | SCP ou SFTP | SCP |
| Éditeur de texte | Programme ouvert par « Modifier » dans l'onglet Fichiers | Bloc-notes |
| Clés de PSMP acceptées | Empreintes mémorisées (bouton « Oublier les clés ») | — |
| Composants mémorisés | Composant PSM choisi par plateforme (bouton « Oublier ») | — |

Toutes les préférences sont enregistrées dans `%APPDATA%\CyberArkTerm\settings.json` : langue, adresse du PVWA,
méthode et identifiant de connexion, paramètres ci-dessus, serveurs « Courants » et leurs dossiers, sessions
récentes, emplacement des coffres KeePass et de leurs fichiers clés. Ce fichier ne contient **aucun mot de passe,
jeton ni clé privée**. Pour repartir de zéro, fermez
l'application et supprimez-le. L'historique des transferts de l'onglet Fichiers est à côté, dans
`transfers.json` (noms et chemins des fichiers, sommes SHA-256, jamais leur contenu).

## Sécurité

- **HTTPS obligatoire** vers le PVWA ; la validation des certificats n'est jamais désactivée.
- **Aucun secret sur disque** : mot de passe CyberArk, jeton de session, clé MFA et mot de passe PSMP restent
  en mémoire, le temps de la session. Déconnexion du PVWA (`Logoff`) à la fermeture.
- Session PVWA ouverte avec `concurrentSession` : votre session web PVWA éventuelle n'est pas fermée.
- **Copie d'un mot de passe** : la réponse du PVWA est lue dans un tampon effacé ensuite et décodée sans passer par
  une chaîne ; le mot de passe est copié directement dans le presse-papiers Windows, marqué pour être exclu de
  l'historique (`Win+V`), de la synchronisation entre appareils et des outils de surveillance du presse-papiers, puis
  effacé après 20 s s'il y est encore, ainsi qu'à la déconnexion, à la fermeture et au verrouillage de Windows. Il
  n'est jamais affiché ni écrit dans le journal de débogage.
- **Ajout d'un compte** : le mot de passe saisi est lu dans le champ masqué sans passer par une chaîne, envoyé une
  seule fois au PVWA en HTTPS, puis effacé de la mémoire ; il n'est ni enregistré ni écrit dans le journal de
  débogage.
- **Sessions PSM** : le fichier RDP du PVWA (jeton PSM à usage unique) est écrit dans `%TEMP%\CyberArkTerm` pour
  `mstsc`, qui en vérifie la signature, puis supprimé après 60 s ou à la fermeture.
- **Clés d'hôte PSMP épinglées** au premier usage, avec alerte en cas de changement (de même pour les serveurs
  joints en accès d'urgence).
- **Maintien de la session PVWA** : il évite l'expiration par inactivité ; rien n'est envoyé tant que Windows
  est verrouillé, et l'option se désactive dans les Paramètres si votre politique l'exige.
- **Coffres KeePass** :
  - mot de passe maître jamais enregistré, sauf dans le coffre local si vous le demandez : Argon2id (64 Mio,
    3 passes) puis AES-256-GCM, réglages de dérivation authentifiés, le tout protégé par DPAPI (compte Windows) ;
  - en mémoire, clé du coffre et mots de passe des entrées restent masqués et ne sont révélés qu'au moment de la
    connexion ; coffres verrouillés à la déconnexion, à la fermeture et au verrouillage de Windows ;
  - enregistrement sûr : relecture du fichier, modification appliquée à sa version du moment (les changements
    faits ailleurs sont gardés), vérification du résultat déchiffré, copie `.bak`, remplacement en une fois ;
    une entrée modifiée ailleurs entre-temps n'est pas écrasée ;
  - bureau à distance direct : le mot de passe est transmis au seul contrôle Bureau à distance (ni fichier, ni
    gestionnaire d'identification), authentification réseau (NLA) et alerte si le serveur n'est pas reconnu ;
  - journal `urgence.log` : date, compte Windows, poste, action, coffre, entrée, cible ; jamais de mot de passe.
- **Journal de débogage**, désactivé par défaut (menu du bouton Paramètres) : `%LOCALAPPDATA%\CyberArkTerm\debug.log`,
  5 Mo au plus plus une génération `.1`. Il note le déroulement des connexions PVWA, PSM, Bureau à distance et
  SSH : adresses et statuts des requêtes, réglages du fichier .rdp, événements et codes du contrôle Bureau à
  distance, erreurs. Il contient des noms de serveurs et de comptes, mais **jamais** de mot de passe, de jeton de
  session, de demande de session PSM (`PSM@…` masqué), de signature, d'en-tête ou de corps de requête, ni le
  contenu des sessions. La barre d'état le signale tant qu'il est actif. Relisez-le avant de le transmettre, et
  supprimez-le une fois le problème résolu.
- **Fichiers modifiés** : la copie locale ouverte dans l'éditeur est placée dans `%TEMP%\CyberArkTerm\edit`
  et supprimée à la fermeture de l'onglet SSH ; une alerte prévient si des modifications n'ont pas été
  renvoyées.
- **Pas d'injection de commande** : chemins SCP et dossiers de départ protégés entre apostrophes pour le
  shell distant ; arguments `ssh` / Windows Terminal validés et passés sans shell.
- Export CSV protégé contre l'injection de formules Excel.
- Les sessions PSM et PSMP ouvertes par CyberArkTerm sont des sessions CyberArk standard : elles sont
  enregistrées et auditées par le PSM comme celles ouvertes depuis le PVWA.

Pour signaler une vulnérabilité, voir [SECURITY.md](SECURITY.md) (signalement privé, pas d'issue publique).

## Fonctionnement technique

### API du PVWA utilisées

| Appel | Usage |
| --- | --- |
| `POST /PasswordVault/API/auth/{CyberArk\|LDAP\|RADIUS\|Windows}/Logon` | Ouverture de session |
| `GET /PasswordVault/API/Accounts?offset=…&limit=1000` | Liste paginée des comptes |
| `POST /PasswordVault/API/Accounts/{id}/PSMConnect` | Fichier RDP de la session PSM |
| `POST /PasswordVault/API/Accounts` | Création d'un compte dans un safe (« Ajouter un compte ») |
| `POST /PasswordVault/API/Accounts` (une fois par ligne) | Import de comptes depuis un CSV |
| `PATCH` / `DELETE /PasswordVault/API/Accounts/{id}` | Modification (seuls les champs changés) et suppression d'un compte |
| `POST /PasswordVault/API/Accounts/{id}/Verify`, `/Change`, `/Reconcile` | Opérations demandées au CPM |
| `POST /PasswordVault/API/Accounts/{id}/Password/Retrieve` | Copie du mot de passe (motif, ticket ; usage « copy » dans l'audit) |
| `POST` / `PUT` / `DELETE /PasswordVault/API/Safes/{safe}/Members[/{membre}]` | Ajout, droits et retrait d'un membre du safe |
| `GET /PasswordVault/API/Safes/{safe}/Members?offset=…&limit=1000` | Membres d'un safe et leurs droits (fenêtre « Membres du safe ») |
| `POST /PasswordVault/API/Users/Secret/SSHKeys/Cache` | Clé SSH temporaire « MFA caching » (si activée) |
| `GET /PasswordVault/API/Accounts?offset=0&limit=1` | Maintien de la session (toutes les 4 minutes) |
| `POST /PasswordVault/API/Auth/Logoff` | Fermeture de session |

### Coffres KeePass

Lecture et écriture natives (sans KeePass installé) des formats **KDBX 3.1 et 4.x** : chiffrement AES-256 ou
ChaCha20, dérivation de clé AES-KDF (instructions AES du processeur) ou Argon2d / Argon2id, fichiers clés XML
1.0 / 2.0, 32 octets, 64 caractères hexadécimaux ou fichier quelconque. Le fichier réécrit garde la version, le
chiffrement et la dérivation de clé d'origine, avec de nouvelles graines à chaque enregistrement. Les coffres de
test (`tests/CyberArkTerm.Core.Tests/KeePass/Vaults`) viennent de KeePassXC et pykeepass, et les fichiers écrits
par CyberArkTerm ont été vérifiés dans ces deux outils.

### Sessions Bureau à distance

Les sessions PSM s'ouvrent avec le fichier RDP renvoyé par `PSMConnect`, donné tel quel à la Connexion Bureau à
distance (`mstsc`) : elle en vérifie la signature et gère aussi bien le bureau que l'application distante
(RemoteApp). Le journal de débogage en note la structure (jeton, signature et arguments masqués). Les versions 0.4
à 0.6 ouvraient ces sessions dans un onglet : un PSM qui n'accepte que l'application distante n'y fonctionnait pas
bien (position et taille des fenêtres sur le serveur, souris), d'où le retour à `mstsc`.

Les onglets Bureau à distance (bureau à distance direct des coffres KeePass) hébergent le contrôle ActiveX de
Windows (`mstscax.dll`, classe `MsRdpClient` la plus récente disponible), réglé comme une connexion directe :
authentification réseau (NLA), alerte si le serveur n'est pas reconnu, redirections désactivées sauf le
presse-papiers. La résolution du bureau distant suit la taille de l'onglet. Les fermetures de session et les
erreurs de connexion sont expliquées dans l'onglet avec le message et les codes de Windows (raison, raison
étendue). Un test d'intégration (workflow `rdp-integration`) ouvre une vraie session sur le poste de CI.

**Un thread par connexion Bureau à distance.** Le contrôle, sa fenêtre et ses événements vivent sur un thread à
part (STA, avec sa boucle de messages) ; l'interface ne l'attend jamais. L'onglet contient une fenêtre du thread de
l'interface, dans laquelle ce thread place la fenêtre du contrôle. Avant de libérer le contrôle, il l'en retire :
une déconnexion ou une libération qui tarde ne fige plus l'application. Si le thread ne répond plus pendant 5 s,
la barre de l'onglet le signale, et le reste de l'application reste utilisable. Limite : Windows partage le clavier
et la souris entre une fenêtre et celles qu'elle contient, même d'un autre thread ; un contrôle bloqué pour de bon
peut encore retenir un clic dans sa zone ou un changement de focus.

### Sessions PSMP

Chaque onglet SSH ouvre jusqu'à trois connexions au PSMP, avec le même identifiant
`<vous>@<compte>[#domaine]@<cible>` : le terminal, la connexion SFTP de l'onglet Fichiers, et une connexion
SCP au premier dépôt de fichier en SCP. Chacune est une session PSMP, enregistrée par le PSM.
Les envois au serveur (frappe, taille du terminal) et la fermeture des connexions se font hors du thread de
l'interface, dans l'ordre : un serveur ou un PSMP qui ne lit plus ne fige pas l'application.

### Suivi du dossier du terminal

À l'ouverture d'une session SSH (si l'option est active), CyberArkTerm attend que le shell du serveur cible
affiche son invite (jusqu'à 60 s : le PSMP met parfois plusieurs secondes à joindre la cible), puis lui envoie
une commande d'une ligne, précédée d'une espace pour ne pas entrer dans l'historique. Rien n'est envoyé si
vous avez déjà commencé à taper ; la commande peut être renvoyée sans effet en double (case « Suivre ») :

- définition de `PROMPT_COMMAND` (bash) ou `precmd` (zsh) qui émet la séquence standard **OSC 7** avec le
  dossier courant à chaque invite ;
- si un dossier de départ est configuré, un `cd` vers ce dossier ;
- effacement de la commande tapée, pour qu'elle ne reste pas à l'écran.

Le terminal intégré décode la séquence OSC 7 et l'onglet Fichiers se place dans le dossier indiqué.

## Dépannage

| Symptôme | Cause probable et solution |
| --- | --- |
| « Connexion TLS refusée : le certificat du PVWA n'est pas approuvé » | Le certificat (ou l'autorité qui l'a émis) n'est pas dans le magasin Windows du poste. |
| « Le PVWA doit être joint en HTTPS » | Saisissez l'adresse sans `http://` (ou avec `https://`). |
| « Le PVWA n'a pas de composant de connexion « PSM-RDP » pour ce compte » (`EPVWA093E Failed to get the relevant connection component`) | La plateforme du compte utilise un composant d'un autre nom (par exemple `WIN-PSM`) : celui que propose le bouton « Connect » du PVWA, ou le nom après `/c` dans une commande `psm /u … /a … /c …`. Saisissez-le dans « Composant » ; « Mémoriser ce composant pour la plateforme » est coché pour les connexions suivantes. |
| « Votre session CyberArk a expiré » | Délai d'inactivité du PVWA dépassé : reconnectez-vous. |
| « Mot de passe » → « Copier » : « Le PVWA refuse : … « Récupérer les comptes » … » | Droit manquant sur le safe, ou motif / ticket exigé par la plateforme : saisissez-le. Avec une double validation, faites la demande dans le PVWA. |
| « Vérifier / Changer / Réconcilier » : « Le PVWA refuse : … « Lancer les opérations CPM » … » | Demandez ce droit sur le safe ; « Membres du safe » montre vos droits. |
| « Ajouter un compte » : « Le PVWA refuse : votre compte doit avoir le droit « Ajouter des comptes »… » | Demandez ce droit sur le safe (et « Modifier le contenu des comptes » pour fournir le mot de passe), ou créez le compte sans mot de passe. « Membres du safe » montre vos droits. |
| « Membres du safe » : « Votre compte ne peut pas voir les membres de ce safe » | Le PVWA exige le droit « View Safe Members » sur le safe : demandez-le à un gestionnaire du safe. |
| « Connection component … is not configured for platform … » | Choisissez le bon composant dans « Connexion avancée », cochez « Mémoriser » pour la plateforme. |
| « You must specify a reason… » | Saisissez un motif dans la fenêtre qui s'ouvre (ou un motif par défaut dans les propriétés du serveur courant). |
| Le compte n'apparaît pas | Vous n'avez pas le droit « List accounts » sur son safe, ou la liste doit être rechargée (`F5`). |
| Le mot de passe PSMP est demandé à chaque onglet | MFA caching non activé sur le PVWA : comportement normal (une fois par onglet). |
| L'onglet Fichiers indique « Connexion SFTP impossible » | SFTP n'est pas autorisé sur le PSMP ou pour ce compte : voir l'équipe CyberArk. |
| Le navigateur ne suit pas les `cd` | Le shell distant n'est pas bash ou zsh, l'option est désactivée dans les Paramètres, ou l'invite n'a pas été reconnue : recochez « Suivre le dossier du terminal » à l'invite du shell. |
| Alerte « la clé du PSMP a changé » | Ne continuez que si l'équipe CyberArk confirme un changement du serveur. |
| « Mot de passe maître ou fichier clé incorrect » | Vérifiez le mot de passe et le fichier clé ; un coffre protégé par YubiKey n'est pas pris en charge. |
| Le coffre KeePass demande le mot de passe malgré « Mémoriser » | Coffre local verrouillé (« Plus tard » au déverrouillage) ou mot de passe maître changé ailleurs : saisissez-le, il est remémorisé. |
| « Le fichier du coffre local est endommagé ou a été créé par un autre compte Windows » | Le coffre local ne suit pas un changement de poste ou de compte : supprimez-le dans les Paramètres et recréez-le. |
| « L'entrée … a été modifiée ou supprimée dans le coffre entre-temps » | Quelqu'un a changé la même entrée ailleurs : le coffre est rechargé, refaites la modification. |
| Un compte Unix s'ouvre en PSM et pas en SSH | Adresse du PSMP non renseignée dans les Paramètres, ou compte non reconnu comme Unix : clic droit → « Se connecter en SSH ». |
| Comprendre un échec de connexion | Paramètres → Journal de débogage, reproduisez le problème, puis Paramètres → « Afficher le fichier du journal ». |
| Un onglet de bureau à distance direct (KeePass) affiche « Erreur du contrôle Bureau à distance » | Signalez le code affiché (si le contrôle Bureau à distance est absent du poste, la connexion passe par `mstsc`). |

## Développement

### Structure

| Projet | Rôle |
| --- | --- |
| `src/CyberArkTerm.Core` | Logique sans interface, multiplateforme : client de l'API PVWA, classement des comptes, émulateur de terminal xterm, connexions PSMP et navigateur SFTP/SCP (SSH.NET), serveurs « Courants » en dossiers, coffres KeePass (KDBX), coffre local, préférences. |
| `src/CyberArkTerm.App` | Application WPF : fenêtres, onglets, contrôle terminal, contrôle Bureau à distance (onglets RDP), lancement de `mstsc`, icône (`Assets`). |
| `tests/CyberArkTerm.Core.Tests` | Tests xUnit de Core (faux PVWA HTTP, terminal, PSMP, dossiers…). |
| `tests/CyberArkTerm.App.Tests` | Tests Windows de l'application (vrai contrôle Bureau à distance, DPAPI). |

Dépendance externe : [SSH.NET](https://github.com/sshnet/SSH.NET) (licence MIT).

### Traductions

Les textes de l'interface sont dans `src/CyberArkTerm.Core/Localization/CoreStrings*.resx` et
`src/CyberArkTerm.App/Localization/Strings*.resx` : anglais dans le fichier neutre, puis `.fr` et `.it`.
Les classes `*.Designer.cs` sont générées par Visual Studio (`PublicResXFileCodeGenerator`) ; un test vérifie
que chaque langue a toutes les clés, les mêmes paramètres `{0}` et les mêmes touches d'accès `_`.
Pour ajouter une langue : copier les `.resx` avec le nouveau code (`.de.resx`…), traduire, puis ajouter le
code à `UiLanguage.Supported`.

### Compiler et tester

Avec le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) :

```powershell
dotnet test CyberArkTerm.sln
dotnet run --project src/CyberArkTerm.App
```

Le projet compile aussi sous Linux ou macOS (`EnableWindowsTargeting`) ; l'application ne s'exécute que sous
Windows. Les tests de `tests/CyberArkTerm.App.Tests` (dont un test du vrai contrôle Bureau à distance) ne
s'exécutent que sous Windows ; ailleurs, lancez `dotnet test tests/CyberArkTerm.Core.Tests`.

### Publier l'exécutable

```powershell
# Autonome (~65 Mo) : aucun runtime à installer sur le poste
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

# Léger : nécessite le « .NET Desktop Runtime 10 » sur le poste
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
```

La CI ([`.github/workflows/build.yml`](.github/workflows/build.yml)) exécute les tests et publie l'exécutable
autonome en artefact `CyberArkTerm-win-x64` pour chaque pull request et chaque push sur `main`.

### Publier une version

Depuis GitHub : **Actions → release → Run workflow** sur `main`, en indiquant le numéro `X.Y.Z` ; ou bien
poussez un tag `vX.Y.Z` sur `main`. Le workflow [`release.yml`](.github/workflows/release.yml) exécute les
tests, compile l'exécutable avec ce numéro de version, crée le tag s'il n'existe pas et publie la *Release*
GitHub avec le zip et `SHA256SUMS.txt`. Les notes de version sont lues dans `docs/releases/vX.Y.Z.md` si ce
fichier existe.

## Limites et pistes

**Limites actuelles**

- **Privilege Cloud** (connexion via CyberArk Identity) et **SAML** ne sont pas gérés.
- L'API Accounts n'indique pas quels composants PSM une plateforme propose : le composant est déduit, puis
  mémorisable.
- Coffres KeePass : chiffrement Twofish et clés YubiKey non pris en charge ; pas de création de coffre (créez-le
  avec KeePass ou KeePassXC) ; pièces jointes gardées mais non affichées.
- Le suivi du dossier du terminal nécessite bash ou zsh sur le serveur.
- PSM Gateway (HTML5), double validation (dual control) et accès exclusif ne sont pas gérés.

**Pistes**

- Exécutable signé et installateur MSI.
- Plusieurs PVWA (profils de connexion), Privilege Cloud.

## Licence

[MIT](LICENSE) © 2026 muller-camille
