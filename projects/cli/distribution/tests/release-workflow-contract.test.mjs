import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { extractReleaseNotes } from '../extract-release-notes.mjs'

const workflow = readFileSync('.github/workflows/cli.yml', 'utf8')
const buildAction = readFileSync('.github/actions/cli/build/action.yml', 'utf8')
const releaseAction = readFileSync(
  '.github/actions/cli/release/action.yml',
  'utf8'
)
const dockerfile = readFileSync('projects/cli/distribution/Dockerfile', 'utf8')

test('Scenario_ReleaseWorkflow_Targets_AllSupportedRidsAreConfigured', () => {
  for (const runtimeIdentifier of [
    'win-x64',
    'linux-x64',
    'linux-arm64',
    'osx-x64',
    'osx-arm64'
  ]) {
    assert.match(
      workflow,
      new RegExp(`'${runtimeIdentifier}' = \\[PSCustomObject\\]@\\{ runner =`)
    )
  }
})

test('Scenario_ReleaseAction_ArchiveSetContainsOnlySupportedTargets', () => {
  for (const archive of [
    'win-x64.zip',
    'linux-x64.tar.gz',
    'linux-arm64.tar.gz',
    'osx-x64.tar.gz',
    'osx-arm64.tar.gz'
  ]) {
    assert.ok(releaseAction.includes(`luna-cli-\${version}-${archive}`))
  }

  assert.match(releaseAction, /Missing CLI archive/)
  assert.match(releaseAction, /Unexpected CLI archive name/)
})

test('Scenario_ReleaseAction_NuGetPublishesRidPackagesBeforePointerPackage', () => {
  assert.match(
    buildAction,
    /dotnet pack projects\/cli\/src\/Lunapack\.Cli\/Lunapack\.Cli\.csproj[\s\S]*--no-build[\s\S]*-p:RuntimeIdentifier="\$RUNTIME_IDENTIFIER"[\s\S]*-p:LunapackPackAsTool=true/
  )
  assert.match(
    buildAction,
    /name: cli-nuget-\$\{\{ inputs\.runtime-identifier \}\}/
  )
  assert.match(releaseAction, /default: cli-nuget-\*/)
  assert.match(releaseAction, /Missing RID package/)
  assert.match(
    releaseAction,
    /dotnet pack projects\/cli\/src\/Lunapack\.Cli\/Lunapack\.Cli\.csproj[\s\S]*-p:MinVerVersionOverride="\$VERSION"[\s\S]*-p:LunapackPackAsTool=true/
  )

  const ridPackage =
    'Lunaris.Lunapack.Luna.${runtime_identifier}.${VERSION}.nupkg'
  const pointerPackage = 'Lunaris.Lunapack.Luna.${VERSION}.nupkg'
  assert.ok(
    releaseAction.indexOf(ridPackage) < releaseAction.indexOf(pointerPackage)
  )
})

test('Scenario_ReleaseWorkflow_RegistryPublishingUsesFederatedIdentity', () => {
  assert.doesNotMatch(workflow, /NPM_TOKEN|NUGET_API_KEY/)
  assert.doesNotMatch(
    releaseAction,
    /npm-token|NPM_CONFIG_USERCONFIG|_authToken/
  )
  assert.equal([...workflow.matchAll(/id-token: write/g)].length, 1)
  assert.match(
    workflow,
    /release:[\s\S]*?permissions:\s+contents: write\s+id-token: write\s+packages: write/
  )
  assert.match(releaseAction, /npm publish [^\n]+ --provenance /)
  assert.match(
    releaseAction,
    /NUGET_API_KEY: \$\{\{ steps\.login\.outputs\.NUGET_API_KEY \}\}/
  )
})

test('Scenario_ReleaseAction_StagesPortableChecksumsAndBoundedNotes', () => {
  assert.match(
    releaseAction,
    /node projects\/cli\/distribution\/extract-release-notes\.mjs/
  )
  assert.match(
    releaseAction,
    /cd release-staging\/distributions[\s\S]*printf '%s\\0' "\$\{expected_archives\[@\]\}" \| xargs -0 sha256sum/
  )
  assert.doesNotMatch(
    releaseAction,
    /printf '%s\\0' "\$\{archives\[@\]\}" \| xargs -0 sha256sum/
  )
})

test('Scenario_ReleaseAction_ChangelogContainsOnlyRequestedVersion', () => {
  const changelog = `# Changelog

## Unreleased

Pending.

## Version 1.2.3 - 2026-08-24

Requested notes.

## Version 1.2.2 - 2026-08-23

Older notes.
`

  assert.equal(
    extractReleaseNotes(changelog, '1.2.3'),
    '## Version 1.2.3 - 2026-08-24\n\nRequested notes.\n'
  )
  assert.throws(
    () => extractReleaseNotes(changelog, '9.9.9'),
    /no section for version 9\.9\.9/
  )
})

test('Scenario_ReleaseWorkflow_AcceptsOnlyOciSafeSemanticVersions', () => {
  assert.match(workflow, /-cnotmatch/)
  const workflowPattern = workflow.match(/-cnotmatch '([^']+)'/)[1]
  const releasePattern = releaseAction.match(
    /\[\[ ! "\$TAG" =~ (\^v[^\n]+) \]\]/
  )[1]
  const patterns = [new RegExp(workflowPattern), new RegExp(releasePattern)]

  for (const version of [
    'v0.0.0',
    'v1.2.3',
    'v1.2.3-alpha',
    'v1.2.3-alpha.1',
    'v1.2.3-0.3.7',
    'v1.2.3-x.7.z.92'
  ]) {
    for (const pattern of patterns) {
      assert.match(version, pattern)
    }
  }

  for (const version of [
    '1.2.3',
    'V1.2.3',
    'v01.2.3',
    'v1.02.3',
    'v1.2.03',
    'v1.2.3-01',
    'v1.2.3-alpha.01',
    'v1.2.3-',
    'v1.2.3+build.1',
    'v١.2.3'
  ]) {
    for (const pattern of patterns) {
      assert.doesNotMatch(version, pattern)
    }
  }

  assert.match(workflow, /without build metadata/)
  assert.match(releaseAction, /without build metadata/)
})

test('Scenario_ReleaseWorkflow_RunsAreBoundedAndSerialized', () => {
  assert.match(
    workflow,
    /concurrency:\s+group: cli-release-\$\{\{ github\.ref \}\}/
  )
  assert.match(workflow, /cancel-in-progress: false/)
  assert.equal([...workflow.matchAll(/timeout-minutes:/g)].length, 3)
  assert.match(buildAction, /default: '3'/)
  assert.match(
    buildAction,
    /retention-days: \$\{\{ inputs\.artifact-retention-days \}\}/
  )
})

test('Scenario_ReleaseAction_ThirdPartyActionsUseCommitPins', () => {
  const externalActions = [
    ...releaseAction.matchAll(/^\s*uses: ([^./][^@]+)@([^\s]+)$/gm)
  ]
  assert.ok(externalActions.length > 0)
  for (const [, action, reference] of externalActions) {
    assert.match(reference, /^[0-9a-f]{40}$/, `${action} must use a commit SHA`)
  }
})

test('Scenario_ReleaseAction_DryRunSkipsEveryPublishingStep', () => {
  assert.match(releaseAction, /dry-run:[\s\S]*default: 'false'/)

  for (const step of [
    'Create GitHub Release',
    'Authenticate to GitHub Container Registry',
    'Publish npm platform packages',
    'Publish npm entry package',
    'NuGet login',
    'Publish Luna .NET tool'
  ]) {
    assert.match(
      releaseAction,
      new RegExp(
        `- name: ${step}\\n(?:\\s+id: [^\\n]+\\n)?\\s+if: \\$\\{\\{ inputs\\.dry-run != 'true' \\}\\}`
      )
    )
  }
})

test('Scenario_ReleaseAction_ExistingReleaseMustMatchStagedAssets', () => {
  assert.match(releaseAction, /gh release download "\$VERSION"/)
  assert.match(
    releaseAction,
    /node projects\/cli\/distribution\/verify-release-assets\.mjs/
  )
  assert.match(releaseAction, /gh release view "\$VERSION" --json body/)
  assert.doesNotMatch(releaseAction, /expected_notes|actual_notes/)
  assert.match(releaseAction, /Verified existing GitHub Release/)
})

test('Scenario_ContainerImage_UsesPublishedBinaryAndNonRootRuntime', () => {
  assert.equal((dockerfile.match(/^FROM /gm) ?? []).length, 1)
  assert.match(
    dockerfile,
    /^FROM mcr\.microsoft\.com\/dotnet\/runtime-deps:10\.0-noble-chiseled-extra@sha256:[0-9a-f]{64}$/m
  )
  assert.match(dockerfile, /^COPY --chmod=0555 luna \/usr\/local\/bin\/luna$/m)
  assert.match(dockerfile, /^USER app$/m)
  assert.match(dockerfile, /^ENTRYPOINT \["\/usr\/local\/bin\/luna"\]$/m)
  assert.doesNotMatch(dockerfile, /^RUN /m)
})

test('Scenario_ReleaseAction_ContainerUsesExistingLinuxArtifact', () => {
  assert.match(releaseAction, /uses: docker\/setup-buildx-action@[0-9a-f]{40}/)
  assert.match(
    releaseAction,
    /packages\/native\/linux-x64\/luna[\s\S]*docker buildx build/
  )
  assert.doesNotMatch(releaseAction, /tar -xzf/)
  assert.match(
    releaseAction,
    /image="ghcr\.io\/\$\{GITHUB_REPOSITORY_OWNER,,\}\/lunapack"/
  )
  assert.match(releaseAction, /output=\(--load\)/)
  assert.match(
    releaseAction,
    /output=\(--push --provenance=mode=max --sbom=true\)/
  )
})
