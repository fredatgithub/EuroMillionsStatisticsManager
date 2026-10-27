# EuroMillionsStatisticsManager

Au démarrage, l’application crée un dossier `Archives` à côté de l’exécutable s’il
n’existe pas, puis charge les fichiers `*.csv` de ce dossier dans l’onglet
`Historique`. La première ligne de chaque fichier doit contenir les noms de
colonnes. Les séparateurs virgule, point-virgule et tabulation sont détectés
automatiquement. Une colonne `Fichier source` permet d’identifier le fichier
d’origine de chaque ligne.

L’onglet `Log` affiche les actions et résultats du chargement. Le journal est
également enregistré à côté de l’exécutable dans un fichier
`EuroMillionsStatisticsManager-YYYY-MM-DD_HH-MM-SS.log` pour chaque lancement.
