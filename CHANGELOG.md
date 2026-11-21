# Changelog

## 1.0

First release as SquirrelStash (renamed from StashieV2; old history is
archived in `docs/stashiev2-history.bundle`).

- Settings window and editors rebuilt on house UI v2, with searchable
  stash-tab pickers and opaque themed popups.
- Filter test workbench opens at full size; hover overlay matches the
  house theme.
- Currency refill editor uses a full-size clickable cell grid; ignored
  cells use the same grid.
- Old `config/Stashie` migrates to the new config folder on first run.
- JIT optimization enabled; settings-window allocations and per-frame
  visibility checks cut down from profiled hot spots.
