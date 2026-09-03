---
title: SW2URDF Documentation
description: A practical guide to exporting robot models from SolidWorks
---

# SW2URDF Documentation

SW2URDF turns a SolidWorks assembly into robot description files. In the plugin, you review the
links, joints, coordinate systems, mass properties, collision geometry, and appearance, then export
files for ROS, OpenUSD, or MuJoCo.

## Why Use This Version

The original community version established the basic SolidWorks-to-URDF workflow, but it has
practical limitations with newer SolidWorks releases, deeply nested assemblies, Chinese names,
collision checks, and modern simulation formats. This version focuses on those issues:

- Coordinate systems and axes inside deeply nested components are identified reliably, without
  guessing from duplicate or Chinese names.
- Users explicitly confirm joint types, axes, and limits, reducing incorrect detection of fixed
  assemblies or STEP models.
- Mass, center of mass, and inertia are validated, and collision geometry can be previewed before
  export.
- Appearance, collision, and inertia settings have separate pages instead of being crowded together.
- ROS 1, ROS 2, OpenUSD, and MuJoCo MJCF can be exported directly.
- The Chinese interface, error messages, and export reports are easier to understand, and page
  switching is smoother.

[See the detailed differences from the community version](/en/guide/whats-new)

## First-Time Setup

1. [Install the plugin](/en/guide/installation)
2. [Complete your first export with the quick-start guide](/en/guide/getting-started)
3. [Review each settings page](/en/features/link-tree)
4. [Choose the output you actually need](/en/exports/)

## Four Output Formats

<div class="output-list">
  <div><strong>ROS 1 Package</strong><p>For existing ROS 1 robot description projects.</p></div>
  <div><strong>ROS 2 Package</strong><p>For ROS 2 descriptions, visualization, and later control setup.</p></div>
  <div><strong>OpenUSD Robot Asset</strong><p>For Isaac Sim and other tools that support USD.</p></div>
  <div><strong>MuJoCo MJCF Model</strong><p>For building MuJoCo scenes and controls.</p></div>
</div>

## Project and Feedback

Project: <https://github.com/osrbot/solidworks_urdf_exporter_pro>

When reporting a problem, include your SolidWorks version, plugin version, reproduction steps, log,
and export report. See [Questions and Contributions](/en/support/help-and-contribute) for the
recommended format.

Contributors to the maintained fork, in project display order, include
[kitso666](https://github.com/kitso666), [W472351926](https://github.com/W472351926),
[dajianli](https://github.com/dajianli), and [sunmaxwll](https://github.com/sunmaxwll). See the repository
[contributors record](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CONTRIBUTORS.md)
and Git history for the complete record.
