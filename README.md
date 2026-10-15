# Ambysto Kit

Shared foundation of the **Ambysto Pro Suite**, a set of native utilities that **measure first, change safely and are always reversible**:

| App | What it does | Repo |
|---|---|---|
| Ambysto Optimer | System resources monitor + safe cleanup | `ambysto/ambysto-optimer` |
| Ambysto Steady | Stable Internet connection (monitor, diagnose, recover) | separate |
| Ambysto Lean (was Space) | Disk map, large/old files, duplicates, uninstall (planned) | `ambysto/ambysto-space` |

This repo holds:

- **The Kit:** a Swift package (a .NET library for Windows follows later) with license verification, brand tokens, the floating mini bar framework, suite discovery and the merged mini bar.
- **Suite-level docs:** strategy intents (`intent/`), the platform-neutral license spec, customer policies and market research.

Platform order: **every utility ships on macOS first, then Windows.**
