import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import {
  packageName,
  stagePackages,
  targetForPlatform,
  targets,
  validateArchiveEntries
} from '../package-distribution.mjs'
import { resolveBinary } from '../run-luna.mjs'

test('Scenario_TargetMappings_ExpectedPackages', () => {
  assert.deepEqual(targets.map(packageName), [
    '@lunarisdigitalsolutions/lunapack-win64',
    '@lunarisdigitalsolutions/lunapack-linux-x64',
    '@lunarisdigitalsolutions/lunapack-linux-arm64',
    '@lunarisdigitalsolutions/lunapack-macos-x64',
    '@lunarisdigitalsolutions/lunapack-macos-arm64'
  ])
})

test('Scenario_SupportedPlatform_MatchingTargetReturned', () => {
  assert.deepEqual(targetForPlatform('linux', 'x64'), targets[1])
})

test('Scenario_UnsupportedPlatform_LauncherReportsTargets', () => {
  assert.throws(
    () => resolveBinary('freebsd', 'x64'),
    /Unsupported Luna platform freebsd-x64/
  )
})

test('Scenario_MissingOptionalPackage_LauncherReportsReinstallGuidance', () => {
  assert.throws(
    () =>
      resolveBinary('linux', 'x64', () => {
        throw new Error('not found')
      }),
    /optional dependencies enabled/
  )
})

test('Scenario_UnsafeArchiveEntries_StagingIsRejected', () => {
  for (const entry of [
    { name: '../luna', type: 'file' },
    { name: '/tmp/luna', type: 'file' },
    { name: 'C:/temp/luna.exe', type: 'file' },
    { name: 'payload/luna', type: 'link' }
  ]) {
    assert.throws(
      () => validateArchiveEntries([entry], 'luna', 'unsafe.tar.gz'),
      /unsafe path|link or special entry/
    )
  }
})

test('Scenario_DuplicateArchiveEntries_StagingIsRejected', () => {
  assert.throws(
    () =>
      validateArchiveEntries(
        [
          { name: 'luna', type: 'file' },
          { name: 'LUNA', type: 'file' }
        ],
        'luna',
        'duplicate.tar.gz'
      ),
    /duplicate paths/
  )
})

test('Scenario_ValidArchives_StagesConstrainedNpmPackages', () => {
  const root = mkdtempSync(join(tmpdir(), 'luna-package-test-'))
  const archives = join(root, 'archives')
  mkdirSync(archives)

  for (const target of targets) {
    const payload = join(root, target.rid)
    mkdirSync(payload)
    writeFileSync(join(payload, target.binary), target.rid)
    const archive = join(
      archives,
      `luna-cli-1.2.3-${target.rid}.${target.archive}`
    )
    if (target.archive === 'zip') {
      execFileSync('powershell.exe', [
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        `Compress-Archive -LiteralPath '${join(payload, target.binary)}' -DestinationPath '${archive}'`
      ])
    } else {
      execFileSync('tar', ['-C', payload, '-czf', archive, target.binary])
    }
  }

  const staged = stagePackages({
    archiveDirectory: archives,
    outputDirectory: join(root, 'staging'),
    version: '1.2.3'
  })
  assert.deepEqual(staged.packages, [
    'lunapack',
    'lunapack-linux-arm64',
    'lunapack-linux-x64',
    'lunapack-macos-arm64',
    'lunapack-macos-x64',
    'lunapack-win64'
  ])

  const platformPackage = JSON.parse(
    readFileSync(join(staged.packageRoot, 'lunapack-linux-x64', 'package.json'))
  )
  assert.deepEqual(platformPackage.os, ['linux'])
  assert.deepEqual(platformPackage.cpu, ['x64'])

  const entryPackage = JSON.parse(
    readFileSync(join(staged.packageRoot, 'lunapack', 'package.json'))
  )
  assert.equal(
    entryPackage.optionalDependencies[
      '@lunarisdigitalsolutions/lunapack-win64'
    ],
    '1.2.3'
  )
})
