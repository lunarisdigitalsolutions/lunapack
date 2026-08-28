import { execFileSync } from 'node:child_process'
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  rmSync,
  writeFileSync
} from 'node:fs'
import { basename, join, resolve } from 'node:path'

export const targets = [
  {
    rid: 'win-x64',
    packageSuffix: 'win64',
    os: 'win32',
    cpu: 'x64',
    binary: 'luna.exe',
    archive: 'zip'
  },
  {
    rid: 'linux-x64',
    packageSuffix: 'linux-x64',
    os: 'linux',
    cpu: 'x64',
    binary: 'luna',
    archive: 'tar.gz'
  },
  {
    rid: 'linux-arm64',
    packageSuffix: 'linux-arm64',
    os: 'linux',
    cpu: 'arm64',
    binary: 'luna',
    archive: 'tar.gz'
  },
  {
    rid: 'osx-x64',
    packageSuffix: 'macos-x64',
    os: 'darwin',
    cpu: 'x64',
    binary: 'luna',
    archive: 'tar.gz'
  },
  {
    rid: 'osx-arm64',
    packageSuffix: 'macos-arm64',
    os: 'darwin',
    cpu: 'arm64',
    binary: 'luna',
    archive: 'tar.gz'
  }
]

export function packageName(target) {
  return `@lunarisdigitalsolutions/lunapack-${target.packageSuffix}`
}

export function targetForPlatform(platform, architecture) {
  return targets.find(
    (target) => target.os === platform && target.cpu === architecture
  )
}

function parseArguments(argumentsList) {
  const values = new Map()
  for (let index = 0; index < argumentsList.length; index += 2) {
    values.set(argumentsList[index], argumentsList[index + 1])
  }
  return values
}

function run(command, argumentsList, environment = {}) {
  return execFileSync(command, argumentsList, {
    encoding: 'utf8',
    env: { ...process.env, ...environment },
    maxBuffer: 1024 * 1024
  })
}

function listArchiveEntries(archivePath) {
  if (archivePath.endsWith('.zip')) {
    const powershell = process.platform === 'win32' ? 'powershell.exe' : 'pwsh'
    const output = run(
      powershell,
      [
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        `Add-Type -AssemblyName System.IO.Compression.FileSystem; $archive = [IO.Compression.ZipFile]::OpenRead($env:LUNA_ARCHIVE_PATH); try { $entries = @($archive.Entries | ForEach-Object { $mode = ($_.ExternalAttributes -shr 16) -band 0xF000; $type = if ($_.FullName.EndsWith('/')) { 'directory' } elseif ($mode -eq 0xA000) { 'link' } else { 'file' }; [PSCustomObject]@{ name = $_.FullName; type = $type } }); ConvertTo-Json -InputObject $entries -Compress } finally { $archive.Dispose() }`
      ],
      { LUNA_ARCHIVE_PATH: archivePath }
    )
    return JSON.parse(output)
  }

  const names = run('tar', ['-tzf', archivePath]).split(/\r?\n/).filter(Boolean)
  const details = run('tar', ['-tvzf', archivePath])
    .split(/\r?\n/)
    .filter(Boolean)
  if (names.length !== details.length) {
    throw new Error(
      `Unable to inspect every archive entry: ${basename(archivePath)}`
    )
  }

  return names.map((name, index) => ({
    name,
    type:
      details[index][0] === '-'
        ? 'file'
        : details[index][0] === 'd'
          ? 'directory'
          : 'special'
  }))
}

export function validateArchiveEntries(entries, expectedBinary, archiveName) {
  if (entries.length === 0 || entries.length > 256) {
    throw new Error(
      `Distribution archive has an invalid entry count: ${archiveName}`
    )
  }

  const paths = new Set()
  let containsBinary = false
  for (const entry of entries) {
    if (!['file', 'directory'].includes(entry.type)) {
      throw new Error(
        `Distribution archive contains a link or special entry: ${archiveName}`
      )
    }

    const name = entry.name.startsWith('./') ? entry.name.slice(2) : entry.name
    const path = name.endsWith('/') ? name.slice(0, -1) : name
    if (path === '' && entry.type === 'directory') {
      continue
    }

    const segments = path.split('/')
    if (
      path.length > 512 ||
      path.includes('\\') ||
      path.startsWith('/') ||
      /^[A-Za-z]:/.test(path) ||
      segments.some(
        (segment) => segment === '' || segment === '.' || segment === '..'
      )
    ) {
      throw new Error(
        `Distribution archive contains an unsafe path: ${archiveName}`
      )
    }

    const comparisonPath = path.toLowerCase()
    if (paths.has(comparisonPath)) {
      throw new Error(
        `Distribution archive contains duplicate paths: ${archiveName}`
      )
    }
    paths.add(comparisonPath)
    containsBinary ||= entry.type === 'file' && path === expectedBinary
  }

  if (!containsBinary) {
    throw new Error(
      `Distribution archive does not contain ${expectedBinary}: ${archiveName}`
    )
  }
}

function validateExtractedTree(directory, archiveName) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isSymbolicLink() || (!entry.isFile() && !entry.isDirectory())) {
      throw new Error(
        `Distribution archive extracted an unsupported file: ${archiveName}`
      )
    }
    if (entry.isDirectory()) {
      validateExtractedTree(path, archiveName)
    }
  }
}

function extractArchive(archivePath, outputDirectory, expectedBinary) {
  validateArchiveEntries(
    listArchiveEntries(archivePath),
    expectedBinary,
    basename(archivePath)
  )

  if (archivePath.endsWith('.zip')) {
    if (process.platform === 'win32') {
      const command = `Expand-Archive -LiteralPath '${archivePath.replaceAll("'", "''")}' -DestinationPath '${outputDirectory.replaceAll("'", "''")}' -Force`
      execFileSync('powershell.exe', [
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        command
      ])
    } else {
      execFileSync('unzip', ['-q', archivePath, '-d', outputDirectory])
    }
  } else {
    execFileSync('tar', ['-xzf', archivePath, '-C', outputDirectory])
  }

  validateExtractedTree(outputDirectory, basename(archivePath))
}

function writeJson(path, value) {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`)
}

export function stagePackages({ archiveDirectory, outputDirectory, version }) {
  rmSync(outputDirectory, { recursive: true, force: true })
  mkdirSync(outputDirectory, { recursive: true })

  const packageRoot = join(outputDirectory, 'npm')
  const nativeRoot = join(outputDirectory, 'native')
  mkdirSync(packageRoot, { recursive: true })
  mkdirSync(nativeRoot, { recursive: true })

  for (const target of targets) {
    const archiveName = `luna-cli-${version}-${target.rid}.${target.archive}`
    const archivePath = join(archiveDirectory, archiveName)
    if (!existsSync(archivePath)) {
      throw new Error(`Missing distribution archive: ${archiveName}`)
    }

    const nativeDirectory = join(nativeRoot, target.rid)
    mkdirSync(nativeDirectory, { recursive: true })
    extractArchive(archivePath, nativeDirectory, target.binary)

    const binaryPath = join(nativeDirectory, target.binary)
    if (!existsSync(binaryPath) || !lstatSync(binaryPath).isFile()) {
      throw new Error(
        `Distribution archive does not contain ${target.binary}: ${archiveName}`
      )
    }

    const packageDirectory = join(
      packageRoot,
      `lunapack-${target.packageSuffix}`
    )
    mkdirSync(packageDirectory, { recursive: true })
    cpSync(binaryPath, join(packageDirectory, target.binary))
    cpSync(
      resolve(import.meta.dirname, '..', 'README.md'),
      join(packageDirectory, 'README.md')
    )
    writeJson(join(packageDirectory, 'package.json'), {
      name: packageName(target),
      version,
      description: `Luna native binary for ${target.os} ${target.cpu}.`,
      os: [target.os],
      cpu: [target.cpu],
      files: [target.binary, 'README.md'],
      license: 'MIT',
      repository: 'github:lunarisdigitalsolutions/lunapack'
    })
  }

  const entryDirectory = join(packageRoot, 'lunapack')
  mkdirSync(entryDirectory, { recursive: true })
  cpSync(
    resolve(import.meta.dirname, 'run-luna.mjs'),
    join(entryDirectory, 'run-luna.mjs')
  )
  cpSync(
    resolve(import.meta.dirname, '..', 'README.md'),
    join(entryDirectory, 'README.md')
  )
  writeJson(join(entryDirectory, 'package.json'), {
    name: '@lunarisdigitalsolutions/lunapack',
    version,
    description: 'Luna CLI.',
    bin: { luna: 'run-luna.mjs' },
    optionalDependencies: Object.fromEntries(
      targets.map((target) => [packageName(target), version])
    ),
    files: ['run-luna.mjs', 'README.md'],
    license: 'MIT',
    repository: 'github:lunarisdigitalsolutions/lunapack'
  })

  return { packageRoot, nativeRoot, packages: readdirSync(packageRoot).sort() }
}

if (process.argv[1] === import.meta.filename) {
  const argumentsByName = parseArguments(process.argv.slice(2))
  const archiveDirectory = argumentsByName.get('--archives')
  const outputDirectory = argumentsByName.get('--output')
  const version = argumentsByName.get('--version')
  if (!archiveDirectory || !outputDirectory || !version) {
    throw new Error(
      'Usage: node package-distribution.mjs --archives <directory> --output <directory> --version <version>'
    )
  }

  const staged = stagePackages({
    archiveDirectory: resolve(archiveDirectory),
    outputDirectory: resolve(outputDirectory),
    version
  })
  process.stdout.write(
    `${basename(staged.packageRoot)}: ${staged.packages.join(', ')}\n`
  )
}
