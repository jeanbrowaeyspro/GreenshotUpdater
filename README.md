# GreenshotUpdater

Petit utilitaire Windows qui tient [Greenshot](https://getgreenshot.org) à jour : à
l'ouverture de session, il compare la version installée à la dernière version stable
publiée sur GitHub et, si une version plus récente existe, propose de l'installer en
silence par winget. Il remplace la vérification intégrée de Greenshot, qui peut être désactivée.

Outil indépendant, non affilié au projet Greenshot ni approuvé par lui.

Livrable : un seul fichier, `GreenshotUpdater.exe` (environ 420 Ko), sans droits
administrateur pour l'installer.

## Installation

1. Lancer `GreenshotUpdater.exe` (depuis Téléchargements, une clé USB…).
2. Si le runtime .NET Desktop 10 (x64) manque, le lanceur propose de le télécharger
   depuis Microsoft (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`)
   et de l'installer ; l'installeur demande lui-même l'élévation (UAC).
3. La fenêtre d'installation s'ouvre : elle ne montre que les options et le bouton
   "Installer" (ni versions de Greenshot, ni vérification, ni désinstallation).
4. "Installer" copie le lanceur, seul fichier du dossier, dans
   `%LOCALAPPDATA%\Programs\GreenshotUpdater`, applique les options et affiche
   "Installation terminée.". Après OK, la fenêtre se ferme et le lanceur installé est
   démarré avec `/demarrage` (instance unique libérée avant) : première vérification,
   silencieuse s'il n'y a rien à proposer (voir plus bas).

Lancé depuis le dossier d'installation (menu Démarrer, Bureau), la fenêtre de réglages
complète s'ouvre : bouton "Appliquer", versions, "Vérifier maintenant", "Désinstaller".
Relancer un `GreenshotUpdater.exe` plus récent puis "Installer" met à jour la copie
installée.

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

Une fois installé, la fenêtre affiche aussi la version installée de Greenshot et la dernière version
stable ; "Vérifier maintenant" propose la mise à jour si elle existe (voir plus bas).

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
   - "Installer" : mise à jour silencieuse, sans l'assistant de Greenshot (voir plus
     bas).
   - "Plus tard" : rien n'est mémorisé, la question revient au prochain démarrage.

"Vérifier maintenant" (fenêtre de réglages) ouvre la même fenêtre ; "Installer" y masque
aussi la fenêtre de réglages, qui est fermée à la fin de l'installation.

### Mise à jour silencieuse

1. La fenêtre de proposition devient une fenêtre d'attente : "Installation de
   Greenshot x…", "Autorisez la modification de l'appareil si Windows le demande.",
   barre de progression, sans bouton ni premier plan forcé (pour ne pas masquer
   l'UAC). Elle ne peut pas être fermée tant que l'installation tourne.
2. Greenshot est fermé (fermeture propre, puis arrêt forcé après 5 s), puis
   `winget upgrade --id Greenshot.Greenshot --exact --silent --accept-source-agreements --accept-package-agreements`
   est lancé, console cachée. Si winget est introuvable, GreenshotUpdater propose
   d'abord de télécharger l'installeur `Greenshot-INSTALLER-*-RELEASE.exe` de la
   release GitHub, puis le lance avec `/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
   Dans les deux cas l'installeur n'affiche rien : la fenêtre d'attente est la seule
   progression visible.
3. L'UAC reste demandée par Windows (installation de Greenshot pour tous les
   utilisateurs, cas habituel). Les choix de l'installation précédente (dossier, composants, portée)
   sont repris par Inno Setup.
4. La fenêtre d'attente reste affichée jusqu'à la fin de winget ou de l'installeur, puis :
   - succès : Greenshot est relancé s'il ne tourne pas, la version installée est relue
     dans le registre et la fenêtre devient "Greenshot x est installé." avec OK (si la
     version relue n'est pas celle attendue, le message le dit) ; OK ferme tout ;
   - annulation (UAC refusée, installation annulée : codes `0x8A15010C`, `1223`,
     `0x800704C7`, `2` et `5`) : fermeture sans message, Greenshot relancé s'il
     tournait avant ;
   - échec : la fenêtre devient le message d'erreur avec le code de sortie, OK ferme ;
     Greenshot relancé s'il tournait avant.

Switches vérifiés dans le manifeste winget `Greenshot.Greenshot` 1.3.323
(`manifests/g/Greenshot/Greenshot/1.3.323/Greenshot.Greenshot.installer.yaml` du dépôt
`microsoft/winget-pkgs`) : `InstallerType: inno`, sans `Silent` ni
`SilentWithProgress` propres, seulement `Custom: /ALLUSERS` (portée machine) ou
`/CURRENTUSER` (portée utilisateur). `--silent` donne donc les switches Inno par défaut
de winget, `/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`, ceux du repli sans winget.

Une seule instance tourne à la fois : l'instance unique reste prise pendant
l'installation (un lancement depuis le menu Démarrer à ce moment se ferme aussitôt) et
n'est libérée qu'à la fin. Le déroulement est noté dans
`%LOCALAPPDATA%\GreenshotUpdater\journal.log` (au-delà de 256 Ko, l'ancien journal
devient `journal.old.log`).

## Désinstallation

Bouton "Désinstaller" de la fenêtre de réglages (menu Démarrer > GreenshotUpdater),
après confirmation : retrait de l'entrée Run, `UpdateCheckInterval=14` rétabli,
raccourcis et `settings.json` supprimés. Le dossier d'installation et
`%LOCALAPPDATA%\GreenshotUpdater\GreenshotUpdater.App.exe` (l'exécutable en cours) sont
supprimés quelques secondes après la fermeture par une commande `cmd` cachée. Le journal
(`%LOCALAPPDATA%\GreenshotUpdater\journal.log`) est conservé.

## Lanceur et application

`GreenshotUpdater.exe` est un lanceur .NET Framework 4.8 (présent sur tout Windows 10
et 11) qui embarque l'application WPF `GreenshotUpdater.App.exe` (.NET 10, exe unique
sans runtime embarqué). À chaque démarrage, il :

1. vérifie le runtime `Microsoft.WindowsDesktop.App` 10.x x64 (`DOTNET_ROOT`, registre
   `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\InstallLocation` vue 32 bits,
   `%ProgramFiles%\dotnet`) et propose de l'installer s'il manque ;
2. extrait `GreenshotUpdater.App.exe` dans `%LOCALAPPDATA%\GreenshotUpdater` s'il manque
   ou diffère (taille puis empreinte SHA-256) : rien n'est écrit à côté du lanceur, que
   ce soit dans le dossier de téléchargement ou dans le dossier d'installation ;
3. démarre l'application avec `/lanceur "<chemin du lanceur>"` suivi de sa propre ligne
   de commande, puis se termine.

Le chemin du lanceur est transmis par l'argument `/lanceur` plutôt que par une variable
d'environnement : il reste visible dans le journal et n'est pas hérité par les
programmes lancés ensuite (winget, Greenshot). L'application s'en sert pour savoir quel
fichier copier à l'installation.

L'interface suit le thème clair ou sombre de Windows (thème Fluent), y compris quand il
change pendant l'exécution. Tous les messages de l'application (questions, informations,
erreurs, proposition de mise à jour, attente et fin de l'installation) passent par une seule fenêtre de dialogue WPF
(`DialogWindow`, modèle de vue `DialogViewModel`) qui suit ce thème, centrée sur la
fenêtre active ou à l'écran ; seuls les messages du lanceur (runtime .NET manquant,
téléchargement) restent des boîtes Windows classiques, affichées avant que .NET 10 soit
disponible. La version (`<Version>` de `Directory.Build.props`) est
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
src\Shared\AppLocations.cs        nom et emplacement de l'application extraite (lanceur et application)
src\GreenshotUpdater.Core\        logique sans interface et modèles de vue
    AppPaths, AppSettings, AppArguments, Log
    VersionText, GitHubRelease     versions, API GitHub
    InstalledGreenshot, RegistryStore   lecture du registre (abstraction)
    GreenshotIni, GreenshotConfig  modification de Greenshot.ini
    Processes, Winget              Greenshot, winget, programmes externes
    Shortcuts, Setup               raccourcis, installation, désinstallation
    UpdateService                  vérification et mise à jour
    InnoSetup                      arguments silencieux de l'installeur de Greenshot (sans winget)
    ViewModels\                    fenêtre de réglages, boîtes de dialogue, proposition de mise à jour
src\GreenshotUpdater\             application WPF (GreenshotUpdater.App.exe) : SettingsWindow, DialogWindow
src\GreenshotUpdater.Launcher\    lanceur net48 (GreenshotUpdater.exe)
tests\GreenshotUpdater.Tests\     tests xUnit
```

## Licence

Distribué sous licence MIT (voir le fichier `LICENSE`).
