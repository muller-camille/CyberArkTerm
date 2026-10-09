#!/usr/bin/env bash
# SSH.NET patché pour ZillaTerm : la version 2026.0.0 publiée, plus le commit qui garde la ligne « longname » des
# listes SFTP version 3 (noms du propriétaire et du groupe), en attendant une version de SSH.NET qui l'intègre.
#
# Compile le paquet depuis le fork muller-camille/SSH.NET, au commit figé ci-dessous (vérifié après le clone), et le
# dépose dans local-packages/, où nuget.config prend SSH.NET et lui seul. À lancer une fois avant de compiler
# ZillaTerm (Git Bash sous Windows) ; la CI, la publication et l'image de l'OSS Scanner le lancent aussi.
set -euo pipefail

repository=https://github.com/muller-camille/SSH.NET
commit=dce42a9d5750587df25c883cd3c37990d46ea64e
version=2026.0.1-zillaterm.1.gdce42a9d57

root=$(cd "$(dirname "$0")/.." && pwd)
package="$root/local-packages/SSH.NET.$version.nupkg"
if [ -f "$package" ]; then
  echo "SSH.NET $version : déjà dans local-packages."
  exit 0
fi

# Hors du dossier de ZillaTerm : SSH.NET a ses propres nuget.config, global.json et Directory.Build.props.
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
# Historique complet : Nerdbank.GitVersioning en tire le numéro de version du paquet.
git clone --quiet "$repository" "$work/sshnet"
git -C "$work/sshnet" checkout --quiet --detach "$commit"
test "$(git -C "$work/sshnet" rev-parse HEAD)" = "$commit"
# Sans les variables de GitHub Actions, le numéro de version ne dépend pas de la branche de ZillaTerm compilée.
env -u GITHUB_ACTIONS -u GITHUB_REF dotnet pack "$work/sshnet/src/Renci.SshNet" -c Release -o "$root/local-packages"
test -f "$package"
echo "SSH.NET $version : compilé depuis $repository au commit $commit."
