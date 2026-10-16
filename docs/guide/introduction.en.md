# Introduction

[English](/guide/introduction.en) | [简体中文](/guide/introduction)

**Doggo** is an open-source gaming info platform focused on **CS2 (Counter-Strike 2)** and **Dota 2**, providing news aggregation, skin market data, case-opening simulation, and Steam bot capabilities.

The project originates from a platform already running in production. The core capabilities that can be published openly were extracted from it and rewritten in **C# / .NET** as this open-source version, running on Linux and delivered with Docker.

## Design points

- **Monorepo**: news scraping, market sync, case-opening gameplay, the Steam bot, and the admin console live in one repository, split by module.
- **Reuse vapor**: Steam-side data and automation (market, inventory, trading) are based on [vapor](https://github.com/cuihairu/vapor); vapor and this project are both open-source projects maintained by the same author, while the CS2 / Dota 2 external news scrapers are built by this project.
- **Linux first**: every component treats the Linux container as a first-class citizen, orchestrated with Docker Compose.
- **Docs first**: architecture, data model, and gameplay logic are documented before being implemented.

## Core capabilities

| Capability | Description |
| --- | --- |
| News aggregation | Multi-source scraping and aggregated display of official CS2 / Dota 2 updates, patch notes, and esports news |
| Skin market data | Case and skin catalog, Steam market price tracking and trend display |
| Case-opening simulation | Case-opening gameplay using the official drop probabilities and wear ranges, including inventory and record management |
| Steam bot | Steam-side automation based on vapor: market sync, inventory, trading |
| Admin console | Operations interface for users, content, skins, orders, and statistics |

## Project status

All M0-M12 milestones are delivered (469 server tests green; web/admin/miniapp/mobile builds pass). The remaining open items are concentrated in external dependencies such as the vapor integration and the miniapp review; the list is in TODO.md at the repository root. Contributions and discussion are welcome through Issues and PRs.

## License

This project is released under [Apache License 2.0](https://github.com/cuihairu/doggo/blob/main/LICENSE).
