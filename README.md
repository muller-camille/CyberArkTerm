# CyberArkTerm

Application Windows (WPF / .NET 10), dans l'esprit de MobaXterm, pour ouvrir des sessions sur les
comptes CyberArk via PSM et PSM for SSH, avec terminal et navigateur de fichiers intégrés.

Au lancement, une fenêtre de connexion demande l'adresse du PVWA et les identifiants ; l'application
charge ensuite **tous les comptes visibles par l'utilisateur** via l'API REST du PVWA et les présente
comme des sessions : un double-clic ouvre la connexion.

## Fonctionnalités

**Interface « à la MobaXterm »**

- Barre d'outils à grosses icônes : Se connecter, SSH, Connexion avancée, Favori, Actualiser, Exporter,
  Paramètres, Déconnexion, Quitter.
- Bandeau latéral avec trois onglets verticaux :
  - **Disponibles** : tous les comptes CyberArk de l'utilisateur, groupés par safe, plateforme ou type de
    cible, avec filtre ;
  - **Courants** : les serveurs que l'utilisateur utilise, rangés dans des dossiers et sous-dossiers
    (créer, renommer, supprimer, glisser-déposer), chacun avec sa configuration propre : nom affiché, mode
    PSM ou SSH, composant PSM, machine cible, motif par défaut, dossier de départ SFTP. Ajout par clic droit
    sur un compte « Disponible » ou par glisser-déposer sur l'onglet ;
  - **Fichiers** : navigateur SFTP du serveur de la session SSH active (voir plus bas).
- Onglet **Accueil** : connexion rapide (tapez un serveur, Entrée), sessions récentes, raccourcis.
- Onglet **Tous les comptes** : tableau triable, copie (`Ctrl+C`), export CSV.
- Icônes par type de cible (Windows, Unix, base de données, réseau, autre).

**Connexion aux machines**

- **PSM (bureau à distance)** : l'application demande au PVWA une connexion
  (`POST API/Accounts/{id}/PSMConnect`, comme le bouton « Connect » du PVWA) et ouvre le fichier RDP
  reçu dans `mstsc`. Le fichier, qui contient un jeton à usage unique, est supprimé après 60 s.
- Composant PSM déduit de la plateforme (`PSM-RDP` pour Windows, `PSM-SSH` pour Unix/réseau,
  `PSM-SQLServerMgmtStudio`, `PSM-SQLPlus`...) et mémorisable par plateforme.
- **Comptes de domaine** : choix de la machine cible (`PSMRemoteMachine`), pré-rempli avec les machines
  autorisées du compte.
- **Motif / ticket** : si le PVWA refuse la demande (motif exigé, composant non configuré...), la fenêtre
  « Connexion avancée » s'ouvre avec le message du PVWA pour corriger et réessayer.
- **SSH via PSM for SSH (PSMP)**, optionnel : identifiant `<vous>@<compte>[#domaine]@<cible>` sur le PSMP.
  Par défaut la session s'ouvre **dans un onglet de CyberArkTerm** (terminal compatible xterm : couleurs,
  vim, less, top, copier à la sélection, collage au clic droit) ; sinon dans Windows Terminal.
  - Authentification : clé SSH temporaire « MFA caching » du PVWA si elle est activée, sinon les questions
    du PSMP (mot de passe, code MFA) dans une fenêtre ; le mot de passe est réutilisé pour les connexions
    SFTP/SCP de la même session, jamais enregistré.
  - Clé d'hôte du PSMP vérifiée (empreinte SHA256 à accepter au premier usage, alerte si elle change).

**Onglet « Fichiers » (sessions SSH)**

- Liste du dossier (SFTP), navigation (double-clic, `..`, dossier personnel, barre de chemin éditable).
- **Dépôt de fichiers par glisser-déposer** depuis l'Explorateur (ou bouton « Envoyer »), en **SCP** par
  défaut (SFTP en option), dossiers compris ; confirmation avant d'écraser.
- Suppression (`rm`, dossiers vides), création de dossier, téléchargement, copie du chemin.
- **« Suivre le dossier du terminal »** : à l'ouverture, CyberArkTerm installe discrètement dans le shell
  (bash/zsh) un `PROMPT_COMMAND` qui signale le dossier courant (séquence OSC 7) ; chaque `cd` dans le
  terminal déplace le navigateur. Le dossier de départ d'un serveur « Courant » s'applique au shell et au
  navigateur.

**Session CyberArk**

- Connexion **CyberArk**, **LDAP**, **RADIUS** (y compris challenge / OTP) ou **Windows** (session courante).
- Chargement paginé de tous les comptes (`GET /PasswordVault/API/Accounts`), avec barre de progression.
- Actualisation (`F5`), déconnexion, détection de l'expiration de session.

## Prérequis

- Windows 10 / 11 (x64).
- Un PVWA CyberArk **v10 ou supérieur** (API REST `/PasswordVault/API/...`), joignable en **HTTPS**.
- L'utilisateur doit avoir le droit **List accounts** sur les safes concernés : l'API ne renvoie que ce qu'il
  a le droit de voir.
- Le certificat du PVWA doit être approuvé par le poste (magasin de certificats Windows).

## Utilisation

1. Lancer `CyberArkTerm.exe`.
2. Saisir l'adresse du PVWA (`pvwa.mondomaine.local` suffit : `https://` et `/PasswordVault` sont ajoutés),
   choisir la méthode d'authentification, puis le compte et le mot de passe.
3. Les comptes se chargent dans l'onglet « Disponibles ». Double-clic (ou Entrée) sur un compte pour s'y
   connecter, clic droit pour choisir PSM / SSH / connexion avancée ou l'ajouter aux serveurs courants.
4. Pour le SSH direct, renseigner l'adresse du PSMP dans **Paramètres**.

L'adresse du PVWA, la méthode, le nom d'utilisateur, le PSMP, les serveurs « Courants » et leurs dossiers,
les sessions récentes, les composants mémorisés et les empreintes des PSMP sont enregistrés dans
`%APPDATA%\CyberArkTerm\settings.json`.
**Le mot de passe n'est jamais enregistré.**

Prérequis côté poste : le client Bureau à distance (`mstsc`, présent sur Windows) ; pour le SSH, le
« Client OpenSSH » de Windows (et, de préférence, Windows Terminal).

## Compiler

Avec le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) :

```powershell
dotnet test CyberArkTerm.sln
dotnet run --project src/CyberArkTerm.App
```

Produire un exécutable unique :

```powershell
# Autonome (~65 Mo) : aucun runtime à installer sur le poste
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

# Léger (~250 Ko) : nécessite le « .NET Desktop Runtime 10 » sur le poste
dotnet publish src/CyberArkTerm.App -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
```

La CI GitHub Actions (`.github/workflows/build.yml`) exécute les tests et publie l'exe autonome en artefact
`CyberArkTerm-win-x64` à chaque push sur `main`.

## Structure

| Projet | Rôle |
| --- | --- |
| `src/CyberArkTerm.Core` | Client de l'API PVWA (logon, comptes, PSMConnect, clé MFA), émulateur de terminal xterm, connexions PSMP (SSH.NET), navigateur SFTP/SCP, sessions « Courantes » en dossiers, préférences (multiplateforme, testé) |
| `src/CyberArkTerm.App` | Interface WPF : connexion, fenêtre principale façon MobaXterm, onglets Disponibles / Courants / Fichiers, terminal intégré, dialogues, lancement mstsc |
| `tests/CyberArkTerm.Core.Tests` | Tests xUnit du client (faux PVWA HTTP), du filtre et de l'export |

## Sécurité

- HTTPS obligatoire ; aucune désactivation de la validation des certificats.
- Session ouverte avec `concurrentSession: true` pour ne pas fermer une éventuelle session PVWA web en cours.
- Déconnexion (`API/Auth/Logoff`) à la fermeture de la fenêtre ou sur « Déconnexion ».
- Le jeton de session reste en mémoire uniquement.
- Les fichiers RDP (jeton PSM à usage unique) sont écrits dans `%TEMP%\CyberArkTerm` et supprimés après 60 s
  ou à la fermeture.
- Les arguments passés à `ssh` / Windows Terminal sont contrôlés (pas d'espace ni de métacaractère) et
  transmis sans passer par un shell.
- SCP : chemins distants protégés entre apostrophes (pas d'injection de commande via un nom de fichier).
- Empreintes des PSMP acceptées stockées dans les préférences (« Paramètres » → « Oublier les clés »).
- La clé MFA du PVWA et le mot de passe PSMP restent en mémoire, le temps de la session.
- L'export CSV neutralise les valeurs interprétables comme formules par Excel.

## Limites actuelles

- **Privilege Cloud** (authentification via CyberArk Identity / OAuth) et **SAML** ne sont pas gérés.
- La liste contient tous les comptes visibles : l'API Accounts n'indique pas si la plateforme du compte
  autorise une connexion PSM ni quels composants elle propose (d'où le composant déduit / mémorisable).
- Les sessions PSM (RDP) s'ouvrent dans `mstsc` ; seules les sessions SSH (PSMP) sont intégrées en onglet.
- Le suivi du dossier du terminal nécessite bash ou zsh côté serveur ; le PSMP doit autoriser SFTP pour le
  navigateur (et SCP pour le dépôt en SCP).
- Chaque onglet SSH ouvre jusqu'à trois sessions PSMP (terminal, SFTP, SCP), toutes enregistrées par PSM.
- PSM Gateway (HTML5), double validation (dual control) et accès exclusif ne sont pas gérés.
