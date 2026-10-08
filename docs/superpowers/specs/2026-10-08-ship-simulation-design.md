# Simulation de vaisseau spatial: conception du jalon 1

Date: 2026-10-08
Statut: en attente de relecture

## 1. Objectif

Jeu de simulation de pilotage et de gestion d'un vaisseau spatial (esprit Faucon Millenium, Wing Commander, vaisseaux de fantasy), avec une simulation volontairement poussée. Le jalon 1 construit le cœur de simulation, sans rendu graphique.

Chaîne simulée: cockpit (manette de puissance, joystick) → câbles → ordinateur de bord (avec routeur) → câbles → propulsion, alimentée par un groupe électrique. La télémétrie remonte des composants vers l'ordinateur par les mêmes câbles.

Critères de succès du jalon 1:
- Un vaisseau complet tourne en console: le joueur actionne throttle et joystick, le vaisseau accélère et tourne selon une physique newtonienne 3D.
- La télémétrie de chaque composant est visible en temps réel, avec indication d'obsolescence.
- Le câblage est modifiable à chaud (connecter, déconnecter, remplacer un câble).
- Un câble brûlé coupe la liaison, et un routeur capable de failover bascule sur une route de secours.
- Simulation déterministe à seed fixe, couverte par des tests automatisés.

## 2. Décisions

| Sujet | Décision |
|---|---|
| Techno | C# (.NET), bibliothèque de simulation pure, portable vers Unity/Godot plus tard |
| Rendu | Aucun au jalon 1. Interface console |
| Temps | Tick fixe 60 Hz, RNG avec seed |
| Physique | Newtonienne 3D (masse, inertie, rotation, sans amortissement magique) |
| Communication | Réseau à paquets adressés, topologie en graphe modifiable à l'exécution |
| Routage | Capacité du modèle de routeur (gamme), pas un choix global |
| Hors périmètre | Voir section 9 |

## 3. Architecture

Solution .NET:
- `Starship.Sim`: bibliothèque, aucune dépendance de rendu ni d'E/S.
- `Starship.Sim.Tests`: tests automatisés.
- `Starship.Cli`: application console (entrées clavier, affichage télémétrie).

Concepts principaux de `Starship.Sim`:
- `SimClock`: tick fixe, fournit dt et le RNG seedé.
- `Ship`: possède le graphe d'appareils et de câbles, avance la simulation d'un tick.
- `ShipBody`: état physique (position, orientation, vitesses, masse, inertie).
- `Device` (base): `Address`, une ou plusieurs `NetInterface`, implémente `ITelemetrySource`.
- Appareils: `ThrottleLever`, `Joystick`, `BoardComputer`, `PowerPlant`, `PropulsionSystem`.
- `Router`: module de l'ordinateur de bord, défini par un `RouterModel`.
- `Cable`: arête entre deux `NetInterface`.
- `Packet`: unité de communication.

## 4. Réseau

### 4.1 Adressage et paquets
- Chaque appareil a une `Address`. Chaque `NetInterface` appartient à un appareil.
- `Packet { Src, Dst, Kind, Topic, Payload, Seq, Checksum }`.
- `Kind`: `Control`, `Telemetry`, `Heartbeat`. `Topic` précise un paquet `Control`: `Throttle`, `Stick`, `Setpoint`.
- Commandes (throttle, joystick) émises à cadence fixe. Télémétrie et heartbeat émis par chaque composant à sa propre cadence.

### 4.2 Câbles
Propriétés: `Quality`, `Length`, `Health`, `State` (OK / dégradé / brûlé).
Effets sur chaque paquet en transit: latence en ticks, probabilité de perte, probabilité de corruption (checksum invalide), fonctions de `Length`, `Quality` et `Health`. Un câble `Burnt` ne transmet plus rien.
Même abstraction `Cable` prévue pour l'énergie (type `Power`) aux jalons suivants.

### 4.3 Topologie éditable
`Ship.Connect(ifaceA, ifaceB, cable)`, `Disconnect(...)`, `Replace(cable)`, utilisables pendant la simulation. Une nouvelle route est testée par heartbeat avant d'être marquée `Up`.

### 4.4 Routeur et gammes
`RouterModel` (données) décrit:
- `Ports`: type (`Dedicated(kind)` ou `Generic`), débit max, latence.
- `Capabilities`: `ConfigurableAddressing`, `RoutingTable`, `Failover`, `MaxRoutesPerDst`.
- `Throughput`: paquets traités par tick. Au-delà, file d'attente puis perte si débordement.

| Gamme | Ports | Configurabilité |
|---|---|---|
| Basique | ~4 dédiés, non interchangeables | Aucune. Adresses fixes, routes en dur, pas de failover |
| Intermédiaire | ~8, mi-dédiés mi-génériques | Table de routes simple, une route de secours par destination |
| Avancé | 16+, tous génériques | Adressage libre par port, table complète, failover auto, plusieurs routes par destination |

Un seul moteur de routage pour toutes les gammes: il lit les `Capabilities`. Ajouter une gamme = ajouter des données. Un port dédié refuse un appareil d'un autre type.

Liaison de port: chaque port connecté est lié (`BoundAddress`) à l'adresse de l'appareil à l'autre bout du câble. Le routeur envoie vers `Dst` par les ports liés à `Dst` (plusieurs ports liés = routes redondantes, classées par index). `RoutingTable` permet de fixer des routes explicites (`SetRoute`) prioritaires sur ces routes automatiques. `ConfigurableAddressing` permet d'assigner l'adresse d'un appareil via son port (`AssignAddress`). Un port est considéré `Up` s'il a reçu un paquet valide de l'adresse liée depuis moins de 30 ticks.

## 5. Flux d'un tick

1. **Émission**: appareils produisent commandes, télémétrie, heartbeats.
2. **Transit des câbles**: perte, corruption, latence appliquées. Paquets arrivés déposés dans la file d'entrée de l'interface destination.
3. **Routeur**: rejette les paquets au checksum invalide, consulte la table, choisit une route saine, réémet, dans la limite du `Throughput`.
4. **Ordinateur de bord**: consomme ses paquets. Commandes: dernière valeur valide (par `Seq`). Télémétrie: met à jour le tableau d'état par appareil. Calcule les consignes et les émet vers la propulsion.
5. **Énergie**: `PowerPlant` fournit la puissance disponible, les consommateurs en demandent. En cas de manque, répartition proportionnelle (brownout), rendement dégradé.
6. **Propulsion**: applique la dernière consigne reçue, bornée par l'énergie disponible, produit poussée et couple.
7. **Physique**: `ShipBody` intègre force et couple.
8. **Surveillance**: santé et chauffe des câbles, état des routes (heartbeats).

Étapes 1 à 4 entièrement en paquets. Seules l'énergie et la physique sont continues. Tout est déterministe à seed fixe.

## 6. Télémétrie

Chaque composant implémente `ITelemetrySource.Report()` (puissance, température, charge, santé). Le rapport voyage en paquet `Telemetry` vers l'ordinateur, par les câbles et le routeur. L'ordinateur garde pour chaque appareil la dernière valeur reçue avec son âge. Une donnée trop ancienne est marquée obsolète à l'affichage.

## 7. Pannes et dégradations

- **Câble brûlé**: `Health` sous un seuil (surcharge, chaleur, dégât) → `Burnt`. Détecté uniquement par l'absence de heartbeat.
- **Failover**: après N heartbeats manqués, la route est `Down`. Si le modèle a `Failover`, bascule sur la route de secours, sinon l'appareil est injoignable.
- **Perte de commande**: sans commande valide depuis X ticks, la propulsion maintient la dernière consigne un court délai, puis coupe.
- **Télémétrie perdue**: dernière valeur conservée, marquée obsolète.
- **Panne d'énergie**: composants sous-alimentés ralentissent leur cadence d'émission, puis s'éteignent sous un seuil. Un routeur éteint coupe tout le trafic qui y passe.

## 8. Tests

- Déterminisme: deux runs à seed identique donnent un état identique.
- Un test par cause de panne: câble brûlé, corruption, file pleine, failover, brownout.
- Propriétés du bruit en fonction de `Length` et `Quality`.
- Physique: poussée constante donne l'accélération attendue, couple donne la rotation attendue.
- Gammes de routeur: un port dédié refuse le mauvais type, un modèle sans `Failover` ne bascule pas.

## 9. Hors périmètre du jalon 1

- Énergie détaillée par câble de puissance (section, chauffe de câble d'alimentation).
- Découverte dynamique de topologie (candidat pour un routeur haut de gamme).
- Composants dupliqués avec vote (deux propulseurs ou groupes redondants).
- Interface 3D, économie (coût, masse, énergie des routeurs), armes, navigation.
