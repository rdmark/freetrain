# Retired desktop infrastructure

These files were retired on 17 October 2026 after validation of the web vertical
slice. Build the maintained application with `FreeTrain.slnx`; see
[README.web.md](../../README.web.md).

This archive preserves original file contents and repository-relative paths from
revision `18986fb`. The historical source baseline is `1f755ca`. Original notices
remain attached to the archived files; see also the original
[project notices](../../readme.txt) and [license](../../COPYING.txt).

The archive contains:

- Visual Studio desktop solutions, Windows resource-copy/exclusion scripts,
  release manifests and the ColorDiff Ant packaging file.
- Native desktop binaries, external UI/COM/compression dependencies and
  DirectDraw surface-recovery documentation.
- Desktop launchers and the GUID clipboard utility.
- DirectShow type libraries, recording filter/support files and recording UI.
- The desktop debug window.

IDE user-state files and CVS ignore metadata were removed instead of archived.

This is a historical source record, not a supported desktop build. Relative
references to gameplay source, content and other libraries may no longer resolve:
those files remain outside the attic for future ports. Consult the original
revision for the complete desktop layout. The application build, content importer
and deployment do not consume this archive; the Docker context excludes it.

Keep `core/`, gameplay/content plugins, NeoFT, algorithm experiments, graphics
effects and retained content utilities available as porting references. Their
absence from the modern build does not imply that their behavior is implemented.
