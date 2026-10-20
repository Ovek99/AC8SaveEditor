# Third-party notices and provenance

This is an unofficial fan-made editor. ACE COMBAT, its names and game-derived metadata belong to their respective rights holders. This project is not affiliated with or endorsed by Bandai Namco or Epic Games.

The generated `editor-catalogue.json` contains game-derived item IDs, names, descriptions and associations. English text, reference schemas and special-weapon ordering were obtained from the exported metadata accompanying [RivaTesu/ac8-save-editor v1.0.2](https://github.com/RivaTesu/ac8-save-editor/releases/tag/v1.0.2), then cross-referenced against installed game data during development. The upstream source repository states an MIT license; that does not establish a separate license for the underlying game's content.

The checksum salt/formula and save-layout interpretation were corroborated against the [upstream checksum implementation](https://github.com/RivaTesu/ac8-save-editor/blob/main/src/Ac8Save/Checksum.cs) and typed campaign model. Both supplied development saves matched that checksum formula.

[retoc v0.1.5](https://github.com/trumank/retoc/releases/tag/v0.1.5) was used locally to inspect selected game containers. retoc, game archives, extracted binary assets, access keys and personal saves are not included in this repository or the distributable ZIP.

Publishing the source does not grant rights to any third-party game content. DLC and online tags describe metadata only; they do not confer entitlements or online ownership.
