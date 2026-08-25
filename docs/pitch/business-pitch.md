---
marp: true
theme: default
paginate: true
size: 16:9
title: LunaPack: Versioned engineering foundations, safely shared across every project
description: A concise business pitch for LunaPack.
style: |
  section {
    background: #f7f4ed;
    color: #172b36;
    font-family: "Aptos", "Segoe UI", sans-serif;
    font-size: 28px;
    letter-spacing: 0;
    line-height: 1.25;
    padding: 62px 76px;
  }

  h1, h2, h3 {
    color: #103c4a;
    font-family: "Aptos Display", "Aptos", "Segoe UI", sans-serif;
    letter-spacing: 0;
    line-height: 1.05;
  }

  h1 {
    font-size: 70px;
    margin: 0 0 22px;
  }

  h2 {
    font-size: 46px;
    margin: 0 0 28px;
  }

  h3 {
    color: #e95d3c;
    font-size: 25px;
    margin: 0 0 12px;
    text-transform: uppercase;
  }

  strong {
    color: #075a60;
  }

  section::after {
    color: #5b7475;
    font-size: 15px;
    font-weight: 700;
  }

  section.lead {
    background: #103c4a;
    color: #f7f4ed;
    padding: 92px 92px;
  }

  section.lead h1, section.lead h2, section.close h1, section.close h2 {
    color: #f7f4ed;
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
    left: 92px;
    position: absolute;
    top: 66px;
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
    color: #f7f4ed;
  }
---

<!-- _class: lead -->

## LunaPack

## Start projects with shared practice, not a blank repository

**Keep that practice connected as every project evolves.**

---

## A new project starts empty

Before a team delivers its first feature, it must assemble the engineering
foundation again:

- Documentation, ADR templates, contribution guidance, and licenses
- Security tooling, scanner configuration, and dependency policy
- CI/CD pipelines, release rules, and repository automation
- SDK selection, build rules, code conventions, and developer tooling

None of this is the product. All of it must be right for the product to ship.

---

<!-- _class: statement -->

## Today, teams copy a “good” repository

They copy workflow files, build settings, documentation folders, security
configuration, and code conventions. Then they remove what does not fit.

The first commit works. The connection to the people who maintain those
standards is gone.

---

## The hidden cost arrives with the next improvement

A pipeline must be hardened. A scanner configuration changes. A documentation
rule or build policy needs an update.

Now every project is an unknown local variation:

- Who copied the original? Who changed it?
- Which projects need the update now?
- Can anyone apply it without overwriting useful local work?

---

## LunaPack turns setup into a managed relationship

The platform team publishes reusable packs for the pieces every project needs:

- Project documentation and ADR structure
- Security, quality, and delivery configuration
- Build, dependency, formatting, and repository rules

The project selects the packs it needs. LunaPack records their exact source,
version, and managed content.

The next change becomes visible and reviewable rather than another manual copy.

---

## What changes after LunaPack

**Before:** a team searches for a comparable repository, copies the right files,
and later hand-ports every improvement.

**After:** a team starts with chosen practices, previews their next version, and
keeps intentional local changes under its own control.

Platform experts improve the shared asset once. Project teams decide when to
adopt it.

---

## It is not a package manager or template generator

**Package manager:** brings application libraries into a build.

**Template:** creates a starting point, then leaves no route back to the owner.

**Copy-paste:** creates an unmanaged project-specific fork.

**LunaPack:** manages the continuing relationship to repository content: what it
is, where it came from, who owns it, and how a project can adopt the next change.

---

## Start with one complete foundation

Turn an existing project setup into a small set of packs. Use them in two new
projects. Release one real improvement.

Success is concrete: the teams start faster, see the effect of the change, and
do not reconstruct it by hand.

---

<!-- _class: close -->

## Stop treating project setup as disposable work

### Give every project a strong start and every standard a way forward

**LunaPack makes engineering practice reusable without making local teams passive.**
