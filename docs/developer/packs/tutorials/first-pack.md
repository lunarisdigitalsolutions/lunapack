# Create a first pack

Create a small pack, make it available from a source, and install it in a
throwaway project.

1. Create a pack directory with `pack.yml` and the content it will manage.
2. Give the pack a stable ID and a semantic version.
3. Add one managed-file entry with a project-relative target.
4. Initialize a test project and add the directory or Git repository as a
   source.
5. Discover the pack, install it with a dry run, then install it.
6. Check the resulting file and the lock record. Uninstall only after checking
   that the target is unchanged.

Start with one complete file. Add templates, parameters, composition, or merge
behavior only when the simple lifecycle is understood.
