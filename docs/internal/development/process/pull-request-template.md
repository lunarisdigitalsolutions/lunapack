# Pull Request Description Guide

Use this format when drafting or revising a pull request description with AI assistance.

Keep the headings in the order shown. Remove optional sections that do not apply.

Every pull request must reference its related GitHub issue, for example `#123`.
When the issue number is not available, add a TODO asking the author to replace it with the correct `#123` reference.

## Template

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
