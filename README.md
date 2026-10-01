# CyberArkTerm

Application Windows (WPF / .NET 10) qui liste les comptes CyberArk accessibles à l'utilisateur
pour ouvrir des sessions PSM.

Au lancement, une fenêtre de connexion demande l'adresse du PVWA et les identifiants ; l'application
charge ensuite **tous les comptes visibles par l'utilisateur** (serveur, compte, domaine, plateforme,
safe, machines autorisées) via l'API REST du PVWA.

## Fonctionnalités

- Connexion **CyberArk**, **LDAP**, **RADIUS** (y compris challenge / OTP) ou **Windows** (session courante).
- Chargement paginé de tous les comptes (`GET /PasswordVault/API/Accounts`), avec barre de progression.
- Recherche instantanée multi-mots sur toutes les colonnes (`Ctrl+F`), tri par colonne.
- Copie du serveur, de l'utilisateur ou de `domaine\utilisateur` (clic droit), copie des lignes (`Ctrl+C`).
- Export CSV de la liste filtrée (séparateur `;`, compatible Excel français).
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
3. La liste des comptes se charge ; filtrer avec la zone de recherche.

L'adresse, la méthode et le nom d'utilisateur sont mémorisés dans `%APPDATA%\CyberArkTerm\settings.json`.
**Le mot de passe n'est jamais enregistré.**

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
| `src/CyberArkTerm.Core` | Client de l'API PVWA, modèle des comptes, filtre, export CSV, préférences (multiplateforme, testé) |
| `src/CyberArkTerm.App` | Interface WPF : fenêtre de connexion et liste des comptes |
| `tests/CyberArkTerm.Core.Tests` | Tests xUnit du client (faux PVWA HTTP), du filtre et de l'export |

## Sécurité

- HTTPS obligatoire ; aucune désactivation de la validation des certificats.
- Session ouverte avec `concurrentSession: true` pour ne pas fermer une éventuelle session PVWA web en cours.
- Déconnexion (`API/Auth/Logoff`) à la fermeture de la fenêtre ou sur « Déconnexion ».
- Le jeton de session reste en mémoire uniquement.
- L'export CSV neutralise les valeurs interprétables comme formules par Excel.

## Limites actuelles

- **Privilege Cloud** (authentification via CyberArk Identity / OAuth) et **SAML** ne sont pas gérés.
- La liste contient tous les comptes visibles : l'API Accounts n'indique pas si la plateforme du compte
  autorise une connexion PSM.
- Pas encore de lancement de session PSM depuis l'application (`POST API/Accounts/{id}/PSMConnect`).
