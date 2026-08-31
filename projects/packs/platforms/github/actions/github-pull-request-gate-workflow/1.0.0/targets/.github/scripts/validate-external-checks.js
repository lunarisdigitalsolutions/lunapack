function isSkippedCheck(name, skippedCheckNameParts) {
  const normalizedName = String(name ?? '').toLowerCase()
  return skippedCheckNameParts.some((part) => {
    const normalizedPart = String(part ?? '').toLowerCase()
    return normalizedPart.length > 0 && normalizedName.includes(normalizedPart)
  })
}

function latestByName(entries, nameSelector, timestampSelector) {
  const latestEntries = new Map()

  for (const entry of entries) {
    const name = nameSelector(entry)
    const current = latestEntries.get(name)

    if (
      !current ||
      new Date(timestampSelector(entry) ?? 0) >
        new Date(timestampSelector(current) ?? 0)
    ) {
      latestEntries.set(name, entry)
    }
  }

  return Array.from(latestEntries.values())
}

function formatSection(title, entries) {
  return entries.length === 0
    ? ''
    : `${title}\n${entries.map((entry) => `- ${entry}`).join('\n')}`
}

async function resolvePullRequest({ github, context, expectedBaseBranch }) {
  const { owner, repo } = context.repo
  const requestedNumber = context.payload.inputs?.pull_request_number

  if (context.eventName === 'pull_request') {
    return context.payload.pull_request
  }

  if (requestedNumber) {
    const response = await github.rest.pulls.get({
      owner,
      repo,
      pull_number: Number(requestedNumber)
    })
    return response.data
  }

  const headSha = context.payload.check_suite?.head_sha ?? context.sha
  if (!headSha) {
    return null
  }

  const pullRequests = await github.paginate(
    github.rest.repos.listPullRequestsAssociatedWithCommit,
    {
      owner,
      repo,
      commit_sha: headSha,
      per_page: 100
    }
  )

  return (
    pullRequests.find(
      (pullRequest) =>
        pullRequest.state === 'open' &&
        pullRequest.base?.ref === expectedBaseBranch &&
        pullRequest.head?.sha === headSha
    ) ?? null
  )
}

function collectCheckRunProblems(
  checkRuns,
  validationJobName,
  skippedCheckNameParts
) {
  const externalRuns = checkRuns.filter(
    (checkRun) =>
      checkRun.name !== validationJobName &&
      !isSkippedCheck(checkRun.name, skippedCheckNameParts)
  )
  const latestRuns = latestByName(
    externalRuns,
    (checkRun) => checkRun.name,
    (checkRun) => checkRun.started_at
  )
  const relevantRuns = latestRuns.filter(
    (checkRun) =>
      checkRun.conclusion !== 'skipped' && checkRun.conclusion !== 'cancelled'
  )
  const pending = []
  const failed = []

  for (const checkRun of relevantRuns) {
    if (checkRun.status !== 'completed') {
      pending.push(`${checkRun.name} (${checkRun.status})`)
    } else if (!['success', 'neutral'].includes(checkRun.conclusion)) {
      failed.push(`${checkRun.name} (${checkRun.conclusion})`)
    }
  }

  return {
    failed,
    pending,
    relevantCount: relevantRuns.length,
    skippedCount: latestRuns.length - relevantRuns.length,
    totalCount: latestRuns.length,
    unfilteredCount: externalRuns.length
  }
}

function collectStatusProblems(statuses, skippedCheckNameParts) {
  const latestStatuses = latestByName(
    statuses,
    (status) => status.context,
    (status) => status.updated_at
  ).filter((status) => !isSkippedCheck(status.context, skippedCheckNameParts))
  const pending = latestStatuses
    .filter((status) => status.state === 'pending')
    .map((status) => `${status.context} (${status.state})`)
  const failed = latestStatuses
    .filter(
      (status) => status.state !== 'success' && status.state !== 'pending'
    )
    .map((status) => `${status.context} (${status.state})`)

  return { failed, pending, totalCount: latestStatuses.length }
}

async function validateExternalChecks({
  github,
  context,
  core,
  skippedCheckNameParts = []
}) {
  const expectedBaseBranch = process.env.TARGET_BASE_BRANCH
  const validationJobName = process.env.VALIDATION_JOB_NAME
  const pullRequest = await resolvePullRequest({
    github,
    context,
    expectedBaseBranch
  })

  if (!pullRequest) {
    core.info(
      `No open pull request for ${context.eventName} at ${context.sha ?? 'an unknown SHA'}.`
    )
    return
  }

  if (pullRequest.draft) {
    core.info(`Skipping draft pull request #${pullRequest.number}.`)
    return
  }

  const { owner, repo } = context.repo
  const headSha = pullRequest.head.sha
  const [checkRuns, statusResponse] = await Promise.all([
    github.paginate(github.rest.checks.listForRef, {
      owner,
      repo,
      ref: headSha,
      filter: 'latest',
      per_page: 100
    }),
    github.rest.repos.getCombinedStatusForRef({ owner, repo, ref: headSha })
  ])

  const checkRunSummary = collectCheckRunProblems(
    checkRuns,
    validationJobName,
    skippedCheckNameParts
  )
  const statusSummary = collectStatusProblems(
    statusResponse.data.statuses ?? [],
    skippedCheckNameParts
  )
  const problems = [
    ...checkRunSummary.pending,
    ...checkRunSummary.failed,
    ...statusSummary.pending,
    ...statusSummary.failed
  ]

  core.info(
    [
      `Evaluating pull request #${pullRequest.number}.`,
      `Head SHA: ${headSha}`,
      `External check runs: ${checkRunSummary.totalCount} (${checkRunSummary.unfilteredCount} before deduplication).`,
      `Skipped or cancelled check runs: ${checkRunSummary.skippedCount}.`,
      `Commit statuses: ${statusSummary.totalCount}.`
    ].join('\n')
  )

  if (checkRunSummary.totalCount + statusSummary.totalCount === 0) {
    core.setFailed(
      'No external checks were found yet. Wait for at least one non-gate result.'
    )
    return
  }

  if (checkRunSummary.relevantCount === 0 && statusSummary.totalCount === 0) {
    core.info('All discovered external check runs were skipped or cancelled.')
    return
  }

  if (problems.length > 0) {
    const details = [
      formatSection('Pending checks:', [
        ...checkRunSummary.pending,
        ...statusSummary.pending
      ]),
      formatSection('Failing checks:', [
        ...checkRunSummary.failed,
        ...statusSummary.failed
      ])
    ]
      .filter(Boolean)
      .join('\n\n')
    core.setFailed(`External checks have not all succeeded.\n\n${details}`)
    return
  }

  core.info('All non-skipped external checks and commit statuses succeeded.')
}

module.exports = { validateExternalChecks }
