---
marp: true
theme: default
paginate: true
size: 16:9
title: LunaPack: Versioned engineering foundations, safely shared across every project
description: A concise technical pitch for pack consumers and authors.
style: |
  section {
    background: #f4f7f6;
    color: #172b36;
    font-family: "Aptos", "Segoe UI", sans-serif;
    font-size: 26px;
    letter-spacing: 0;
    line-height: 1.25;
    padding: 58px 72px;
  }

  h1, h2, h3 {
    color: #103c4a;
    font-family: "Aptos Display", "Aptos", "Segoe UI", sans-serif;
    letter-spacing: 0;
    line-height: 1.05;
  }

  h1 {
    font-size: 68px;
    margin: 0 0 20px;
  }

  h2 {
    font-size: 44px;
    margin: 0 0 24px;
  }

  h3 {
    color: #e95d3c;
    font-size: 23px;
    margin: 0 0 10px;
    text-transform: uppercase;
  }

  strong {
    color: #075a60;
  }

  code {
    background: #dde9e5;
    border-radius: 3px;
    color: #075a60;
    font-family: "Cascadia Code", Consolas, monospace;
    padding: 3px 7px;
  }

  pre {
    background: #103c4a;
    border-left: 10px solid #e95d3c;
    box-sizing: border-box;
    color: #f4f7f6;
    font-family: "Cascadia Code", Consolas, monospace;
    font-size: 20px;
    line-height: 1.45;
    margin: 24px 0;
    padding: 22px 26px;
    white-space: pre-wrap;
  }

  pre code {
    background: transparent;
    color: inherit;
    padding: 0;
    white-space: inherit;
  }

  pre code span {
    color: inherit !important;
  }

  section::after {
    color: #5b7475;
    font-size: 15px;
    font-weight: 700;
  }

  section.lead {
    background: #103c4a;
    color: #f4f7f6;
    padding: 88px 88px;
  }

  section.lead h1, section.lead h2, section.close h1, section.close h2 {
    color: #f4f7f6;
  }

  section.lead h3, section.close h3 {
    color: #f4b860;
  }

  section.lead strong, section.close strong {
    color: #b8e0d2;
  }

  section.lead::before {
    background: #e95d3c;
    content: "";
    height: 13px;
    left: 88px;
    position: absolute;
    top: 62px;
    width: 94px;
  }

  section.statement {
    background: #e95d3c;
    color: #fff9f0;
  }

  section.statement h2, section.statement strong {
    color: #fff9f0;
  }

  section.close {
    background: #075a60;
    color: #f4f7f6;
  }
---

<!-- _class: lead -->

## LunaPack

## The missing lifecycle between an empty repository and a governed project

**Create once. Consume deliberately. Improve without another round of copying.**

---

## The first task is not the first feature

An empty repository needs more than application code:

- CI/CD workflow and release configuration
- Security and dependency tooling
- Build, SDK, and formatting rules
- Documentation, ADR, contribution, and repository conventions

Teams often locate a “good” existing project and copy the relevant folders.

---

## Copying solves today and creates tomorrow’s drift

The project builds. But copied content has no durable connection to its source.

When the pipeline, scanner, build policy, or documentation standard changes:

- The team does not know which upstream version it began with.
- The standard owner does not know which projects still need the change.
- A bulk replacement risks destroying deliberate local customizations.

The missing capability is not file generation. It is safe, traceable evolution.

---

## Copy, templates, and packages stop at the wrong point

**Copy-paste** creates files, then forgets their origin.

**Templates** create a project once; they do not manage its next update.

**Package managers** resolve application binaries; they do not own project files
such as pipeline YAML, `.editorconfig`, or ADR templates.

LunaPack manages the engineering content that surrounds the application.

---

<!-- _class: statement -->

## LunaPack makes project setup an explicit contract

A pack is versioned repository content plus rules for how it reaches a project:

```text
delivery-pipeline@1.2.0     .github/workflows/ci.yml
security-tools@2.1.0        security configuration
dotnet-sdk-10@1.0.0         global.json
dotnet-editorconfig@1.0.0   .editorconfig
madr-adr-template@1.0.0     docs/architecture/adr/
```

`pack.yml` declares the pack identity, dependencies, content, and copy or merge
strategy.

---

## Store packs where teams already work

### Local directory

Fast feedback while authoring or piloting a foundation in one workspace.

### Git repository or GitHub repository

Share versioned packs across teams. Consumers can select a ref and source path;
LunaPack records the resolved commit in the lock.

**Current boundary:** local and Git sources. No hosted marketplace or registry
is required to start.

---

## A project chooses its foundation

```powershell
luna init
luna sources add github acme/engineering-packs
luna install dotnet-project
luna install madr-adr-template
```

`lunapack.yml` records intent. `lunapack-lock.yml` records the resolved graph,
source provenance, targets, and content digests.

---

## The next update is a plan, not a surprise

```powershell
luna outdated
luna update dotnet-project --dry-run
luna update dotnet-project
```

The dry run resolves the next graph and shows its effects before it changes files
or lock state. The project team decides whether to apply it.

---

## Copy and merge describe real ownership

### Copy

One pack owns one target. Choose `overwrite`, `fail-if-exists`,
`skip-if-exists`, or `backup-and-overwrite`.

A conflicting unowned file stops the operation.

### Merge

Multiple packs intentionally share a target through `lines`, `section`, or
`json` merging. The result is deterministic.

Only merge strategies may share a target. Modified managed files stay for review.

---

## Safe updates are the differentiator

LunaPack records a digest for each rendered managed file.

- A conflicting unowned target stops before it is overwritten.
- A modified managed file stays for review.
- A failed install, update, or state save rolls back planned changes.
- Only explicit merge strategies may share one target.

This is the lifecycle that templates, file copies, and package managers do not
provide for repository content.

---

## Inner-source the practice your teams keep rebuilding

1. Start with one complete managed file.
2. Define its ID, semantic version, content, and strategy in `pack.yml`.
3. Validate it in a fixture project, including update and uninstall behavior.
4. Publish the immutable release to a local or Git source.

Pack authors own a contract. Consumer teams own source selection and update timing.

---

<!-- _class: close -->

## Make the next project easier than the last

### Start with one real practice, one project, and its first update

**LunaPack turns recurring project setup into engineering capability that can evolve.**
