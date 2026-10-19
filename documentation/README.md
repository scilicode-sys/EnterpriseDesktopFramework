# Documentation du projet — mode d'emploi du dossier

Ce dossier est la documentation **lisible** du socle. Chaque page est un HTML
autonome, avec une seule feuille de style partagée (`assets/doc.css`) en tout,
pensée pour l'écran **et** pour l'impression : `Ctrl + P` → « Enregistrer au
format PDF » donne un A4 propre, sans barre de navigation ni couleur perdue.

Point d'entrée : [`index.html`](index.html).

## Arborescence

```text
documentation/
├── index.html                        accueil, catégories, contrat de mise à jour
├── assets/doc.css                    charte commune à toutes les pages
├── 01-structure/architecture.html    les 5 projets, le sens des dépendances, les briques
├── 02-tutoriels/
│   ├── demarrage-d-un-poste.html     un poste neuf, de l'assistant au premier compte
│   ├── nouveau-produit.html          Framework → produit métier en 2 h
│   └── ajouter-ecran-metier.html     copier l'écran CRUD de référence
├── 03-plan/
│   ├── cahier-des-charges.html       périmètre, exigences EF/EN, définition de « terminé »
│   └── feuille-de-route.html         tours livrés, briques restantes, bloqueurs
├── 04-manuel-test/
│   ├── plan-de-qualification.html    parcours T0 → T6 joués par un exploitant
│   └── fiche-de-recette.html         registre à remplir (verdict, anomalie, clôture)
└── 05-qualite/conventions-et-pieges.html  règles permanentes + pièges mesurés
```

## Qui consulte quoi

| Profil | Pages |
| --- | --- |
| Reprendre la maintenance du socle | `01-structure`, puis `05-qualite` |
| Démarrer un produit métier | `02-tutoriels/nouveau-produit`, puis `03-plan/cahier-des-charges` |
| Installer un poste | `02-tutoriels/demarrage-d-un-poste` |
| Recetter une livraison | `04-manuel-test/plan-de-qualification` + `fiche-de-recette` |

## Qui met à jour, et quand

Le contrat est rappelé en bas de `index.html` ; il tient en trois règles.

1. **Agent, à chaque brique livrée** (commit `feat(...)`) : table des briques de
   `01-structure/architecture.html`, statuts de `03-plan/feuille-de-route.html`,
   en-tête de `index.html` (commit, date, nombre de tests) et le
   `Framework_Blueprint.md` à la racine. Une page qui n'est pas mise à jour dans
   le même commit que le code est considérée comme non livrée.
2. **Exploitant, à chaque parcours joué** : la `04-manuel-test/fiche-de-recette.html`
   (verdict, date, rôle testé). Une anomalie se signale par trois éléments :
   **numéro du parcours, écran, phrase exacte affichée** — pas de description
   reconstruite de mémoire.
3. **Agent, avant une mise en production produit** : relecture de chaque page
   contre le code, correction des écarts, puis datation de l'en-tête.

## Ordre de vérité

`code` → `Framework_Blueprint.md` → page HTML.

Le blueprint est le document de travail de l'agent ; les pages HTML en sont la
traduction lisible. En cas de divergence, la page est fautive : on corrige la
page, jamais l'inverse.

## Conventions de rédaction

- Chaque étape d'une recette porte un badge d'acteur :
  `Exploitant` · `Agent` · `Poste` · `Outil`. Un texte sans acteur est une
  ambiguïté, pas une indication.
- Les faits sont vérifiés dans le dépôt au moment de l'écriture (versions lues
  dans les `.csproj`, libellés lus dans les `.xaml`, commits lus dans `git log`),
  jamais écrits de mémoire.
- Une page ne promet pas ce que le socle ne fait pas : les briques absentes
  (écran comptes, licence, exports branchés) sont listées comme **à faire** dans
  le CDC et la feuille de route, pas comme acquises.
- La politique de secrets s'applique aussi ici : aucun mot de passe, aucune clé
  de licence, aucun contenu de `settings.json` dans une page.
