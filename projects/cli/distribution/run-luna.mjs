#!/usr/bin/env node
import { spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'

const require = createRequire(import.meta.url)
const targets = {
  'win32-x64': ['@lunarisdigitalsolutions/lunapack-win64', 'luna.exe'],
  'linux-x64': ['@lunarisdigitalsolutions/lunapack-linux-x64', 'luna'],
  'linux-arm64': ['@lunarisdigitalsolutions/lunapack-linux-arm64', 'luna'],
  'darwin-x64': ['@lunarisdigitalsolutions/lunapack-macos-x64', 'luna'],
  'darwin-arm64': ['@lunarisdigitalsolutions/lunapack-macos-arm64', 'luna']
}

export function resolveBinary(
  platform,
  architecture,
  resolvePackage = require.resolve
) {
  const target = targets[`${platform}-${architecture}`]
  if (!target) {
    throw new Error(
      `Unsupported Luna platform ${platform}-${architecture}. Supported targets: ${Object.keys(targets).join(', ')}.`
    )
  }

  const [packageName, binaryName] = target
  let packageJson
  try {
    packageJson = resolvePackage(`${packageName}/package.json`)
  } catch {
    throw new Error(
      `Missing ${packageName}. Reinstall @lunarisdigitalsolutions/lunapack with optional dependencies enabled.`
    )
  }

  const binary = join(dirname(packageJson), binaryName)
  if (!existsSync(binary)) {
    throw new Error(
      `Missing Luna binary in ${packageName}. Reinstall @lunarisdigitalsolutions/lunapack.`
    )
  }

  return binary
}

if (process.argv[1] === import.meta.filename) {
  try {
    const binary = resolveBinary(process.platform, process.arch)
    const result = spawnSync(binary, process.argv.slice(2), {
      stdio: 'inherit'
    })
    if (result.error) {
      throw result.error
    }
    process.exitCode = result.status ?? 1
  } catch (error) {
    process.stderr.write(`luna: ${error.message}\n`)
    process.exitCode = 1
  }
}
