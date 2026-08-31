<p align="center">
  <img src="icon.png" width="96" alt="DevGuard" />
</p>

<h1 align="center">DevGuard — NoBOMSuite</h1>

<p align="center">
  <b>Finds the characters your eyes cannot see.</b><br/>
  Byte-order marks and invisible Unicode, flagged where they actually are.
</p>

---

A file looks fine. The diff looks fine. The build fails, or a string comparison
silently returns `false`. The cause is a character with no width: a BOM, a
zero-width space, a soft hyphen.

DevGuard marks them in the editor, at the exact position, as you open and save.

## What it does

| | |
|---|---|
| **BOM detection** | UTF-8 byte-order marks. `.sln` files are exempt — Visual Studio writes them deliberately, so flagging one is a false alarm. |
| **Invisible characters** | Zero-Width Space, Zero-Width Non-Joiner, Zero-Width Joiner, Word Joiner, Soft Hyphen — each reported with its name and cursor position. |
| **Scan on open and save** | Diagnostics appear in the Problems panel like any linter's. Save-scanning can be turned off. |
| **Workspace scan** | One command sweeps the whole workspace. |
| **One-click BOM removal** | Right-click a file, remove the BOM, byte counts reported before and after. |

The scanning core is native C, reached through a C ABI — the same engine the
command-line tool uses, not a re-implementation.

## Commands

| Command | What it does |
|---|---|
| `DevGuard: Çalışma alanını tara` | Scan the entire workspace |
| `DevGuard: Bu dosyadaki BOM'u kaldır` | Strip the BOM from one file |

> **Note on language:** the command titles and messages are currently Turkish.
> Everything else — diagnostics, settings, behaviour — is language-neutral.
> English localisation is planned.

## Settings

| Setting | Default | |
|---|---|---|
| `devguard.etkin` | `true` | Enable diagnostics |
| `devguard.kaydettesTara` | `true` | Scan on save |
| `devguard.taramaDeseni` | source files | Glob for workspace scan |
| `devguard.haricDesen` | build/vendor dirs | Glob to exclude |

### Why the default scope is narrow

An early wide-scope run in a data-heavy repository scanned 22,665 files and
returned **364,000+ matches** across 384 text corpora (arXiv papers, Gutenberg
books). Every match was *correct* and every one was *noise*: ZWNJ and ZWJ are
legitimate orthographic characters in Persian, Arabic and the Indic scripts.

DevGuard is a **code** hygiene tool. It ships pointed at source files, and it
excludes `data/` and `datasets/` by default. Widen it deliberately, not by
accident.

## Platform

This release targets **Linux x64**. The scanning core is a native library, so
Windows and macOS need their own builds; they are not published yet rather than
published untested.

## Requirements

None. The native core is bundled.

## Known limitations

- Command titles are Turkish (see above)
- Linux x64 only
- The desktop application in this repository (Avalonia) is **not** part of the
  extension and is not finished — see [`README.desktop.md`](README.desktop.md)

## License

MIT — [source on GitHub](https://github.com/Bymuratcoskun/NoBOMSuite)
