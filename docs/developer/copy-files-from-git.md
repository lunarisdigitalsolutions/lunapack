# Get Files from Git Without a Pack

Get a file from a Git repository and place it in your project in one command.
The repository does not need to publish a LunaPack or define a `pack.yml`.

This example copies an agent definition from `github/awesome-copilot` into the
current project.

## Add the repository as a source

```powershell
luna sources add github awesome-copilot github/awesome-copilot --ref main
```

## Copy one file into your project

Register the file, copy it into `.github/agents`, and install it in one command:

```powershell
luna links add csharp-expert-agent --source awesome-copilot --path agents --include CSharpExpert.agent.md --target .github/agents --install
```

Luna records the source file, target, and locked Git commit in
`lunapack-lock.yml`. The upstream repository needs no `pack.yml`; Luna selects
the file directly.

## Keep it current

Check for a newer revision, then update when ready:

```powershell
luna outdated
luna update csharp-expert-agent
```

Remove the copied file while keeping its reusable definition:

```powershell
luna uninstall csharp-expert-agent
```

The [Luna Links](manage-links.md) feature powers this workflow. Its guide
covers multiple files, mapping, recovery, and full command details.
