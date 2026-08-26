# Summary

## What was changed?

[Summarize the main changes in a few concise bullet points]

## Why were these changes made?

[Explain the business or technical reason for making these changes]

## ⚠️ Breaking Changes

[Include this section only when existing consumers must adapt; otherwise remove it]

**⚠️ BREAKING CHANGE:** [Explain what changed and what users must do differently]

## 📦 New Dependencies

[Include this section only when a package or other dependency was added; otherwise remove it]

**⚠️ NEW DEPENDENCY:** [Name and version; explain the purpose and confirm license compatibility]

## Key Changes

[Record implementation details without repeating the summary above. Focus on design decisions, code structure, and notable patterns.]

- [Implementation detail, such as introducing a repository boundary in `CliApplication.cs`]
- [Implementation detail, such as adding Redis-backed session caching]
- [Implementation detail, such as standardizing failures around a `Result<T>` type]

## Testing

[Describe the checks performed, including relevant unit, integration, or manual testing]

## What to Review

[Call out files, behaviors, or decisions that deserve particular review attention]

## Checklist

- [ ] The change is focused and contains no credentials, private URLs, customer
      data, personal information, or generated build output.
- [ ] Tests cover changed behavior and pass locally.
- [ ] Public documentation matches implemented behavior.
- [ ] Maintainer documentation records durable implementation decisions.
- [ ] `CHANGELOG.md` includes externally observable consumer changes only.
- [ ] Schema, pack, workflow, website, and package surfaces are updated together
      when their shared contract changes.
