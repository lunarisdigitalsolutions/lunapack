# Release a pack

Prepare a pack release that consumers can reproduce.

1. Validate the manifest against the version-1 pack schema.
2. Install the pack in an empty fixture project and inspect the rendered files.
3. Test the required parameters, each conditional file, and every managed-file
   strategy used by the pack.
4. Test an update from the prior released version and an uninstall of unchanged
   output.
5. Assign the next semantic version and record the consumer-visible changes.
6. Place the immutable release in a local source directory or a Git repository.

The MVP discovers local directories and Git repositories.
