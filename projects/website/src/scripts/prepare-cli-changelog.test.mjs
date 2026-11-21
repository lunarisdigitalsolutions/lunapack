import assert from 'node:assert/strict'
import test from 'node:test'

import {
  generateChangelogDocument,
  prepareChangelog,
  unreleasedPlaceholder
} from './prepare-cli-changelog.mjs'

test('removes a placeholder-only Unreleased section', () => {
  const markdown = `# Changelog

## Unreleased

${unreleasedPlaceholder}

## Version 1.0.0 - 2026-01-01

### Added

- Added a feature.
`

  const prepared = prepareChangelog(markdown)

  assert.doesNotMatch(prepared, /## Unreleased/)
  assert.doesNotMatch(prepared, /Update this section/)
  assert.match(prepared, /## Version 1\.0\.0/)
})

test('preserves real Unreleased entries and removes the maintainer placeholder', () => {
  const markdown = `# Changelog

## Unreleased

${unreleasedPlaceholder}

### Added

- Added an upcoming feature.

## Version 1.0.0 - 2026-01-01
`

  const prepared = prepareChangelog(markdown)

  assert.match(prepared, /## Unreleased/)
  assert.doesNotMatch(prepared, /Update this section/)
  assert.match(prepared, /Added an upcoming feature/)
})

test('preserves released sections without an Unreleased section', () => {
  const markdown = `# Changelog

## Version 1.0.0 - 2026-01-01

### Fixed

- Fixed a bug.
`

  assert.equal(prepareChangelog(markdown), markdown)
})

test('adds Docusaurus presentation metadata', () => {
  const document = generateChangelogDocument('# Changelog\n')

  assert.match(document, /^---\ntitle: CLI Changelog\n/)
  assert.match(document, /description: Luna CLI release history/)
  assert.match(document, /# Changelog/)
})
