import { readFileSync, writeFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

export function extractUnreleasedNotes(changelog) {
  const lines = changelog.replaceAll('\r\n', '\n').split('\n')
  const start = lines.findIndex((line) => line === '## Unreleased')
  if (start < 0) {
    throw new Error('Changelog has no Unreleased section.')
  }

  const nextVersion = lines.findIndex(
    (line, index) => index > start && line.startsWith('## Version ')
  )
  const end = nextVersion < 0 ? lines.length : nextVersion
  return `# Changelog\n\n${lines.slice(start, end).join('\n').trimEnd()}\n`
}

export function extractReleaseNotes(changelog, version) {
  const lines = changelog.replaceAll('\r\n', '\n').split('\n')
  const heading = `## Version ${version} - `
  const start = lines.findIndex((line) => line.startsWith(heading))
  if (start < 0) {
    throw new Error(`Changelog has no section for version ${version}.`)
  }

  const nextVersion = lines.findIndex(
    (line, index) => index > start && line.startsWith('## Version ')
  )
  const end = nextVersion < 0 ? lines.length : nextVersion
  return `${lines.slice(start, end).join('\n').trimEnd()}\n`
}

function main() {
  const [changelogPath, outputPath, version] = process.argv.slice(2)
  if (!changelogPath || !outputPath || !version) {
    throw new Error(
      'Usage: node extract-release-notes.mjs <changelog> <output> <version>'
    )
  }

  const changelog = readFileSync(changelogPath, 'utf8')
  const releaseNotes =
    version === 'unreleased'
      ? extractUnreleasedNotes(changelog)
      : extractReleaseNotes(changelog, version)
  writeFileSync(outputPath, releaseNotes, 'utf8')
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
