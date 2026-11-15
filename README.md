# Pedigree8 Station

A lightweight, offline, touch-first husbandry station for jumping spiders, built for a small
Windows 10 tablet (2 GB RAM). WPF on .NET Framework 4.8 + SQLite; the whole app is ~3 MB and
uses ~80 MB of memory.

- One-tap Fed / Misted / Molted with undo, Feed all, Mist all, editable history
- Instars, premolt watch, care schedules, measurements, photos
- Enclosures with QR labels (print + camera scan), breeding, family tree
- Daily verified backups, USB backup/restore, auto-start full screen, GitHub updates

## Layout

| Folder | What |
|---|---|
| `src/Pedigree8.Core` | Data layer, rules, backups, updater (netstandard2.0) |
| `src/Pedigree8.Station` | The WPF app (net48, x86) |
| `tools/Pedigree8.Migrate` | One-way migration from the original Pedigree8 (read-only on the source) |
| `tests/Pedigree8.Core.Tests` | Automated tests |
| `packaging/` | Tablet installer and setup guide |

## Scripts

- `migrate.ps1` — copy the Pedigree8 lab into `migration-output/` with a comparison report
  (owner email is read from the untracked `local.settings.txt`)
- `build-package.ps1` — fresh migration + app + data → zip for a first install on the tablet
- `publish-release.ps1 -Notes "..."` — publish the current version for in-app updates

Station data, backups and migration output are never committed or published.
