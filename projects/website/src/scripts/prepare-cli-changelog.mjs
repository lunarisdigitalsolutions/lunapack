import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

export const unreleasedPlaceholder =
  'Update this section before creating a release tag.'

export function prepareChangelog(markdown) {
  const unreleasedHeading = /^##\s+Unreleased\s*$/m
  const match = unreleasedHeading.exec(markdown)

  if (!match) {
    return markdown
  }

  const bodyStart = match.index + match[0].length
  const followingContent = markdown.slice(bodyStart)
  const nextSection = /^##\s+/m.exec(followingContent)
  const bodyEnd = nextSection
    ? bodyStart + nextSection.index
    : markdown.length
  const body = markdown.slice(bodyStart, bodyEnd)
  const remainingBody = body
    .split(/\r?\n/)
    .filter((line) => line.trim() !== unreleasedPlaceholder)
    .join('\n')
    .trim()

  const replacement = remainingBody
    ? `## Unreleased\n\n${remainingBody}\n\n`
    : ''

  return markdown.slice(0, match.index) + replacement + markdown.slice(bodyEnd)
}

export function generateChangelogDocument(markdown) {
  const prepared = prepareChangelog(markdown)

  return `---
title: CLI Changelog
description: Luna CLI release history
---

${prepared.trimStart()}
`
}

async function main() {
  const scriptDirectory = path.dirname(fileURLToPath(import.meta.url))
  const repositoryRoot = path.resolve(scriptDirectory, '../../../../')
  const canonicalChangelog = path.join(
    repositoryRoot,
    'projects',
    'cli',
    'CHANGELOG.md'
  )
  const outputDirectory = path.resolve(
    scriptDirectory,
    '..',
    '.generated',
    'changelog'
  )
  const outputFile = path.join(outputDirectory, 'index.md')
  const markdown = await readFile(canonicalChangelog, 'utf8')

  await mkdir(outputDirectory, { recursive: true })
  await writeFile(outputFile, generateChangelogDocument(markdown), 'utf8')
  console.log(`Prepared ${path.relative(repositoryRoot, outputFile)}`)
}

const isDirectRun =
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)

if (isDirectRun) {
  main().catch((error) => {
    console.error(error)
    process.exitCode = 1
  })
}
