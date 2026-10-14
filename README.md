# GreenshotUpdater

Petit utilitaire Windows qui tient [Greenshot](https://getgreenshot.org) à jour : à
l'ouverture de session, il compare la version installée à la dernière version stable
publiée sur GitHub et, si une version plus récente existe, propose de l'installer par
winget. Il remplace la vérification intégrée de Greenshot, qui peut être désactivée.

Livrable : un seul fichier, `GreenshotUpdater.exe` (environ 410 Ko), sans droits
administrateur pour l'installer.

## Installation

1. Lancer `GreenshotUpdater.exe` (depuis Téléchargements, une clé USB…).
2. Si le runtime .NET Desktop 10 (x64) manque, le lanceur propose de le télécharger
   depuis Microsoft (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`)
   et de l'installer ; l'installeur demande lui-même l'élévation (UAC).
3. La fenêtre de réglages s'ouvre : choisir les options puis cliquer sur "Installer".

L'installation copie le lanceur (et l'application qu'il a extraite) dans
`%LOCALAPPDATA%\Programs\GreenshotUpdater`, puis applique les options. Lancé depuis ce
dossier, le bouton devient "Appliquer". Relancer un `GreenshotUpdater.exe` plus récent
puis "Installer" met à jour la copie installée.

## Options

| Option | Par défaut | Effet |
|---|---|---|
| Démarrer avec Windows | cochée | Valeur `GreenshotUpdater` dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` : `"<dossier>\GreenshotUpdater.exe" /demarrage` |
| Désactiver la vérification intégrée de Greenshot | cochée | `UpdateCheckInterval=0` dans la section `[Core]` de `%APPDATA%\Greenshot\Greenshot.ini` (décochée : `14`, valeur d'origine) |
| Raccourci sur le Bureau | décochée | `GreenshotUpdater.lnk` sur le Bureau de l'utilisateur |
| Raccourci dans le menu Démarrer | cochée | `GreenshotUpdater.lnk` dans le dossier Programmes du menu Démarrer de l'utilisateur |

Les raccourcis pointent sur le lanceur installé, sans argument (ils ouvrent la fenêtre
de réglages), avec l'icône de l'application. Les options sont mémorisées dans
`%APPDATA%\GreenshotUpdater\settings.json`.

La fenêtre affiche aussi la version installée de Greenshot et la dernière version
stable ; "Vérifier maintenant" propose la mise à jour si elle existe.

### Greenshot.ini pendant que Greenshot tourne

Greenshot réécrit tout son fichier `.ini` à sa fermeture : une modification faite
pendant qu'il tourne serait écrasée. Quand la valeur doit changer (et seulement dans ce
cas), GreenshotUpdater ferme donc Greenshot (fermeture propre, puis arrêt forcé après
5 s), modifie la ligne, puis relance Greenshot s'il tournait. Seule la ligne
`UpdateCheckInterval` de `[Core]` est touchée : le reste du fichier est réécrit octet
pour octet (encodage, BOM et fins de ligne conservés).

## Fonctionnement au démarrage (`/demarrage`)

Aucune fenêtre au départ :

1. Lecture de la version installée de Greenshot dans les programmes installés
   (`...\CurrentVersion\Uninstall`, `DisplayName` "Greenshot*", `DisplayVersion`, HKLM
   64 et 32 bits et HKCU).
2. Interrogation de `https://api.github.com/repos/greenshot/greenshot/releases/latest`
   (dernière version stable, préversions "UNSTABLE" ignorées). Si le réseau ne répond
   pas, nouvelles tentatives après 5, 10, 15 et 30 s (environ une minute en tout).
3. Greenshot absent, déjà à jour ou réseau muet : GreenshotUpdater se ferme sans rien
   afficher.
4. Sinon, une petite fenêtre propose "Greenshot x est disponible (version installée :
   y). Installer maintenant ?" :
   - "Installer" : Greenshot est fermé, puis
     `winget upgrade --id Greenshot.Greenshot --exact --interactive --accept-source-agreements --accept-package-agreements`
     est lancé (UAC puis assistant de Greenshot), Greenshot est relancé et le résultat
     affiché (succès, ou échec avec le code de winget). Si winget est introuvable,
     GreenshotUpdater propose de télécharger l'installeur
     `Greenshot-INSTALLER-*-RELEASE.exe` de la release GitHub et de le lancer.
   - "Plus tard" : rien n'est mémorisé, la question revient au prochain démarrage.

Une seule instance tourne à la fois. Le déroulement est noté dans
`%LOCALAPPDATA%\GreenshotUpdater\journal.log` (au-delà de 256 Ko, l'ancien journal
devient `journal.old.log`).

## Désinstallation

Bouton "Désinstaller" de la fenêtre de réglages (menu Démarrer > GreenshotUpdater),
après confirmation : retrait de l'entrée Run, `UpdateCheckInterval=14` rétabli,
raccourcis et `settings.json` supprimés. Le dossier d'installation, qui contient
l'exécutable en cours, est supprimé quelques secondes après la fermeture par une
commande `cmd` cachée. Le journal (`%LOCALAPPDATA%\GreenshotUpdater`) est conservé.

## Lanceur et application

`GreenshotUpdater.exe` est un lanceur .NET Framework 4.8 (présent sur tout Windows 10
et 11) qui embarque l'application WPF `GreenshotUpdater.App.exe` (.NET 10, exe unique
sans runtime embarqué). À chaque démarrage, il :

1. vérifie le runtime `Microsoft.WindowsDesktop.App` 10.x x64 (`DOTNET_ROOT`, registre
   `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\InstallLocation` vue 32 bits,
   `%ProgramFiles%\dotnet`) et propose de l'installer s'il manque ;
2. extrait `GreenshotUpdater.App.exe` à côté de lui s'il manque ou diffère (taille puis
   empreinte SHA-256) ;
3. démarre l'application avec `/lanceur "<chemin du lanceur>"` suivi de sa propre ligne
   de commande, puis se termine.

Le chemin du lanceur est transmis par l'argument `/lanceur` plutôt que par une variable
d'environnement : il reste visible dans le journal et n'est pas hérité par les
programmes lancés ensuite (winget, Greenshot). L'application s'en sert pour savoir quel
fichier copier à l'installation.

L'interface suit le thème clair ou sombre de Windows (thème Fluent), y compris quand il
change pendant l'exécution. La version (`<Version>` de `Directory.Build.props`) est
affichée dans le titre des fenêtres.

## Construction

Prérequis : SDK .NET 10.

```
build.cmd
```

Génère l'icône si elle manque (`dotnet run build_tools\MakeIcon.cs`), publie
l'application dans `build\app`, compile le lanceur en y incorporant l'application, et
produit `dist\GreenshotUpdater.exe`.

Tests (hors écran, sans toucher au registre, à Greenshot.ini ni au Bureau réels) :

```
dotnet test tests\GreenshotUpdater.Tests
```

## Structure

```
build.cmd                         construction du livrable
Directory.Build.props             version et réglages communs
assets\icon.ico, icon.png         icône (générée par build_tools\MakeIcon.cs)
src\Shared\Switches.cs            arguments /demarrage et /lanceur (lanceur et application)
src\GreenshotUpdater.Core\        logique sans interface et modèles de vue
    AppPaths, AppSettings, AppArguments, Log
    VersionText, GitHubRelease     versions, API GitHub
    InstalledGreenshot, RegistryStore   lecture du registre (abstraction)
    GreenshotIni, GreenshotConfig  modification de Greenshot.ini
    Processes, Winget              Greenshot, winget, programmes externes
    Shortcuts, Setup               raccourcis, installation, désinstallation
    UpdateService                  vérification et mise à jour
    ViewModels\                    fenêtre de réglages, proposition de mise à jour
src\GreenshotUpdater\             application WPF (GreenshotUpdater.App.exe)
src\GreenshotUpdater.Launcher\    lanceur net48 (GreenshotUpdater.exe)
tests\GreenshotUpdater.Tests\     tests xUnit
```
