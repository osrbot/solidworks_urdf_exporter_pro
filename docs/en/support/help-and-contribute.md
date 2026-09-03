# Questions and Contributions

Project: <https://github.com/osrbot/solidworks_urdf_exporter_pro>

## Project Contributors

Contributors to the current maintained fork include:

- [dajianli](https://github.com/dajianli)
- [kitso666](https://github.com/kitso666)
- [sunmaxwll](https://github.com/sunmaxwll)
- [W472351926](https://github.com/W472351926)

The list does not imply rank. See the
[contributors record](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CONTRIBUTORS.md)
and Git history for upstream and complete project attribution.

## Report a Problem

Open a new [GitHub Issue](https://github.com/osrbot/solidworks_urdf_exporter_pro/issues) and include:

1. SolidWorks release year and Service Pack.
2. Plugin version, commit, or installer filename.
3. Steps that reliably reproduce the problem.
4. Expected and actual results.
5. Complete error text, not only a screenshot of a truncated dialog.
6. `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`.
7. The `export_report`, inertia report, and mesh report from the matching output directory.
8. When permitted, a minimal assembly or a sample with sensitive information removed.

For coordinate, inertia, or motion-direction problems, also describe the reference coordinate
system, units, and the expected correct result.

## Suggest a Feature

Describe the real workflow, the step that is currently blocked, and the expected output. Focus on
the problem to solve instead of providing only a vague feature name. If the request depends on a
specific ROS, Isaac Sim, MuJoCo, or SolidWorks release, state the version and validation environment.

## Contribute Code

1. Fork the project and create a feature branch from the current maintained branch.
2. Keep the change focused and avoid rewriting unrelated modules.
3. Add tests for conversion rules, validation, and error paths.
4. For UI changes, check Chinese and English text, common DPI settings, and window sizes.
5. Update the relevant user documentation and Changelog.
6. Open a pull request that explains the problem, implementation, test results, and anything not yet
   verified.

Build and test commands are documented in the repository's
[contribution guide](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/docs/wiki/Contributing.md).

## Contribute Documentation

Documentation should first answer "What should the user do?" and "What will they get?" Avoid
internal class names, temporary data formats, or unimplemented plans in user-facing pages.
Screenshots must not include personal paths, email addresses, tokens, or unrelated windows.
