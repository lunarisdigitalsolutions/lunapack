# Install multiple packs safely

Install several requested roots in one command when their exact dependency
graphs and managed targets are compatible.

## Preview roots in order

List roots in the order Luna should process them:

```powershell
luna install license-mit@1.0.0 dotnet-editorconfig@1.0.0 `
  --dry-run `
  --scripts skip
```

Each root resolves its complete dependency graph before its files are applied.
Shared transient packs at the same version are reused. Luna reports a conflict
when another requested root requires a different version or when managed
targets cannot coexist.

## Understand partial success

A multi-root invocation processes requested roots sequentially. If a later root
fails, earlier successful roots remain installed; the complete command is not
one atomic transaction. A root already installed at the requested version is
warned about and skipped so processing can continue.

Prefer separate commands when each root needs different parameters,
remappings, script policy, or external-source approval. Shared command options
otherwise apply to each requested root.

## Apply and verify

Remove `--dry-run` after reviewing every root and dependency:

```powershell
luna install license-mit@1.0.0 dotnet-editorconfig@1.0.0 `
  --scripts skip
luna audit
```

If a later root fails, run `luna audit` to identify completed roots. Correct the
conflict and retry only the missing root, or explicitly uninstall earlier roots
that should not remain.
