# Sidekick - notes de développement

Les chemins et commandes de ce guide sont relatifs à la racine du projet.

Sidekick est une boîte à outils Dalamud personnelle pour Final Fantasy XIV, destinée à son auteur et à un petit comité. `/sidekick` ou `/sk` ouvre cinq onglets : **Chat**, **Focus**, **Penumbra Rules / Règles Penumbra**, **Commands / Commandes** et **Settings / Paramètres**.

L’interface est en anglais par défaut ; le français est disponible dans **Settings → Language → Français**. Le champ affiche **Langue** en français, sans titre supplémentaire ni infobulle. Le choix est immédiat et persistant. Les termes techniques usuels restent inchangés, notamment Toggle, Redraw all, Self et Furniture, ainsi que les noms réels des mods et collections. Les explications sont accessibles derrière les boutons **? entourés**, au survol ou au focus clavier. Les états et erreurs utiles restent visibles. Les confirmations de commande ne sont plus affichées dans le chat ; les erreurs réelles restent signalées.

## Charger la version locale

Aucune URL publique de dépôt n’est encore publiée. Le build local utilise Dalamud API 15 et demande au minimum Dalamud `15.0.3.9`.

1. Lancez FFXIV avec XIVLauncher et Dalamud activé.
2. Dans `/xlsettings` → **Experimental** → **Dev Plugin Locations**, ajoutez :

   ```text
   <Sidekick>\src\Sidekick\bin\x64\Release\Sidekick.dll
   ```

3. Enregistrez, puis ouvrez `/xlplugins`. Utilisez **Scan Dev Plugins** si nécessaire et chargez Sidekick.
4. Ouvrez `/sidekick`. Pour actualiser un DLL déjà chargé, déchargez puis rechargez Sidekick dans `/xlplugins`.

Le script de validation produit aussi `artifacts/Sidekick-0.10.1.0.zip`. Si vous utilisez une copie extraite ailleurs, remplacez ses fichiers, y compris les dossiers `fr` et `images`, puis rechargez le plugin. Le manifeste local porte la mention **Local build** comme auteur technique de développement ; le vrai nom d’auteur reste à renseigner avant publication.

## Commandes et paramètres

L’onglet **Commands / Commandes** classe les commandes par catégories Général, Emotes et Focus. Il présente les alias `/sidekick` et `/sk`, la sélection `/sk cpose N` avec les exemples 1 à 7, et `/sk focus`, `/sk focus on`, `/sk focus off`. Les limites de poses dépendent de la posture actuelle. Chaque ligne possède un bouton à icône pour copier la commande. `/sidekick` accepte les mêmes arguments que `/sk`.

L’onglet **Settings / Paramètres** rassemble la langue et les réinitialisations :

- **Chat** réactive la coloration et restaure sa couleur par défaut, en conservant les mots-clés.
- **Focus** désactive le module et restaure ses critères, sa portée, ses canaux et son opacité par défaut, en conservant les participants enregistrés et leurs cases cochées ou décochées.

Ces actions préservent la langue et les autres modules, notamment les règles Penumbra. Le changement de langue traduit aussi les noms de zones affichés dans les menus, sans changer les identifiants ou les brouillons en cours. Il ne change ni la langue Dalamud ni les règles de détection des noms.

### Ancien plugin Highlight

Déchargez l’ancien Highlight et retirez son ancien chemin de développement pour éviter deux traitements du chat. Sidekick importe le fichier voisin `Highlight.json` seulement lorsque `Sidekick.json` est absent. L’original reste intact. Un fichier Sidekick existant a priorité ; un fichier illisible ou d’une version future n’est pas remplacé par des valeurs par défaut sur disque.

## Chat

Activez **la coloration des mentions** et choisissez une couleur RGB. Le prénom ou le nom du personnage connecté suffit. Le corps du message entier est coloré ; l’émetteur, l’heure et le canal restent inchangés. Les propres messages du personnage et `/echo` sont inclus.

La comparaison est littérale, insensible à la casse et indépendante de la langue de l’interface. Les accents restent distincts, les copies de comparaison utilisent Unicode NFC et les frontières respectent les lettres, marques, chiffres, connecteurs, apostrophes et traits d’union internes. `Ann` correspond à `Bonjour Ann !`, mais pas à `Annabelle` ni à `Ann-Marie`. La détection cesse sans identité locale valide.

Les changements concernent les prochains messages. L’historique natif et la base ChatTwo ne sont pas réécrits. Aucun serveur, télémétrie, journal de conversation ou assembly ChatTwo obligatoire n’est ajouté.

### Mots-clés et aperçu

Choisissez un profil enregistré ou renseignez le nom complet et le **monde d’origine**. Un profil peut être préparé hors connexion. Saisissez un mot ou une expression, puis cliquez sur **Ajouter**.

- **Compte coché** : le mot s’applique à tous les personnages utilisant cette configuration locale, même sur plusieurs comptes de jeu. Aucun identifiant de compte ni synchronisation réseau n’est utilisé.
- **Compte décoché** : le mot appartient au personnage et au monde d’origine sélectionnés. Le monde visité ne remplace pas le monde d’origine.

Les mots globaux et ceux du personnage se cumulent. Limites : 64 entrées Compte, 64 par personnage et 128 caractères par entrée. Les entrées vides, caractères de contrôle et doublons d’une même portée sont refusés. Modifier une entrée conserve son identifiant stable. Changer le nom ou le monde d’origine d’un personnage nécessite d’actualiser ses entrées.

L’aperçu utilise la même détection et la même transformation SeString que le chat, avec le profil sélectionné et les mots Compte. Il ne change pas l’identité réellement détectée en jeu.

### Apparence

La mise en évidence change la couleur du texte. Un vrai fond façon marqueur n’est pas implémenté : aucun payload commun de fond ni callback public ChatTwo adapté n’a été identifié dans les contrats inspectés. La [conversion SeString ChatTwo](https://github.com/Infiziert90/ChatTwo/blob/ca9aa992da06846c081e44841fa3145d5d401566/ChatTwo/Util/ChunkUtil.cs) traite les couleurs de texte et de contour ; le [guide IPC](https://github.com/Infiziert90/ChatTwo/blob/ca9aa992da06846c081e44841fa3145d5d401566/ipc.md) ne fournit pas d’intégration de fond de message.

Sidekick ne gère plus directement les bulles de dialogue. L’utilisateur indique que le client natif reprend déjà la coloration du message ; l’ancienne option et son intégration native ont été retirées.

## Focus

Focus garde les participants d’une scène normalement lisibles et atténue le corps des autres nouveaux messages dans les canaux choisis. Il fonctionne indépendamment de la coloration des mentions. Il est désactivé par défaut.

- Le bouton cible et son état actif / inactif se trouvent au début de l’onglet Focus.
- `/sk focus` bascule son état. `/sk focus on` et `/sk focus off` le définissent explicitement, notamment dans une macro. `/sidekick` accepte les mêmes arguments.
- La portée se règle de 1 à 300 **yalms**, affichés **y**, avec 15 y par défaut. La distance est mesurée en 3D à la réception du message, sur le thread du framework. Les valeurs enregistrées sont conservées sans conversion : une ancienne portée de 10 devient 10 y. Les entiers s’affichent sans décimale inutile. Seule leur ancienne désignation en mètres était incorrecte.
- Les participants se choisissent dans la liste recherchable des joueurs proches, actualisée toutes les 500 ms lorsque le sélecteur est ouvert. Le bouton plus permet aussi une saisie manuelle, sans connexion du personnage concerné. Le nom complet et le monde d’origine identifient chaque entrée ; le monde visité ne les remplace pas. La liste contient au plus 64 entrées, que l’on peut cocher, décocher ou supprimer.
- La portée et les participants peuvent être utilisés séparément. Ensemble, **OU** conserve les joueurs proches ou sélectionnés ; **ET** conserve seulement les participants sélectionnés qui sont aussi à portée. OU est le choix initial.
- L’opacité des autres messages se règle de 1 % à 100 %, avec **25 % par défaut**. À 100 %, Focus ne transforme rien.
- Dire, emotes personnalisées et emotes standard sont sélectionnés au départ. Les autres canaux de joueurs, dont les murmures, restent optionnels. Les messages système, PNJ, GM et vos propres messages sont toujours préservés. Avec Focus actif et les deux critères décochés, tous les autres auteurs identifiés des canaux sélectionnés prennent l’opacité configurée, même s’ils sont proches ou enregistrés dans la liste. Sans canal sélectionné, Focus n’atténue rien.

La coloration Sidekick s’applique avant Focus. Les couleurs explicites du texte, des liens et leurs contours ou ombres explicites gardent leurs composantes RGB ; leur alpha est multiplié, sans remplacer une transparence déjà présente. Pour le texte sans couleur explicite, les messages atténués prennent les couleurs de canal configurées dans **FFXIV**, y compris dans Chat 2. Les couleurs de canal personnalisées uniquement dans Chat 2 ne sont donc pas utilisées pour ces messages atténués. Les participants conservés gardent le rendu habituel de chaque chat. Ce compromis a été retenu explicitement, car l’API publique ChatTwo ne fournit pas de modulation d’opacité indépendante du RGB.

Le pipeline conserve les payloads de liens, styles, icônes et données inconnues. Les icônes, l’horodatage et le nom de l’émetteur ne sont pas atténués. L’alpha est encodé sur 8 bits : 25 % correspond à 64/255. Le minimum proposé est 1 %, car le décodeur ChatTwo 1.41.2.0 traite un alpha nul omis comme opaque.

Un auteur identifié mais absent des objets chargés n’a pas de distance connue : il est hors de portée et peut rester lisible grâce à la liste en mode OU. Un auteur impossible à identifier avec certitude reste inchangé. Les noms seuls sont résolus seulement parmi les personnages proches, sans ambiguïté entre mondes. Les liens joueur sont lus via les expressions publiques Lumina, sans recherche de service ni lecture du contenu pour deviner l’auteur. Le nom d’un participant présent uniquement dans le corps n’identifie jamais l’émetteur.

Les couleurs `UIColor` et les paramètres numériques globaux du texte sont résolus avec Lumina et les structures publiques FFXIVClientStructs. Une couleur impossible à résoudre laisse le message lisible et incrémente un compteur sans conserver son contenu. Aucun hook, accès à la base ChatTwo ou historique Sidekick n’est ajouté.

L’activation, les canaux, la portée, l’opacité et la liste sont persistants et communs aux personnages utilisant cette configuration locale. Déplacer son personnage, changer les réglages ou désactiver Focus ne réécrit pas les anciens messages, qui peuvent rester atténués après désactivation. La remise à zéro du chat ne touche pas à Focus. Focus ne modifie pas les bulles natives.

### Validation de Focus en jeu

Le décodeur de couleurs de l’assembly installé **ChatTwo 1.41.2.0** a été vérifié hors jeu avec des payloads rouge, vert, bleu, noir et un alpha faible : la valeur alpha est conservée dans la couleur transmise au rendu. Cela ne valide pas le résultat affiché. Le rendu natif, les contours hérités, les éléments spéciaux et les interactions ChatTwo restent à vérifier dans FFXIV.

1. Rechargez Sidekick, ouvrez `/sk`, puis Focus. Gardez Dire et les emotes, une portée de 15 y et une opacité de 25 %.
2. Activez avec `/sk focus`. Faites envoyer des messages par une autre personne, à l’intérieur puis à l’extérieur de la portée. Vos messages doivent rester habituels.
3. Ajoutez cette personne à la liste, éloignez-vous et comparez OU et ET. Vérifiez aussi la portée seule et la liste seule.
4. Faites envoyer un lien joueur, un lien objet et une mention de votre personnage. Vérifiez les couleurs RGB, l’opacité, les liens cliquables et l’absence de fuite vers le message suivant, dans les deux chats.
5. Testez un canal non sélectionné, des murmures, `/echo`, le chargement d’une zone et une déconnexion. Changez la couleur de canal FFXIV puis vérifiez les messages suivants. Testez aussi des couleurs personnalisées dans Chat 2 pour constater le compromis décrit ci-dessus.
6. Utilisez `/sk focus off`, puis rechargez le plugin et vérifiez les réglages persistants, l’absence de double abonnement et les nouveaux messages. Les anciens messages ne doivent pas être réécrits.

## Sélection directe de cpose

La sélection de pose est disponible par commande, sans dépendance à Character Select+ et sans configuration ou profil supplémentaire :

```text
/sk cpose 1
/sk cpose 5
```

`/sidekick cpose N` fonctionne aussi. La numérotation commence à **1**, qui représente le premier indice natif, `0`. Le numéro concerne la posture actuelle : debout, arme sortie, assis sur un siège, assis au sol, allongé, ombrelle ou accessoire de mode. Les animations dépendent du personnage et de cette posture. Un numéro disponible debout peut ne pas l’être assis : Sidekick lit la plage native et affiche la plage valide en cas de dépassement, sans choisir une autre pose à votre place. La commande ne fait pas asseoir ou allonger un personnage debout.

Character Select+ a servi de référence de fonctionnement : ses profils conservent des indices `IdlePoseIndex`, `SitPoseIndex`, `GroundSitPoseIndex` et `DozePoseIndex`, puis restaurent les valeurs de `PlayerState.SelectedPoses` et l’état de pose du personnage. Ce sont des indices, pas des fichiers d’animation. Voir son [modèle de personnage](https://github.com/IcarusXIV/Character-Select-/blob/8d8f83ed683d1fab3fe9504258fe097e5eba1d24/CharacterSelectPlugin/Character.cs), son [restaurateur](https://github.com/IcarusXIV/Character-Select-/blob/8d8f83ed683d1fab3fe9504258fe097e5eba1d24/CharacterSelectPlugin/PersistentPoseRestorer.cs) et son [PoseManager](https://github.com/IcarusXIV/Character-Select-/blob/8d8f83ed683d1fab3fe9504258fe097e5eba1d24/CharacterSelectPlugin/PoseManager.cs). Sidekick ne lit ni ne modifie ses profils, n’appelle pas son assembly ou son IPC et n’utilise pas ECommons.

L’implémentation Sidekick prépare l’indice précédent dans les structures publiques, puis demande **une seule exécution** de l’emote native de changement de pose via `EmoteManager.ExecuteEmote`. La préparation et l’appel se déroulent dans le même callback du framework, sans boucle destinée à afficher chaque pose intermédiaire. L’emote est identifiée à partir de `/cpose` dans les feuilles Lumina `Emote` / `TextCommand` ; aucun identifiant numérique n’est deviné. `EmoteController.GetPoseKind` et `GetAvailablePoses` déterminent la famille et la plage de poses. Les contrats ont été vérifiés dans les assemblies API 15 installées et les [structures publiques](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Control/EmoteController.cs).

Sidekick confirme le résultat pendant au plus 1,5 seconde. En cas de refus ou d’absence de confirmation, il ne lance pas de cycle de rattrapage. Les valeurs de préparation sont restaurées seulement si elles sont encore détenues par Sidekick et si le même personnage et la même posture sont toujours présents. Une modification manuelle ultérieure est conservée. La dernière demande remplace les autres demandes encore en attente ; aucune file illimitée, tâche de fond ou adresse de personnage conservée n’est utilisée. Hors demande active, le module ne lit pas les objets à chaque frame.

Une déconnexion, un changement de personnage ou de posture annule la demande. Le module refuse les chargements, le combat, les montures et GPose ; le jeu doit également autoriser l’emote. Toutes les lectures et écritures natives ont lieu sur le thread du framework. Le module ne possède aucun réglage de pose persistant dans Sidekick et ne modifie pas les autres familles de poses.

L’utilisateur a confirmé le fonctionnement de la sélection de cpose. La visibilité par les autres joueurs et l’ensemble des postures n’ont pas été observés pendant le développement. Les tests vérifient la numérotation, les limites, l’exécution unique, la confirmation interne silencieuse, l’annulation, la restauration des valeurs détenues et le déchargement avec un adaptateur simulé.

Pour la validation manuelle :

1. Debout et immobile, essayez `/sk cpose 1`, puis `/sk cpose 5` et comparez avec les poses du jeu. Aucun passage visible par les poses intermédiaires ne doit apparaître.
2. Essayez un numéro trop grand. Vérifiez la plage indiquée et l’absence de changement.
3. Répétez assis sur un siège, assis au sol et allongé ; testez également l’arme sortie ou une ombrelle lorsque le jeu l’autorise.
4. Vérifiez le résultat depuis un autre client et après un déplacement. Si Character Select+ est chargé avec une sauvegarde ou restauration automatique de poses, vérifiez leur coexistence.
5. Testez les commandes rapprochées, une modification manuelle, un chargement de zone et le déchargement de Sidekick pendant une demande.

## Règles Penumbra

Les boutons utilisent les icônes FontAwesome de Dalamud : plus pour ajouter, crayon pour modifier, poubelle pour supprimer, flèche circulaire pour actualiser, flèches verticales pour réordonner et cible pour reprendre le lieu actuel. Leurs libellés français restent disponibles en infobulle, y compris au focus clavier. Enregistrer et Annuler associent icône et texte.

La liste affiche l’état, le nom, l’action, la collection, le Redraw et le nombre de mods de chaque règle. **Ajouter une règle** et **Modifier** ouvrent un éditeur distinct. **Enregistrer** et **Annuler** restent visibles en bas ; les changements du brouillon ne s’appliquent qu’après enregistrement. Les colonnes lieu / Penumbra s’adaptent à la largeur disponible.

1. Activez les règles de lieu.
2. Ajoutez une règle nommée et choisissez son lieu, ou utilisez **Utiliser le lieu actuel**.
3. Choisissez l’action **Activer**, **Désactiver** ou **Toggle**.
4. Sélectionnez la **collection cible** et le **Redraw**.
5. Cochez les mods et enregistrez.

Les champs du nom complet et du monde d’origine sont alignés côte à côte. Le lieu actuel tient sur une même ligne lorsque la largeur le permet. La section Adresse regroupe secteur, parcelle et division sur une ligne ; les deux numéros n’ont pas de boutons de pas superflus. Les menus restent à la largeur de leur champ, avec une recherche intégrée et un défilement vertical borné. Les libellés longs sont accessibles en infobulle ; seuls les noms ambigus affichent leur identifiant.
Le bouton cible « Utiliser le lieu actuel » se trouve à droite du titre Lieu, sur la même ligne. Les boutons à icône centrent le dessin visible du glyphe dans le rectangle réellement affiché, indépendamment de la largeur d’avance de la police.
### Lieux et logements

Toutes les conditions se cumulent : monde actuel, zone, instance publique, quartier résidentiel, secteur, parcelle, pièce, division et intérieur / extérieur. **Tous** ignore une condition ; `0` ignore un champ numérique. Au moins une condition est nécessaire.

Une zone résidentielle inclut les intérieurs qui appartiennent à son quartier. **La Coupe + Intérieur** reconnaît donc une maison à La Coupe malgré son identifiant de territoire technique distinct. Le monde, le secteur et la parcelle restent vérifiés. Choisir un intérieur précis ou une zone non résidentielle conserve une correspondance exacte. La capture automatique utilise le quartier pour une adresse résidentielle ; l’affichage du lieu distingue quartier et intérieur.

L’instance publique désigne la copie numérotée 1, 2, 3… et non une session unique de donjon. Les numéros résidentiels commencent à 1. Une parcelle indisponible ne devient pas la parcelle 1 : la détection utilise la parcelle courante fournie par le jeu, pas un rayon autour d’une maison.

### Collections, actions et nettoyage

La collection cible est choisie dans la liste recherchable des vraies collections Penumbra, dont `Default`. Son GUID stable résiste au renommage. Une collection absente est ignorée, sans repli vers une autre collection. **Collection du personnage** conserve le comportement des anciennes règles et suit sa collection effective. Ouvrir le sélecteur actualise les listes sans réessayer les règles ignorées.

La dernière règle correspondante gagne pour chaque couple collection/mod ; les flèches changent cet ordre. Toggle inverse une fois l’état permanent résolu lors de l’activation. Les effets restent stables tant que la règle correspond. Le lieu est lu sur le thread du framework toutes les 500 ms et doit rester stable une seconde. Les chargements conservent les effets jusqu’à connaissance de la destination.

Les modifications passent par les réglages temporaires publics Penumbra. Priorités et options résolues, y compris héritées, sont préservées dans les réglages permanents. Les réglages temporaires étrangers sont ignorés. La clé d’identification négative propre à Sidekick permet les modifications manuelles ; le nettoyage vérifie la source et les valeurs avant de retirer un état encore détenu.

La sortie du lieu, la mise en pause, la déconnexion et le déchargement retirent les effets. Changer une collection cible nettoie d’abord la collection d’origine. **Actualiser et réessayer** recharge les listes et réessaie les mods bloqués ou absents ; autrement, l’activation suivante ou une modification de règle permet un nouvel essai. Renommer le répertoire d’un mod nécessite d’actualiser sa sélection dans les règles.

Limites : 64 règles et 64 mods par règle. La collection sélectionnée peut concerner plusieurs acteurs ou objets. Le bouton de réinitialisation du chat ne touche pas aux mots-clés ni aux règles Penumbra.

### Redraw

- **Collection members** : les acteurs de la collection sélectionnée.
- **Redraw all** : tous les acteurs.
- **Self** : le personnage local.
- **Furniture (intérieur)** : le mobilier intérieur pris en charge par Penumbra.

Le Redraw choisi sert à l’application et au retrait ; le nettoyage conserve la politique d’origine même après modification. Les requêtes sont regroupées. Redraw all couvre les acteurs, pas Furniture. Les acteurs utilisent l’IPC public ; Furniture utilise la commande locale `/penumbra redraw furniture` via le gestionnaire de commandes Dalamud. La [version Penumbra 1.7.2.1](https://github.com/xivdev/Penumbra/blob/1.7.2.1/Penumbra/UI/ModsTab/RedrawFooter.cs) limite ce rafraîchissement à la plupart des meubles intérieurs, sans support extérieur. Aucun accès mémoire propre au mobilier n’est ajouté.

Lifestream reste facultatif et n’est pas appelé. Ses [instances](https://github.com/NightmareXIV/Lifestream/blob/b36320170faafc8315f7585edb45cfbcd34e7046/Lifestream/Services/InstanceHandler.cs) et [comparaisons d’adresses](https://github.com/NightmareXIV/Lifestream/blob/b36320170faafc8315f7585edb45cfbcd34e7046/Lifestream/Utils.cs) ont servi de référence. Sidekick lit directement `UIState.PublicInstance`, `HousingManager`, `IClientState` et `IPlayerState.CurrentWorld`, sans ECommons, voyage automatique ou hook propre.

## Compilation, localisation et validation

Prérequis : Windows x64, SDK .NET 10 et références Dalamud API 15. `Dalamud.NET.Sdk/15.0.0` et les dépendances de test sont verrouillés. Les références viennent par défaut de `%APPDATA%/XIVLauncher/addon/Hooks/dev` ; `DALAMUD_HOME` permet un autre chemin.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Build.ps1
```

Le script choisit le SDK ignoré `.tools/dotnet` lorsqu’il existe. Il restaure les dépendances verrouillées, compile Release, vérifie le format configuré, exécute les tests et contrôle l’archive officielle du packager. Il produit le ZIP versionné et son SHA-256 dans `artifacts/`. Pour les commandes individuelles, utilisez le SDK .NET 10 de la machine ou `./.tools/dotnet/dotnet.exe` :

```powershell
dotnet build Sidekick.sln -c Release
dotnet test Sidekick.sln -c Release
```

L’archive contient `Sidekick.dll`, `Sidekick.json`, `Sidekick.deps.json`, `fr/Sidekick.resources.dll` et `images/icon.png`, sans assemblies du jeu, de Dalamud ou de test. Le packager officiel utilise une liste explicite de fichiers et conserve l’icône dans le ZIP. Le validateur contrôle la traduction, l’icône PNG carrée de 64 à 512 pixels, son identité avec le fichier source et les métadonnées du manifeste.

La configuration est en version 7. Les versions 0-6 sont migrées en préservant les préférences du chat, de Focus, les mots-clés et les règles Penumbra. La langue absente ou inconnue devient l’anglais. L’ancienne option de bulles est ignorée. La clé JSON historique `DistanceMeters` est conservée pour lire les distances sans changer leur valeur ; le code et l’interface les expriment en yalms. Une sélection de canaux vide reste vide au rechargement. Un fichier illisible ou futur désactive l’enregistrement pour protéger l’original. Les défauts de sauvegarde sont affichés.

Les textes anglais neutres restent dans `Strings.resx`, les textes français dans `Strings.fr.resx`, avec accès typé et anglais par défaut. La préférence `LanguageCode` contient `en` ou `fr` et s’applique à chaque chargement. Les cultures absentes retombent sur l’anglais. Les tests vérifient les traductions, paramètres de format, termes techniques, persistance et repli. Les identifiants ImGui restent indépendants des libellés traduits. Les métadonnées Dalamud restent en anglais et indiquent seulement `/sidekick` et `/sk` pour ouvrir Sidekick.

### État des vérifications

| Élément | Vérification |
|---|---|
| Détection Unicode, SeString, payloads, couleurs, profils et migrations | Tests automatisés |
| Coloration de base du chat natif et de Chat 2 | Confirmée en jeu par l’utilisateur |
| Focus : critères OU / ET, portée, identité, canaux, persistance, composition avec les mentions et cycle de vie | Tests automatisés |
| Focus : conservation de l’alpha par ChatTwo 1.41.2.0 | Décodeur et conversion RGBA de l’assembly installé exécutés hors jeu ; rendu final non vérifié |
| Focus : rendu natif et ChatTwo, liens, contours et canaux réels | Validation en jeu à effectuer |
| cpose : commandes, indices, plage par posture, exécution unique, annulation et cycle de vie | Tests automatisés avec adaptateur simulé ; signatures natives inspectées |
| cpose : pose effectivement appliquée, absence de poses intermédiaires et visibilité depuis un autre client | Validation en jeu à effectuer |
| Liens, styles imbriqués, RGB primaires, rechargement et ordre de chargement ChatTwo | Matrice complète à terminer en jeu |
| Langue, migrations 0-6, réinitialisations indépendantes et catalogue des commandes | Tests automatisés |
| Lieux, quartiers / intérieurs, priorités, Toggle, collections, Redraw et nettoyage | Tests de règles et d’IPC simulé |
| Activation des mods dans un intérieur en retirant l’ancien filtre de zone exacte | Confirmée par l’utilisateur ; correspondance quartier / intérieur corrigée ensuite |
| Contrats Penumbra 1.7.2.1, API 5 | Signatures de l’assembly installé inspectées, dont GetCollections.V5, RedrawAll.V5 et RedrawObject.V5 |
| Interface refondue | Liste de règles 0.7.0 rendue et inspectée hors jeu ; boutons FontAwesome 0.7.1, éditeur et interactions à confirmer dans FFXIV |
| Installation et mise à jour via URL publique | Différées jusqu’à publication |
| Workflow GitHub Actions | Présent, non exécuté à distance pendant cette préparation |

Le chat passe par `IChatGui.CheckMessageHandled` avant les consommateurs finaux. ChatTwo reste optionnel et reçoit la même transformation en amont de `ChatMessageUnhandled`. Le manifeste conserve `LoadSync=true` et `CanUnloadAsync=false` pour le cycle de vie natif des poses et de Penumbra. Un build réussi ne démontre pas une compatibilité complète en jeu.

Pour terminer la validation, vérifier les deux chats, `/echo`, les liens, couleurs intégrées et changements de personnage ; les visites de mondes / instances, logements, appartements, pièces de CL, modifications manuelles, collections absentes, chargements tardifs de Penumbra et déchargement de Sidekick. Vérifier aussi les cinq onglets, le changement de langue à chaud, les réinitialisations depuis Paramètres et la persistance après rechargement.

## Distribution par URL

L’icône est fournie par `https://www.aht.li/3997968/icon.png`, référencée dans `IconUrl`, et disponible localement dans `images/icon.png` à côté du DLL. Le fichier source `icon.png` est copié sans modification ; le téléchargement de l’URL fournie a été comparé au fichier local.

Dalamud conserve les icônes dans un cache qui peut survivre au rechargement d’un plugin. Si l’ancienne image reste visible, ouvrez `/xlplugins`, puis `/xldev` et choisissez **Plugins → Clear cached images/icons**. Le dossier `images` doit accompagner le DLL lors d’une copie manuelle. La copie du logo vers la sortie de compilation est forcée à chaque build.

`repo.json` est volontairement un tableau vide pour le moment. Avant la première publication, renseigner l’auteur, le dépôt source public, la licence et l’hébergement HTTPS. La CI valide et fournit des artefacts sans publier automatiquement.

Construire avec les vraies métadonnées, contrôler le ZIP, publier une archive versionnée, puis générer l’entrée de dépôt depuis son manifeste embarqué. Les liens DownloadLinkInstall et DownloadLinkUpdate doivent viser l’archive disponible. L’URL ajoutée dans les paramètres expérimentaux de Dalamud sera celle du tableau JSON brut, pas une page HTML ni le ZIP. Ce dépôt personnalisé ne nécessite pas de canal de test ou IsTestingExclusive. Les versions publiées doivent rester immuables et croissantes.

### Première publication sur GitHub

Les URL ci-dessous sont des modèles : remplacer `VOTRE_COMPTE` par le véritable propriétaire et adapter le nom du dépôt ou de la branche. Aucune de ces URL n’est déjà publiée pour Sidekick.

1. Créez un dépôt GitHub public, par exemple `Sidekick`. Ajoutez les sources et choisissez la licence du dépôt. Dans `src/Sidekick/Sidekick.csproj`, renseignez le vrai `Author` et ajoutez `RepoUrl` avec l’URL réelle du dépôt. Conservez `Name`, `InternalName` et l’assembly `Sidekick`.
2. Exécutez `./scripts/Build.ps1`. Créez une release GitHub portant un tag correspondant à la version, par exemple `v0.10.1.0`, et joignez l’archive validée `artifacts/Sidekick-0.10.1.0.zip`. Son lien direct suivra le modèle `https://github.com/VOTRE_COMPTE/Sidekick/releases/download/v0.10.1.0/Sidekick-0.10.1.0.zip`. Vérifiez que ce lien télécharge l’archive publiquement.
3. Générez `repo.json` à partir du manifeste de cette compilation. Le fichier doit contenir un tableau JSON de manifestes ; pour Sidekick seul, un tableau d’une entrée. Ajoutez `DownloadLinkInstall` et `DownloadLinkUpdate` avec le lien direct de l’archive, `LastUpdate` avec l’horodatage Unix courant et `IsTestingExclusive` à `false`. Les autres métadonnées, dont `Author`, `RepoUrl`, `AssemblyVersion`, `DalamudApiLevel` et `IconUrl`, viennent de la même compilation. Aucun champ Testing n’est nécessaire.

Une fois la release publiée et le lien réel reporté dans `$download`, ces commandes PowerShell permettent de créer le tableau à partir du manifeste compilé :

```powershell
$entry = Get-Content -Raw -Encoding UTF8 ./src/Sidekick/bin/x64/Release/Sidekick.json | ConvertFrom-Json
$download = 'https://github.com/VOTRE_COMPTE/Sidekick/releases/download/v0.10.1.0/Sidekick-0.10.1.0.zip'
$entry | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $download -Force
$entry | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue $download -Force
$entry | Add-Member -NotePropertyName LastUpdate -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
$entry | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $false -Force
ConvertTo-Json -InputObject @($entry) -Depth 20 | Set-Content -Encoding UTF8 ./repo.json
```

4. Publiez le `repo.json` complété sur la branche principale. L’URL à communiquer suivra alors le modèle `https://raw.githubusercontent.com/VOTRE_COMPTE/Sidekick/main/repo.json`. Le JSON et le ZIP doivent être accessibles sans authentification, conformément au [contrat des dépôts personnalisés Dalamud](https://dalamud.dev/plugin-publishing/custom-repositories/).
5. En jeu, ouvrez `/xlsettings` → **Experimental → Custom Plugin Repositories**. Collez l’URL brute de `repo.json`, cliquez sur **+** et enregistrez. Ouvrez `/xlplugins`, recherchez **Sidekick** et installez-le. Retirez d’abord l’ancien chemin Dev Plugin de Sidekick pour passer à l’installation gérée par le dépôt tout en gardant la même configuration.

Pour une mise à jour, augmentez la version, validez et publiez d’abord un nouveau ZIP sous une nouvelle release, puis remplacez l’entrée du dépôt par celle du nouveau manifeste et de ses liens. Le workflow actuel valide les builds et fournit des artefacts ; il ne publie ni release ni dépôt automatiquement.
