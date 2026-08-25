import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'

export function expectedReleaseAssetNames(version) {
  return [
    `luna-cli-${version}-linux-arm64.tar.gz`,
    `luna-cli-${version}-linux-x64.tar.gz`,
    `luna-cli-${version}-osx-arm64.tar.gz`,
    `luna-cli-${version}-osx-x64.tar.gz`,
    `luna-cli-${version}-win-x64.zip`,
    'CHANGELOG.md',
    'SHA256SUMS.txt'
  ].sort()
}

export function verifyReleaseAssets(
  stagingDirectory,
  releaseDirectory,
  releaseMetadataPath,
  version
) {
  const expectedNames = expectedReleaseAssetNames(version)
  const actualNames = readdirSync(releaseDirectory).sort()
  if (JSON.stringify(actualNames) !== JSON.stringify(expectedNames)) {
    throw new Error(
      `Existing release assets differ from the expected set. Expected: ${expectedNames.join(', ')}. Actual: ${actualNames.join(', ')}.`
    )
  }

  for (const assetName of expectedNames) {
    const localPath = assetName.startsWith('luna-cli-')
      ? join(stagingDirectory, 'distributions', assetName)
      : join(stagingDirectory, assetName)
    const releasePath = join(releaseDirectory, assetName)
    if (!readFileSync(localPath).equals(readFileSync(releasePath))) {
      throw new Error(`Existing release asset differs: ${assetName}.`)
    }
  }

  const metadata = JSON.parse(readFileSync(releaseMetadataPath, 'utf8'))
  const expectedNotes = readFileSync(
    join(stagingDirectory, 'CHANGELOG.md'),
    'utf8'
  )
  if (metadata.body !== expectedNotes) {
    throw new Error('Existing GitHub Release notes differ.')
  }
}

function main() {
  const [stagingDirectory, releaseDirectory, releaseMetadataPath, version] =
    process.argv.slice(2)
  if (
    !stagingDirectory ||
    !releaseDirectory ||
    !releaseMetadataPath ||
    !version
  ) {
    throw new Error(
      'Usage: node verify-release-assets.mjs <staging> <release> <metadata> <version>'
    )
  }

  verifyReleaseAssets(
    stagingDirectory,
    releaseDirectory,
    releaseMetadataPath,
    version
  )
  console.log(`Verified existing release assets for ${version}.`)
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
