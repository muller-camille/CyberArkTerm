<img src="docs/icone.png" alt="" width="72" align="right">

# CyberArkTerm

**Français** · [English](README.md) · [Italiano](README.it.md)

[![build](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml/badge.svg)](https://github.com/muller-camille/CyberArkTerm/actions/workflows/build.yml)
[![release](https://github.com/muller-camille/CyberArkTerm/actions/workflows/release.yml/badge.svg)](https://github.com/muller-camille/CyberArkTerm/releases/latest)

**Client Windows multi-sessions pour CyberArk.** CyberArkTerm se connecte à votre PVWA, liste les comptes auxquels vous
avez accès et ouvre vos sessions en un double-clic : bureau à distance via **PSM**, ou terminal SSH via **PSM for SSH
(PSMP)** avec un **navigateur de fichiers** intégré pour déposer des fichiers sur le serveur.

![Session SSH via le PSMP, avec l'onglet Fichiers qui suit le dossier du terminal](docs/captures/fr/main-window.png)

**[Télécharger](https://github.com/muller-camille/CyberArkTerm/releases/latest)** ·
**[Guide d'utilisation](docs/guide.fr.md)** · [Notes de version](https://github.com/muller-camille/CyberArkTerm/releases) ·
[Politique de sécurité](SECURITY.md)

> Les captures proviennent d'un environnement de démonstration (données fictives).

## Sommaire

- [Fonctionnalités](#fonctionnalités)
- [Installation](#installation)
- [Prise en main](#prise-en-main)
- [Sécurité](#sécurité)
- [Développement](#développement)
- [Limites et pistes](#limites-et-pistes)
- [Licence](#licence)

## Fonctionnalités

| | |
| --- | --- |
| **Connexion CyberArk** | Authentification CyberArk, LDAP, RADIUS (challenge / OTP compris) ou Windows (session courante). |
| **Disponibles** | Tous les comptes visibles dans le coffre, groupés par safe, plateforme ou type de cible, avec recherche instantanée ; mot de passe (CPM, copie), membres d'un safe, ajout, modification et import de comptes. |
| **Courants** | Vos serveurs de travail, rangés en dossiers et sous-dossiers, chacun avec sa propre configuration. |
| **Sessions PSM** | Bureau à distance via le PSM (comme le bouton « Connect » du PVWA), dans la Connexion Bureau à distance de Windows : composant, machine cible, motif, ticket. |
| **Sessions SSH (PSMP)** | Terminal intégré en onglet (compatible xterm : couleurs, vim, less, top…), MFA, menu du clic droit, recherche, fenêtres séparées. |
| **Onglet Fichiers** | Navigateur SFTP du serveur : dépôt (SFTP, ou SCP, l'autre prenant le relais si le serveur refuse) et téléchargement par glisser-déposer, vérification SHA-256 de chaque fichier, file d'attente et historique des transferts, tri par colonne, modification dans votre éditeur de texte, droits, suivi en direct (`tail -f`), comparaison, envoi vers plusieurs serveurs. |
| **Vue parallèle** | Jusqu'à 8 sessions SSH côte à côte (un dossier « Courants » s'ouvre d'un clic), saisie simultanée en option. |
| **Accès d'urgence (KeePass)** | Sans CyberArk : coffres KeePass (.kdbx) dans « Courants », connexions SSH et bureau à distance directes, journal local. |
| **Langues** | Français, anglais et italien : langue de Windows par défaut, modifiable à tout moment. |

<table>
<tr>
<td width="50%"><img src="docs/captures/fr/available.png" alt="Onglet Disponibles"><br><sub>« Disponibles » : tous les comptes du coffre, recherche instantanée</sub></td>
<td width="50%"><img src="docs/captures/fr/my-servers.png" alt="Onglet Courants"><br><sub>« Courants » : vos serveurs en dossiers, coffres KeePass en tête</sub></td>
</tr>
<tr>
<td><img src="docs/captures/fr/terminal-menu.png" alt="Menu du clic droit dans le terminal"><br><sub>Clic droit dans le terminal : copier, coller, rechercher, actions de l'onglet</sub></td>
<td><img src="docs/captures/fr/keepass-vault.png" alt="Accès d'urgence avec KeePass"><br><sub>Accès d'urgence : SSH direct depuis un coffre KeePass</sub></td>
</tr>
</table>

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

### Mettre à jour

Bouton Paramètres → **« À propos de CyberArkTerm… »** : version, liens du projet, dossier des paramètres, et
« Rechercher maintenant ». Si une version plus récente existe, « Télécharger et vérifier » enregistre l'archive dans
le dossier Téléchargements puis la compare à `SHA256SUMS.txt` de la même version (gardée seulement si elle est
identique). Rien n'est installé automatiquement : fermez CyberArkTerm et remplacez l'exécutable ; vos paramètres sont
conservés. L'option « Rechercher une nouvelle version au démarrage » (désactivée par défaut) fait cette recherche au
plus une fois par jour et affiche un lien dans la barre d'état.

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
- Pour le SSH : un **PSM for SSH (PSMP)**, avec SFTP autorisé pour l'onglet Fichiers (et SCP si vous
  choisissez le dépôt en SCP).
- Facultatif : **MFA caching** activé sur le PVWA, pour éviter de ressaisir mot de passe et MFA au PSMP.

## Prise en main

1. **Se connecter** : adresse du PVWA (`pvwa.mondomaine.local` suffit), méthode d'authentification, utilisateur et mot
   de passe. L'adresse, la méthode et l'utilisateur sont mémorisés ; le mot de passe jamais.
2. **Trouver un compte** dans l'onglet « Disponibles » : la recherche porte sur tous les champs (`prd sql`). Clic droit
   sur un compte pour son mot de passe (vérifier, changer, réconcilier, copier), les membres de son safe, ou pour
   ajouter, modifier et importer des comptes.
3. **Se connecter** d'un double-clic : un compte Windows ouvre une session PSM dans la Connexion Bureau à distance ; un
   compte Unix ouvre un terminal SSH en onglet, via le PSMP réglé dans les **Paramètres**. Clic droit dans le terminal
   pour copier, coller, rechercher et les actions de l'onglet.
4. **Onglet Fichiers** (à côté d'une session SSH) : parcourez le serveur, glissez des fichiers depuis l'Explorateur
   pour les déposer, vers l'Explorateur pour les télécharger. Chaque fichier est vérifié (SHA-256) ; le bouton
   **Historique** de la barre d'outils garde chaque transfert et ses sommes de contrôle. Un clic sur l'en-tête d'une
   colonne trie la liste.
5. **Courants** : rangez vos serveurs de travail en dossiers, chacun avec sa configuration (PSM ou SSH, composant,
   machine cible, dossier de départ) ; ouvrez tout un dossier dans la **vue parallèle**.
6. **Accès d'urgence** : quand CyberArk est indisponible, « Accès d'urgence (KeePass) » dans la fenêtre de connexion
   ouvre vos coffres KeePass et se connecte directement en SSH ou en bureau à distance (sans enregistrement par le PSM,
   noté dans un journal sur ce poste).

Le **[guide d'utilisation](docs/guide.fr.md)** décrit chaque onglet en détail, les
[raccourcis](docs/guide.fr.md#raccourcis), les
[paramètres et le fichier de configuration](docs/guide.fr.md#paramètres-et-fichier-de-configuration), le
[fonctionnement technique](docs/guide.fr.md#fonctionnement-technique) et le [dépannage](docs/guide.fr.md#dépannage).

## Sécurité

- **HTTPS obligatoire** vers le PVWA ; la validation du certificat n'est jamais désactivée.
- **Aucun secret sur disque** : mot de passe CyberArk, jeton de session, clé MFA et mot de passe PSMP restent en mémoire ;
  la session PVWA est fermée à la sortie. Le fichier de configuration ne contient aucun mot de passe, jeton ni clé
  privée.
- **Sessions CyberArk standard** : les sessions PSM et PSMP ouvertes par CyberArkTerm sont enregistrées et auditées par
  le PSM comme celles ouvertes depuis le PVWA.
- **Mots de passe copiés** : directement dans le presse-papiers Windows, exclus de son historique et de sa
  synchronisation, effacés après 20 s ; jamais affichés ni journalisés.
- **Clés d'hôte du PSMP** mémorisées à la première connexion, avec une alerte si elles changent.
- **Coffres KeePass** : le mot de passe maître n'est jamais enregistré, sauf dans le coffre local si vous le demandez
  (Argon2id, AES-256-GCM, protégé par votre compte Windows) ; chaque ouverture et connexion est notée dans un journal
  local.
- **Aucune requête vers Internet** sans votre action ou l'option de mise à jour (désactivée par défaut) ; une mise à jour
  téléchargée n'est gardée que si sa somme SHA-256 correspond à `SHA256SUMS.txt`, et rien n'est installé
  automatiquement.
- **Journal de débogage** désactivé par défaut ; il ne contient jamais de mot de passe, de jeton ni le contenu des
  sessions.

Tous les détails : [guide d'utilisation → Sécurité](docs/guide.fr.md#sécurité). Pour signaler une vulnérabilité, voir
[SECURITY.md](SECURITY.md) (signalement privé, pas de ticket public).

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
- Le suivi du dossier du terminal nécessite bash, zsh ou tcsh sur le serveur.
- PSM Gateway (HTML5), double validation (dual control) et accès exclusif ne sont pas gérés.

**Pistes**

- Exécutable signé et installateur MSI.
- Plusieurs PVWA (profils de connexion), Privilege Cloud.
- Autres idées, gardées pour plus tard : voir [IDEAS.md](IDEAS.md).

## Licence

[MIT](LICENSE) © 2026 muller-camille
