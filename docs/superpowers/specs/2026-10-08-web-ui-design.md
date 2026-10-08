# Interface web du simulateur de vaisseau: conception du jalon 2

Date: 2026-10-08
Statut: en attente de relecture
Prérequis: jalon 1 (`docs/superpowers/specs/2026-10-08-ship-simulation-design.md`), branche `feat/ship-simulation`.

## 1. Objectif

Donner au simulateur une interface web pour piloter le vaisseau, observer sa télémétrie et gérer son câblage. La simulation reste en C#: le navigateur n'est qu'un client d'affichage et de commande.

Critères de succès:
- `dotnet run --project src/Starship.Web` lance un serveur local. La page s'ouvre sur `http://127.0.0.1:<port>/` et affiche le vaisseau en vol.
- Le joueur règle la puissance et la direction depuis la page, et le vaisseau accélère et tourne comme dans le CLI.
- Le schéma de câblage montre les appareils, le routeur, ses ports et les câbles avec leur santé. Le joueur peut brûler un câble, le remplacer, et ajouter un câble de secours vers la propulsion si le routeur a un port libre.
- La télémétrie affiche l'âge de chaque donnée et marque `STALE` celles qui sont périmées.
- Une vue 3D montre l'orientation et le déplacement du vaisseau.
- Les commandes et le mappage de l'état sont couverts par des tests automatisés.

## 2. Décisions

| Sujet | Décision |
|---|---|
| Hébergement de la sim | Serveur ASP.NET Core (net8.0), sim côté serveur à 60 Hz, aucune réécriture |
| Transport | WebSocket `/ws`, messages JSON |
| Client | HTML, CSS et JS vanilla dans `wwwroot/`, sans étape de build; Three.js depuis un CDN pour la vue 3D |
| Réseau | Écoute sur `127.0.0.1` uniquement, pas d'authentification |
| Joueurs | Un seul vaisseau partagé par tous les clients connectés |
| Hors périmètre | Voir section 9 |

## 3. Architecture

Nouveaux projets, ajoutés à la solution:
- `src/Starship.Web`: référence `Starship.Sim`. Contient `Program.cs`, `SimHost`, `SnapshotMapper`, les DTO de protocole et `wwwroot/`.
- `tests/Starship.Web.Tests`: xUnit, référence `Starship.Web`.

`Starship.Sim` n'est pas modifié, sauf si une propriété publique manque pour construire le snapshot. Dans ce cas l'ajout est minimal et couvert par un test.

### 3.1 SimHost
Service singleton qui possède le `DefaultShip` courant (créé par `ShipBuilder.CreateDefault`, graine fixe, qualité de câble 0.9 comme le CLI).
- Boucle à 60 Hz: accumulateur sur `Stopwatch`, plafonné à 0,25 s de rattrapage.
- Un verrou protège l'état. La boucle, les commandes et la prise de snapshot l'utilisent.
- Applique les commandes (section 5) et produit les snapshots (section 4).
- Le dernier `input` reçu reste appliqué tant qu'aucun nouveau n'arrive. Si plus aucun client n'est connecté, la manette et le joystick sont remis au neutre.

### 3.2 Endpoint WebSocket
`/ws` accepte plusieurs clients. Le serveur diffuse un snapshot à 20 Hz à chacun. Un client lent ne ralentit pas la sim: ses messages en retard sont abandonnés. Les messages entrants invalides (JSON mal formé, type inconnu, champ manquant) sont ignorés et comptés, sans fermer la connexion.

## 4. Snapshot (serveur vers client)

Un objet JSON par message, champs en camelCase:
- `tick`, `router` (nom du modèle), `dropped` (paquets perdus du routeur), `rejected` (nombre cumulé de commandes refusées ou invalides depuis le dernier `reset`).
- `body`: `position[3]`, `velocity[3]`, `orientation[4]` (quaternion x,y,z,w), `angularVelocity[3]`, `speed`.
- `controls`: `throttle`, `pitch`, `roll`, `yaw` (valeurs appliquées).
- `devices[]`: pour chaque appareil connu de l'ordinateur: `address`, `kind`, `powered`, `powerRatio`, `load`, `temperature`, `health`, `age` (ticks), `stale`. Un appareil jamais vu est présent avec `age` nul et `stale` vrai.
- `power`: `capacity`, `demand`, `online`.
- `cables[]`: `name`, `from`, `to` (appareil ou port, voir ci-dessous), `length`, `quality`, `health`, `burnt`, `errorRate`.
- `ports[]`: `index`, `dedicated` (type d'appareil ou null), `boundAddress` (ou null), `up`, `cable` (nom ou null).
- `canAddBackup`: vrai si le routeur a un port libre accepté par la propulsion.

Les extrémités de câble sont désignées par `{ "device": "Throttle" }` ou `{ "port": 3 }`. Les valeurs non finies sont remplacées par `0` pour garder un JSON valide.

## 5. Commandes (client vers serveur)

| Message | Effet | Validation |
|---|---|---|
| `{"type":"input","throttle":0.5,"pitch":0,"roll":0,"yaw":0}` | règle la manette et le joystick | valeurs non finies ignorées, bornes appliquées par les appareils |
| `{"type":"burn","cable":"throttle"}` | `Cable.Burn()` | nom de câble inconnu: ignoré |
| `{"type":"replace","cable":"throttle"}` | `Ship.Replace` par un câble neuf (qualité 0.9, même longueur, nom suffixé d'un compteur) | seulement si le câble existe |
| `{"type":"addBackup"}` | branche un second câble de la propulsion (nouvelle interface) vers un port libre du routeur | refusé si `canAddBackup` est faux ou si un secours existe déjà |
| `{"type":"reset","router":"basic"}` | recrée le vaisseau avec le modèle `basic`, `intermediate` ou `advanced` | modèle inconnu: ignoré |

Le serveur répond par le snapshot suivant, sans accusé séparé. Une commande refusée est comptée dans `rejected` (champ du snapshot) pour être testable.

## 6. Interface

Une page, quatre zones:
- **Cockpit**: curseur de puissance (glisser ou touches W/S), joystick 2D pour tangage et lacet (souris ou flèches), roulis sur A/D, Espace pour le neutre. Les entrées sont envoyées sur changement et au plus à 20 Hz.
- **Télémétrie et vol**: position, vitesse, cap, tableau des appareils (charge, température, âge, `STALE` en rouge), paquets perdus, état de l'alimentation.
- **Schéma de câblage (SVG)**: l'ordinateur et son routeur au centre avec ses ports numérotés, les appareils autour, les câbles en lignes colorées selon la santé et le taux d'erreur, brûlés en pointillé rouge. Un clic sur un câble ouvre « brûler » et « remplacer ». Un bouton « ajouter un câble de secours » est actif si `canAddBackup`. Un sélecteur de routeur envoie `reset`.
- **Vue de vol 3D (Three.js)**: maillage simple du vaisseau orienté par le quaternion, caméra qui le suit, champ d'étoiles qui défile avec la vitesse, traînée de trajectoire (200 derniers points).

Si le WebSocket se coupe, la page affiche « déconnecté » et tente de se reconnecter toutes les 2 s. Si Three.js n'a pas pu être chargé, la zone 3D affiche un message et le reste fonctionne.

## 7. Pannes et cas limites

- Aucun client connecté: la sim continue, entrées remises au neutre.
- Commandes simultanées de plusieurs clients: la dernière reçue gagne, pas d'arbitrage.
- `reset` pendant que d'autres clients sont connectés: tous reçoivent le nouvel état au snapshot suivant.
- Un nom de câble absent après un `replace` (renommage) est géré côté client en se basant uniquement sur les snapshots.
- Port déjà occupé ou modèle sans port libre: `addBackup` refusé, `rejected` incrémenté.

## 8. Tests

`Starship.Web.Tests`:
- `SnapshotMapper`: champs attendus sur un vaisseau neuf, appareil jamais vu marqué `stale`, valeurs non finies remplacées par 0, `canAddBackup` faux sur le routeur Basique et vrai sur l'Intermédiaire.
- `SimHost` (avec une horloge pilotable plutôt que le temps réel): `input` appliqué et borné, `burn` brûle le câble, `replace` rétablit la liaison (la vitesse recommence à augmenter), `addBackup` puis panne du câble principal conserve la poussée sur routeur Intermédiaire, `reset` change de modèle, commandes invalides ignorées et comptées, plus de clients donne un retour au neutre.
- Protocole: un message JSON mal formé ne lève pas d'exception.

Vérification de l'interface avec Playwright: la page se charge, le snapshot s'affiche, un clic sur « brûler » rend le câble rouge, la vue 3D se rend. Le rendu visuel et le confort de pilotage ne se jugent pas automatiquement et sont revus par le joueur.

## 9. Hors périmètre du jalon 2

- Multi-vaisseaux, multi-joueur, authentification, accès hors machine locale.
- Sauvegarde et chargement de partie.
- Éditeur de câblage par glisser-déposer (ici seulement brûler, remplacer, ajouter un secours).
- Gamepad.
- Nouveaux composants ou nouvelles règles de simulation.
