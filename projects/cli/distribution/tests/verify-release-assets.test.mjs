import assert from 'node:assert/strict'
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  rmSync,
  unlinkSync,
  writeFileSync
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import {
  expectedReleaseAssetNames,
  verifyReleaseAssets
} from '../verify-release-assets.mjs'

function createFixture() {
  const root = mkdtempSync(join(tmpdir(), 'lunapack-release-assets-'))
  const staging = join(root, 'staging')
  const distributions = join(staging, 'distributions')
  const release = join(root, 'release')
  const releaseMetadata = join(root, 'release.json')
  mkdirSync(distributions, { recursive: true })
  mkdirSync(release)

  for (const name of expectedReleaseAssetNames('1.2.3')) {
    const localPath = name.startsWith('luna-cli-')
      ? join(distributions, name)
      : join(staging, name)
    writeFileSync(localPath, `content:${name}`)
    cpSync(localPath, join(release, name))
  }

  writeFileSync(
    releaseMetadata,
    JSON.stringify({ body: 'content:CHANGELOG.md' })
  )

  return { root, staging, release, releaseMetadata }
}

test('Scenario_ExistingReleaseMatchesStagedAssets_VerificationPasses', () => {
  const fixture = createFixture()
  try {
    assert.doesNotThrow(() =>
      verifyReleaseAssets(
        fixture.staging,
        fixture.release,
        fixture.releaseMetadata,
        '1.2.3'
      )
    )
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('Scenario_ExistingReleaseIsIncompleteOrDifferent_VerificationFails', () => {
  const fixture = createFixture()
  try {
    unlinkSync(join(fixture.release, 'SHA256SUMS.txt'))
    assert.throws(
      () =>
        verifyReleaseAssets(
          fixture.staging,
          fixture.release,
          fixture.releaseMetadata,
          '1.2.3'
        ),
      /assets differ from the expected set/
    )

    writeFileSync(join(fixture.release, 'SHA256SUMS.txt'), 'changed')
    assert.throws(
      () =>
        verifyReleaseAssets(
          fixture.staging,
          fixture.release,
          fixture.releaseMetadata,
          '1.2.3'
        ),
      /asset differs: SHA256SUMS\.txt/
    )

    writeFileSync(join(fixture.release, 'unexpected.txt'), 'unexpected')
    assert.throws(
      () =>
        verifyReleaseAssets(
          fixture.staging,
          fixture.release,
          fixture.releaseMetadata,
          '1.2.3'
        ),
      /assets differ from the expected set/
    )
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('Scenario_ExistingReleaseNotesDifferByNewline_VerificationFails', () => {
  const fixture = createFixture()
  try {
    writeFileSync(
      fixture.releaseMetadata,
      JSON.stringify({ body: 'content:CHANGELOG.md\n' })
    )
    assert.throws(
      () =>
        verifyReleaseAssets(
          fixture.staging,
          fixture.release,
          fixture.releaseMetadata,
          '1.2.3'
        ),
      /Release notes differ/
    )
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})
