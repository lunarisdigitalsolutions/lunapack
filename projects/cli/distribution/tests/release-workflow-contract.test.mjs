import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import test from 'node:test'
import {
  extractReleaseNotes,
  extractUnreleasedNotes
} from '../extract-release-notes.mjs'

const workflow = readFileSync('.github/workflows/cli.yml', 'utf8')
const websiteWorkflow = readFileSync('.github/workflows/website.yml', 'utf8')
const planJob = workflow.slice(
  workflow.indexOf('  plan:'),
  workflow.indexOf('  build:')
)
const buildJob = workflow.slice(
  workflow.indexOf('  build:'),
  workflow.indexOf('  sanity-check:')
)
const sanityCheckJob = workflow.slice(
  workflow.indexOf('  sanity-check:'),
  workflow.indexOf('  release:')
)
const releaseJob = workflow.slice(workflow.indexOf('  release:'))
const buildAction = readFileSync('.github/actions/cli/build/action.yml', 'utf8')
const dotnetBuildAction = readFileSync(
  '.github/actions/build-dotnet/action.yml',
  'utf8'
)
const releaseSelectorAction = readFileSync(
  '.github/actions/cli/release/action.yml',
  'utf8'
)
const releaseChannelsAction = readFileSync(
  '.github/actions/cli/release/channels/action.yml',
  'utf8'
)
const releasePrepareAction = readFileSync(
  '.github/actions/cli/release/prepare/action.yml',
  'utf8'
)
const releaseGitHubAction = readFileSync(
  '.github/actions/cli/release/github/action.yml',
  'utf8'
)
const releaseContainerAction = readFileSync(
  '.github/actions/cli/release/container/action.yml',
  'utf8'
)
const releaseNpmAction = readFileSync(
  '.github/actions/cli/release/npm/action.yml',
  'utf8'
)
const releaseNuGetAction = readFileSync(
  '.github/actions/cli/release/nuget/action.yml',
  'utf8'
)
const releaseActions = [
  releaseSelectorAction,
  releaseChannelsAction,
  releasePrepareAction,
  releaseGitHubAction,
  releaseContainerAction,
  releaseNpmAction,
  releaseNuGetAction
]
const dockerfile = readFileSync('projects/cli/distribution/Dockerfile', 'utf8')
const pullRequestGateWorkflow = readFileSync(
  '.github/workflows/pull-request-check-gate.yml',
  'utf8'
)
const pullRequestGateScript = readFileSync(
  '.github/scripts/validate-external-checks.js',
  'utf8'
)
const pullRequestGatePackWorkflow = readFileSync(
  'projects/packs/platforms/github/actions/github-pull-request-gate-workflow/1.0.0/targets/.github/workflows/pull-request-check-gate.yml',
  'utf8'
)
const pullRequestGatePackScript = readFileSync(
  'projects/packs/platforms/github/actions/github-pull-request-gate-workflow/1.0.0/targets/.github/scripts/validate-external-checks.js',
  'utf8'
)
const require = createRequire(import.meta.url)
const {
  collectCheckRunProblems
} = require('../../../../.github/scripts/validate-external-checks.js')
const {
  collectCheckRunProblems: collectPackCheckRunProblems
} = require('../../../packs/platforms/github/actions/github-pull-request-gate-workflow/1.0.0/targets/.github/scripts/validate-external-checks.js')

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
    assert.ok(releasePrepareAction.includes(`luna-cli-\${version}-${archive}`))
  }

  assert.match(releasePrepareAction, /Missing CLI archive/)
  assert.match(releasePrepareAction, /Unexpected CLI archive name/)
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
  assert.match(releaseNuGetAction, /default: cli-nuget-\*/)
  assert.match(
    releaseNuGetAction,
    /- name: Download RID-specific \.NET tools[\s\S]*path: release-staging\/nuget/
  )
  assert.match(releaseNuGetAction, /Missing RID package/)
  assert.doesNotMatch(
    releasePrepareAction,
    /cli-nuget-|release-staging\/nuget|Missing RID package/
  )
  assert.doesNotMatch(
    releaseGitHubAction,
    /cli-nuget-|release-staging\/nuget|Missing RID package/
  )
  assert.match(
    releaseNuGetAction,
    /pack_arguments=\([\s\S]*-p:MinVerVersionOverride="\$version"[\s\S]*-p:LunapackPackAsTool=true[\s\S]*dotnet pack projects\/cli\/src\/Lunapack\.Cli\/Lunapack\.Cli\.csproj "\$\{pack_arguments\[@\]\}"/
  )

  const ridPackage =
    'Lunaris.Lunapack.Luna.${runtime_identifier}.${VERSION}.nupkg'
  const pointerPackage = 'Lunaris.Lunapack.Luna.${VERSION}.nupkg'
  assert.ok(
    releaseNuGetAction.indexOf(ridPackage) <
      releaseNuGetAction.indexOf(pointerPackage)
  )
})

test('Scenario_ReleaseWorkflow_RegistryPublishingUsesFederatedIdentity', () => {
  assert.doesNotMatch(workflow, /secrets\.(?:NPM_TOKEN|NUGET_API_KEY)/)
  assert.doesNotMatch(workflow, /vars\.NUGET_USER/)
  assert.equal(
    [...workflow.matchAll(/nuget-user: \$\{\{ secrets\.NUGET_USER \}\}/g)]
      .length,
    1
  )
  assert.doesNotMatch(
    releaseNpmAction,
    /npm-token|NPM_CONFIG_USERCONFIG|_authToken/
  )
  assert.equal([...workflow.matchAll(/id-token: write/g)].length, 1)
  assert.match(
    workflow,
    /release:[\s\S]*?permissions:\s+actions: read\s+contents: write\s+id-token: write\s+packages: write/
  )
  assert.match(releaseNpmAction, /npm publish [^\n]+ --provenance /)
  assert.match(
    releaseNpmAction,
    /if \[\[ "\$version" == \*-\* \]\]; then[\s\S]*tag=next/
  )
  assert.match(
    releaseContainerAction,
    /if \[\[ "\$version" == \*-\* \]\]; then[\s\S]*channel=next/
  )
  assert.match(
    releaseNuGetAction,
    /NUGET_API_KEY: \$\{\{ steps\.login\.outputs\.NUGET_API_KEY \}\}/
  )
})

test('Scenario_ReleaseAction_StagesPortableChecksumsAndBoundedNotes', () => {
  assert.match(
    releasePrepareAction,
    /node projects\/cli\/distribution\/extract-release-notes\.mjs/
  )
  assert.match(
    releasePrepareAction,
    /cd release-staging\/distributions[\s\S]*printf '%s\\0' "\$\{expected_archives\[@\]\}" \| xargs -0 sha256sum/
  )
  assert.doesNotMatch(
    releasePrepareAction,
    /printf '%s\\0' "\$\{archives\[@\]\}" \| xargs -0 sha256sum/
  )
})

test('Scenario_StableRelease_ArtifactRunMustMatchReleaseTagCommit', () => {
  assert.match(releasePrepareAction, /git rev-parse "\$TAG\^\{commit\}"/)
  assert.match(releasePrepareAction, /actions\.getWorkflowRun/)
  assert.match(releasePrepareAction, /run\.path/)
  assert.match(releasePrepareAction, /\.github\/workflows\/cli\.yml/)
  assert.match(releasePrepareAction, /run\.head_sha/)
  assert.match(releasePrepareAction, /EXPECTED_SHA/)
  assert.match(releasePrepareAction, /run\.conclusion/)
})

test('Scenario_PullRequestGate_UsesTrustedScriptAndExcludesSkippedChecks', () => {
  assert.match(
    pullRequestGateWorkflow,
    /ref: \$\{\{ github\.event\.pull_request\.base\.sha \|\| github\.event\.repository\.default_branch \}\}/
  )
  assert.match(
    pullRequestGatePackWorkflow,
    /ref: \$\{\{ github\.event\.pull_request\.base\.sha \|\| github\.event\.repository\.default_branch \}\}/
  )
  assert.match(
    pullRequestGateScript,
    /module\.exports = \{ collectCheckRunProblems, validateExternalChecks \}/
  )
  assert.match(
    pullRequestGatePackScript,
    /module\.exports = \{ collectCheckRunProblems, validateExternalChecks \}/
  )

  for (const collectProblems of [
    collectCheckRunProblems,
    collectPackCheckRunProblems
  ]) {
    const summary = collectProblems(
      [
        {
          name: 'Security tests',
          status: 'completed',
          conclusion: 'skipped',
          started_at: '2026-08-31T00:00:00Z'
        },
        {
          name: 'Build',
          status: 'completed',
          conclusion: 'cancelled',
          started_at: '2026-08-31T00:00:00Z'
        }
      ],
      'Validate External Checks',
      []
    )

    assert.deepEqual(summary.failed, [])
    assert.deepEqual(summary.pending, [])
    assert.equal(summary.relevantCount, 0)
    assert.equal(summary.skippedCount, 2)
  }
})

test('Scenario_WebsiteRelease_BuildRunsWithoutDeploymentCredentials', () => {
  assert.match(websiteWorkflow, /permissions:\s+contents: read/)
  const buildJob = websiteWorkflow.slice(
    websiteWorkflow.indexOf('  build:'),
    websiteWorkflow.indexOf('  deploy:')
  )
  const deployJob = websiteWorkflow.slice(websiteWorkflow.indexOf('  deploy:'))
  assert.match(buildJob, /permissions:\s+contents: read\s+pages: read/)
  assert.match(buildJob, /uses: \.\/\.github\/actions\/website\/build/)
  assert.doesNotMatch(buildJob, /pages: write|id-token: write|deploy-pages/)
  assert.match(
    deployJob,
    /name: Deploy website[\s\S]*needs: build[\s\S]*permissions:\s+contents: read\s+pages: write\s+id-token: write/
  )
  assert.match(
    deployJob,
    /environment:\s+name: Release\s+url: \$\{\{ steps\.deployment\.outputs\.page_url \}\}/
  )
})

test('Scenario_ReleaseAction_ChangelogContainsOnlyRequestedVersion', () => {
  assert.match(releasePrepareAction, /default: projects\/cli\/CHANGELOG\.md/)

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

test('Scenario_PreviewPackage_ChangelogContainsOnlyUnreleasedNotes', () => {
  const changelog = `# Changelog

## Unreleased

Pending preview notes.

## Version 1.2.0 - 2026-08-29

Stable notes.
`

  assert.equal(
    extractUnreleasedNotes(changelog),
    '# Changelog\n\n## Unreleased\n\nPending preview notes.\n'
  )
  assert.throws(
    () => extractUnreleasedNotes('# Changelog\n'),
    /no Unreleased section/
  )
})

test('Scenario_PreviewWorkflow_PublishesOnlyNuGetForCliChangesOnMain', () => {
  assert.match(
    workflow,
    /push:[\s\S]*?tags:\s+- '\*\*'\s+branches:\s+- main\s+paths:\s+- 'projects\/cli\/\*\*'/
  )
  assert.match(workflow, /name: 'CLI: Release'/)
  assert.match(
    planJob,
    /if \(\$env:REF_TYPE -eq 'tag'\)[\s\S]*\$releaseType = 'stable'[\s\S]*\$releaseType = 'preview'/
  )
  assert.doesNotMatch(workflow, /workflow_dispatch/)
  assert.match(
    planJob,
    /dotnet minver \. --tag-prefix v --default-pre-release-identifiers preview/
  )
  assert.match(
    planJob,
    /release-type: \$\{\{ steps\.targets\.outputs\.release-type \}\}/
  )
  assert.match(planJob, /version: \$\{\{ steps\.targets\.outputs\.version \}\}/)
  assert.match(buildJob, /needs: plan/)
  assert.match(buildJob, /publish-artifacts: 'true'/)
  assert.match(releaseJob, /needs: \[plan, build, sanity-check\]/)
  assert.match(releaseJob, /needs\.sanity-check\.result == 'success'/)
  assert.match(releaseJob, /environment: Release/)
  assert.match(workflow, /cancel-in-progress: false/)
  assert.match(
    planJob,
    /git tag --points-at HEAD \| Where-Object \{ \$_ -cmatch \$stableTagPattern \}/
  )
  assert.match(
    planJob,
    /should-release: \$\{\{ steps\.trigger\.outputs\.should-release \}\}/
  )
  assert.match(
    buildJob,
    /if: \$\{\{ needs\.plan\.outputs\.should-release == 'true' \}\}/
  )
  assert.match(releaseJob, /needs\.plan\.outputs\.should-release == 'true'/)
  assert.match(planJob, /-preview\\\.\(0\|\[1-9\]\[0-9\]\*\)/)
  assert.match(
    releaseNuGetAction,
    /extract-release-notes\.mjs[\s\S]*"\$RUNNER_TEMP\/CHANGELOG\.md" unreleased/
  )
  assert.match(
    releaseNuGetAction,
    /-p:LunapackChangelogPath="\$RUNNER_TEMP\/CHANGELOG\.md"/
  )
  assert.match(
    releaseNuGetAction,
    /- name: Download RID-specific \.NET tools[\s\S]*pattern: \$\{\{ inputs\.artifact-pattern \}\}/
  )
  assert.equal(
    [...workflow.matchAll(/uses: \.\/\.github\/actions\/cli\/release/g)].length,
    1
  )
  assert.match(
    releaseJob,
    /release-type: \$\{\{ needs\.plan\.outputs\.release-type \}\}/
  )
  assert.match(releaseJob, /version: \$\{\{ needs\.plan\.outputs\.version \}\}/)
  assert.match(releaseJob, /nuget-user: \$\{\{ secrets\.NUGET_USER \}\}/)
  assert.match(releaseNuGetAction, /uses: NuGet\/login@[0-9a-f]{40}/)
  assert.match(releaseNuGetAction, /user: \$\{\{ inputs\.nuget-user \}\}/)
  assert.match(
    releaseSelectorAction,
    /release-type:[\s\S]*?currently publishes NuGet only[\s\S]*?default: stable/
  )
  assert.match(releaseSelectorAction, /channels='github,container,npm,nuget'/)
  assert.match(releaseSelectorAction, /channels='nuget'/)
  assert.match(
    releaseSelectorAction,
    /uses: \.\/\.github\/actions\/cli\/release\/channels/
  )
  assert.match(
    releaseSelectorAction,
    /- name: Prepare stable release\r?\n\s+if: \$\{\{ inputs\.release-type == 'stable' \}\}/
  )

  const prepareIndex = releaseSelectorAction.indexOf('Prepare stable release')
  const dispatchIndex = releaseSelectorAction.indexOf(
    'Release selected channels'
  )
  assert.ok(prepareIndex < dispatchIndex)
  assert.doesNotMatch(releaseChannelsAction, /release\/prepare/)

  const githubIndex = releaseChannelsAction.indexOf('Release GitHub channel')
  const containerIndex = releaseChannelsAction.indexOf(
    'Release container channel'
  )
  const npmIndex = releaseChannelsAction.indexOf('Release npm channel')
  const nugetIndex = releaseChannelsAction.indexOf('Release NuGet channel')
  assert.ok(
    githubIndex < containerIndex &&
      containerIndex < npmIndex &&
      npmIndex < nugetIndex
  )
  assert.match(releaseChannelsAction, /Unsupported release channel/)
  assert.match(releaseChannelsAction, /Duplicate release channel/)

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

  assert.doesNotMatch(
    releaseNuGetAction,
    /dotnet publish|runtime-identifier|Publish Native AOT preview/
  )
})

test('Scenario_ReleaseWorkflow_SanityChecksLinuxPackLifecycleWithoutScripts', () => {
  assert.match(sanityCheckJob, /needs: \[plan, build\]/)
  assert.match(sanityCheckJob, /runs-on: ubuntu-latest/)
  assert.match(sanityCheckJob, /name: cli-linux-x64/)

  for (const command of [
    '"$LUNA" init',
    '"$LUNA" sources add github lunapack lunarisdigitalsolutions/lunapack --ref main --path projects/packs',
    '"$LUNA" discover',
    '"$LUNA" search luna',
    '"$LUNA" install lunapack-testing@1.0.0 --scripts skip',
    '"$LUNA" update lunapack-testing --scripts skip',
    '"$LUNA" uninstall lunapack-testing --scripts skip'
  ]) {
    assert.ok(sanityCheckJob.includes(command))
  }

  assert.doesNotMatch(sanityCheckJob, /--scripts (?:prompt|run)/)
})

test('Scenario_ReleaseWorkflow_AcceptsOnlyOciSafeSemanticVersions', () => {
  assert.match(workflow, /-cnotmatch/)
  const workflowPattern = workflow.match(/-cnotmatch '([^']+)'/)[1]
  const releasePattern = releasePrepareAction.match(
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
  assert.match(releasePrepareAction, /without build metadata/)
})

test('Scenario_ReleaseWorkflow_RunsAreBoundedAndSerialized', () => {
  assert.match(
    workflow,
    /concurrency:\s+group: cli-release-\$\{\{ github\.ref \}\}/
  )
  assert.match(workflow, /cancel-in-progress: false/)
  assert.equal([...workflow.matchAll(/timeout-minutes:/g)].length, 4)
  assert.match(buildAction, /default: '3'/)
  assert.match(
    buildAction,
    /retention-days: \$\{\{ inputs\.artifact-retention-days \}\}/
  )
})

test('Scenario_CliBuild_CoverageIsInstrumentedAndPublishedToSummary', () => {
  for (const project of [
    'Lunapack.Cli.UnitTests',
    'Lunapack.Cli.IntegrationTests',
    'Lunapack.Cli.SecurityTests'
  ]) {
    assert.match(buildAction, new RegExp(`${project}/${project}\\.csproj`))
  }

  assert.doesNotMatch(buildAction, /coverage-settings-path/)
  assert.match(
    dotnetBuildAction,
    /test-build-configuration:[\s\S]*default: Debug/
  )
  assert.doesNotMatch(dotnetBuildAction, /coverage-settings-path/)
  assert.match(dotnetBuildAction, /'--coverage'/)
  assert.match(
    dotnetBuildAction,
    /'--coverage-output-format'[\s\S]*'cobertura'/
  )
  assert.match(
    dotnetBuildAction,
    /dorny\/test-reporter@[0-9a-f]{40} # v3\.0\.0/
  )
  assert.match(dotnetBuildAction, /reporter: dotnet-trx/)
  assert.match(
    dotnetBuildAction,
    /danielpalme\/ReportGenerator-GitHub-Action@[0-9a-f]{40} # 5\.5\.11/
  )
  assert.match(dotnetBuildAction, /\*\*\/\*\.cobertura\.xml/)
  assert.match(dotnetBuildAction, /reporttypes: MarkdownSummaryGithub/)
  assert.match(dotnetBuildAction, /GITHUB_STEP_SUMMARY/)
})

test('Scenario_ReleaseAction_ThirdPartyActionsUseCommitPins', () => {
  const externalActions = releaseActions.flatMap((action) => [
    ...action.matchAll(/^\s*uses: ([^./][^@]+)@([^\s]+)$/gm)
  ])
  assert.ok(externalActions.length > 0)
  for (const [, action, reference] of externalActions) {
    assert.match(reference, /^[0-9a-f]{40}$/, `${action} must use a commit SHA`)
  }
})

test('Scenario_ReleaseAction_DryRunSkipsEveryPublishingStep', () => {
  for (const action of [
    releaseGitHubAction,
    releaseContainerAction,
    releaseNpmAction,
    releaseNuGetAction
  ]) {
    assert.match(action, /dry-run:[\s\S]*default: 'false'/)
  }

  assert.match(
    releaseGitHubAction,
    /- name: Create GitHub Release\r?\n\s+if: \$\{\{ inputs\.dry-run != 'true' \}\}/
  )
  assert.match(
    releaseContainerAction,
    /- name: Authenticate to GitHub Container Registry\r?\n\s+if: \$\{\{ inputs\.dry-run != 'true' \}\}/
  )
  for (const step of [
    'Publish npm platform packages',
    'Publish npm entry package'
  ]) {
    assert.match(
      releaseNpmAction,
      new RegExp(
        `- name: ${step}\\r?\\n\\s+if: \\$\\{\\{ inputs\\.dry-run != 'true' \\}\\}`
      )
    )
  }
  assert.match(
    releaseNuGetAction,
    /- name: NuGet login\r?\n\s+id: login\r?\n\s+if: \$\{\{ inputs\.dry-run != 'true' \}\}/
  )
  assert.match(
    releaseNuGetAction,
    /- name: Publish \.NET tools\r?\n\s+if: \$\{\{ inputs\.dry-run != 'true' \}\}/
  )
})

test('Scenario_ReleaseAction_ExistingReleaseMustMatchStagedAssets', () => {
  assert.match(releaseGitHubAction, /gh release download "\$VERSION"/)
  assert.match(
    releaseGitHubAction,
    /node projects\/cli\/distribution\/verify-release-assets\.mjs/
  )
  assert.match(releaseGitHubAction, /gh release view "\$VERSION" --json body/)
  assert.doesNotMatch(releaseGitHubAction, /expected_notes|actual_notes/)
  assert.match(releaseGitHubAction, /Verified existing GitHub Release/)
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
  assert.match(
    releaseContainerAction,
    /uses: docker\/setup-buildx-action@[0-9a-f]{40}/
  )
  assert.match(
    releaseContainerAction,
    /packages\/native\/linux-x64\/luna[\s\S]*docker buildx build/
  )
  assert.doesNotMatch(releaseContainerAction, /tar -xzf/)
  assert.match(
    releaseContainerAction,
    /image="ghcr\.io\/\$\{GITHUB_REPOSITORY_OWNER,,\}\/lunapack"/
  )
  assert.match(releaseContainerAction, /output=\(--load\)/)
  assert.match(
    releaseContainerAction,
    /output=\(--push --provenance=mode=max --sbom=true\)/
  )
})
