# OSURDF Isaac adapter

This adapter consumes a verified OSURDF Robot Bundle. It never infers joint
types, gains, actuator coverage, or an Isaac version.

Ordinary Python CI can run the cryptographic and profile gate:

```bash
python3 osurdf_isaac_adapter.py preflight --bundle /path/to/bundle --require-isaac-lab
python3 osurdf_isaac_adapter.py generate-isaaclab --bundle /path/to/bundle --output /path/to/generated
```

Run conversion only through the `python.sh`/`python.bat` belonging to the exact
Isaac Sim version pinned in `profiles/isaac.json`:

```bash
./python.sh osurdf_isaac_adapter.py convert \
  --bundle /path/to/bundle --output /path/to/isaac-asset
```

The conversion fails closed when the runtime version differs. If Isaac Lab is
enabled, the output includes `robot_cfg.py`, `actuator_groups.json`, and a
generated `smoke_test.py`. Run that script through the exact pinned Isaac Lab
environment; a source-level conversion is not reported as an RL runtime pass.

Generated output must be outside the source Bundle. A non-empty preflight or
Isaac Lab output directory, and an existing conversion directory, require an
explicit `--overwrite`; Bundle/output containment is always rejected. Preflight
and Isaac Lab generation use an isolated staging directory and replace the prior
directory atomically, so stale files are not mixed into a new result.

The maintained current API baseline is Isaac Sim 6.0.0. Isaac Lab 2.3.2 is the
stable reference fixture; 3.x beta packages remain an explicit self-hosted
compatibility gate rather than a default claim. See
[`docs/isaac/README.md`](../../docs/isaac/README.md) and the repository
compatibility matrix for the complete evidence boundary.
