# Copy Files from Git

Copy a file from a remote Git repository even when that repository does not
publish a LunaPack manifest. Create a Luna Link that records the source file,
its target, and the locked revision Luna installed.

For example, copy an agent definition from `github/awesome-copilot` into the
current project.

## Add the repository as a source

```powershell
luna sources add github awesome-copilot github/awesome-copilot
```

## Copy and manage one file

Create the link, copy the file into `.github/agents`, and install it in one
command:

```powershell
luna links add csharp-expert-agent --source awesome-copilot --path agents --include CSharpExpert.agent.md --target .github/agents --install
```

Luna records the copied file and its Git commit in `lunapack-lock.yml`. The
remote repository needs no `pack.yml` because the link selects the file
directly.

Check for a newer revision and update when ready:

```powershell
luna outdated
luna update csharp-expert-agent
```

Remove the copied file while keeping the reusable link definition:

```powershell
luna uninstall csharp-expert-agent
```

Use [Manage Luna Links](manage-links.md) when you need multiple includes,
mapping, recovery, or full command reference details.
