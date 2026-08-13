/** Build verified Windows installer and portable release assets. */

import { spawn, type ChildProcess } from 'node:child_process'
import { createHash } from 'node:crypto'
import { createReadStream, existsSync } from 'node:fs'
import { mkdir, mkdtemp, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { parseArgs } from 'node:util'

const root = resolve(import.meta.dirname, '..')
const distribution = join(root, '.artifacts', 'DeepSeek-Harness-Desktop')
const releaseDirectory = join(root, '.artifacts', 'desktop-release')
const projectPath = join(root, 'apps', 'desktop', 'DeepSeekHarness.Desktop.csproj')
const installerScript = join(root, 'apps', 'desktop', 'installer', 'DeepSeekHarness.iss')
const executableName = 'DeepSeek Harness.exe'

interface Options {
  skipBuild: boolean
  skipInstallerSmoke: boolean
}

function options(): Options {
  const rawArgs = process.argv.slice(2)
  const args = rawArgs[0] === '--' ? rawArgs.slice(1) : rawArgs
  const parsed = parseArgs({
    args,
    options: {
      'skip-build': { type: 'boolean', default: false },
      'skip-installer-smoke': { type: 'boolean', default: false },
      help: { type: 'boolean', default: false },
    },
    strict: true,
  })
  if (parsed.values.help) {
    console.log([
      'Usage: pnpm exec tsx scripts/package-desktop-windows.ts [options]',
      '',
      '  --skip-build             reuse .artifacts/DeepSeek-Harness-Desktop',
      '  --skip-installer-smoke   compile assets without install/uninstall verification',
    ].join('\n'))
    process.exit(0)
  }
  return {
    skipBuild: parsed.values['skip-build'],
    skipInstallerSmoke: parsed.values['skip-installer-smoke'],
  }
}

function printable(command: string, args: readonly string[]): string {
  return [command, ...args].map(value => /\s/u.test(value) ? JSON.stringify(value) : value).join(' ')
}

async function run(command: string, args: string[], env?: NodeJS.ProcessEnv): Promise<void> {
  console.log(`desktop-package: ${printable(command, args)}`)
  const commandUsesCmd = process.platform === 'win32' && command.toLowerCase().endsWith('.cmd')
  const executable = commandUsesCmd ? process.env.ComSpec ?? 'cmd.exe' : command
  const processArgs = commandUsesCmd ? ['/d', '/c', command, ...args] : args
  await new Promise<void>((resolvePromise, reject) => {
    const child = spawn(executable, processArgs, { cwd: root, env: env ?? process.env, stdio: 'inherit' })
    child.once('error', reject)
    child.once('exit', (code, signal) => {
      if (code === 0) resolvePromise()
      else reject(new Error(`${executable} failed with ${code === null ? `signal ${signal ?? 'unknown'}` : `exit ${String(code)}`}`))
    })
  })
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise<void>((resolvePromise) => {
    setTimeout(resolvePromise, milliseconds)
  })
}

function hasExited(child: ChildProcess): boolean {
  return child.exitCode !== null || child.signalCode !== null
}

async function desktopVersion(): Promise<string> {
  const project = await readFile(projectPath, 'utf8')
  const match = /<Version>([^<]+)<\/Version>/u.exec(project)
  if (match?.[1] === undefined || !/^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/u.test(match[1])) {
    throw new Error(`desktop-package: ${projectPath} must contain one valid <Version>`)
  }
  return match[1]
}

function findInnoCompiler(): string {
  const candidates = [
    process.env.ISCC_PATH,
    join(process.env.LOCALAPPDATA ?? '', 'Programs', 'Inno Setup 6', 'ISCC.exe'),
    join(process.env['ProgramFiles(x86)'] ?? '', 'Inno Setup 6', 'ISCC.exe'),
    join(process.env.ProgramFiles ?? '', 'Inno Setup 6', 'ISCC.exe'),
  ]
  const compiler = candidates.find(candidate => candidate !== undefined && existsSync(candidate))
  if (compiler === undefined) {
    throw new Error('desktop-package: Inno Setup 6 not found; install JRSoftware.InnoSetup or set ISCC_PATH')
  }
  return compiler
}

async function sha256(path: string): Promise<string> {
  const hash = createHash('sha256')
  await new Promise<void>((resolvePromise, reject) => {
    const stream = createReadStream(path)
    stream.on('data', chunk => hash.update(chunk))
    stream.once('error', reject)
    stream.once('end', resolvePromise)
  })
  return hash.digest('hex')
}

async function assertDistribution(version: string): Promise<void> {
  const required = [
    join(distribution, executableName),
    join(distribution, 'runtime', 'node', 'node.exe'),
    join(distribution, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'desktop-bin.js'),
    join(distribution, 'desktop-manifest.json'),
  ]
  const missing = required.filter(path => !existsSync(path))
  if (missing.length > 0) throw new Error(`desktop-package: distribution missing:\n${missing.join('\n')}`)
  const manifest = JSON.parse(await readFile(join(distribution, 'desktop-manifest.json'), 'utf8')) as { version?: string }
  if (manifest.version !== version) {
    throw new Error(`desktop-package: distribution version ${String(manifest.version)} does not match ${version}`)
  }
}

async function directorySize(directory: string): Promise<number> {
  let size = 0
  const pending = [directory]
  while (pending.length > 0) {
    const current = pending.pop()
    if (current === undefined) break
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = join(current, entry.name)
      if (entry.isDirectory()) pending.push(path)
      else if (entry.isFile()) size += (await stat(path)).size
    }
  }
  return size
}

async function packagedEntries(directory: string): Promise<string[]> {
  if (!existsSync(directory)) return []
  const entries = await readdir(directory, { withFileTypes: true })
  return entries
    .filter(entry => entry.isDirectory() || entry.name !== 'user-note.txt')
    .map(entry => entry.name)
    .sort()
}

async function main(): Promise<void> {
  if (process.platform !== 'win32' || process.arch !== 'x64') {
    throw new Error(`desktop-package: Windows x64 host required, got ${process.platform}-${process.arch}`)
  }
  const request = options()
  const version = await desktopVersion()
  if (!request.skipBuild) {
    await run('pnpm.cmd', ['run', 'desktop:build'])
  }
  await assertDistribution(version)
  await rm(releaseDirectory, { recursive: true, force: true })
  await mkdir(releaseDirectory, { recursive: true })

  const installer = `DeepSeek-Harness-Desktop-${version}-win-x64-Setup.exe`
  const portableName = `DeepSeek-Harness-Desktop-${version}-win-x64.zip`
  const portablePath = join(releaseDirectory, portableName)
  await run('tar.exe', ['-a', '-c', '-f', portablePath, '-C', join(distribution, '..'), 'DeepSeek-Harness-Desktop'])
  await run(findInnoCompiler(), [
    `/DMyAppVersion=${version}`,
    `/DSourceDirectory=${distribution}`,
    `/DOutputDirectory=${releaseDirectory}`,
    `/DPayloadZip=${portablePath}`,
    installerScript,
  ])
  const installerPath = join(releaseDirectory, installer)
  if (!existsSync(installerPath)) throw new Error(`desktop-package: installer missing: ${installerPath}`)

  if (!request.skipInstallerSmoke) {
    const fixture = await mkdtemp(join(tmpdir(), 'dsh-desktop-installer-'))
    const installed = join(fixture, 'installed')
    const result = join(fixture, 'smoke-result.json')
    const harnessHome = join(fixture, 'home')
    const workspace = join(fixture, 'workspace')
    await Promise.all([mkdir(harnessHome, { recursive: true }), mkdir(workspace, { recursive: true })])
    await run(installerPath, [
      '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
      `/DIR=${installed}`,
      `/LOG=${join(fixture, 'install.log')}`,
    ])
    const installedExecutable = join(installed, executableName)
    if (!existsSync(installedExecutable)) throw new Error('desktop-package: silent install did not write the application')
    const env: NodeJS.ProcessEnv = { ...process.env, DSH_HOME: harnessHome, DSH_TELEMETRY_DISABLED: '1' }
    delete env.DEEPSEEK_API_KEY
    delete env.DEEPSEEK_BASE_URL
    delete env.NODE_OPTIONS
    await run(installedExecutable, [
      '--workspace', workspace,
      '--smoke-result', result,
    ], env)
    const smoke = JSON.parse(await readFile(result, 'utf8')) as {
      Success?: boolean
      WebViewLoaded?: boolean
      GracefulShutdown?: boolean
      Error?: string
    }
    if (smoke.Success !== true || smoke.WebViewLoaded !== true || smoke.GracefulShutdown !== true) {
      throw new Error(`desktop-package: installed lifecycle smoke failed: ${JSON.stringify(smoke)}`)
    }

    const running = spawn(installedExecutable, ['--workspace', workspace], {
      cwd: workspace,
      env,
      stdio: 'ignore',
    })
    await delay(5_000)
    if (hasExited(running)) {
      throw new Error('desktop-package: installed desktop exited before active-uninstall verification')
    }
    await run(installedExecutable, ['--workspace', workspace], env)
    await delay(1_000)
    if (hasExited(running)) {
      throw new Error('desktop-package: second launch closed the primary desktop instance')
    }
    await writeFile(join(installed, 'user-note.txt'), 'foreign file preserved by uninstall\n')
    await run(join(installed, 'unins000.exe'), [
      '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
      `/LOG=${join(fixture, 'uninstall.log')}`,
    ])
    await new Promise<void>((resolvePromise, reject) => {
      if (hasExited(running)) {
        resolvePromise()
        return
      }
      const timer = setTimeout(() => {
        reject(new Error('desktop-package: uninstall left the desktop process running'))
      }, 30_000)
      running.once('exit', () => {
        clearTimeout(timer)
        resolvePromise()
      })
      running.once('error', reject)
    })
    const uninstallDeadline = Date.now() + 15_000
    let leftovers = await packagedEntries(installed)
    while (leftovers.length > 0 && Date.now() < uninstallDeadline) {
      await delay(250)
      leftovers = await packagedEntries(installed)
    }
    if (leftovers.length > 0) {
      throw new Error(`desktop-package: uninstall left packaged entries: ${leftovers.join(', ')}`)
    }
    if (!existsSync(join(installed, 'user-note.txt'))) {
      throw new Error('desktop-package: uninstall removed the foreign fixture file')
    }
    await rm(fixture, { recursive: true, force: true })
    console.log('desktop-package: silent install, WebView lifecycle, second launch, active uninstall, packaged cleanup, and foreign-file preservation smoke passed')
  }

  const assets = (await readdir(releaseDirectory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && (entry.name.endsWith('.exe') || entry.name.endsWith('.zip')))
    .map(entry => entry.name)
    .sort()
  const sums: string[] = []
  for (const asset of assets) sums.push(`${await sha256(join(releaseDirectory, asset))}  ${asset}`)
  await writeFile(join(releaseDirectory, 'SHA256SUMS.txt'), `${sums.join('\n')}\n`)

  console.log('desktop-package: release assets:')
  for (const asset of [...assets, 'SHA256SUMS.txt']) {
    const path = join(releaseDirectory, asset)
    console.log(`  ${path} (${((await stat(path)).size / 1024 / 1024).toFixed(1)} MB)`)
  }
  console.log(`  source distribution ${((await directorySize(distribution)) / 1024 / 1024).toFixed(1)} MB`)
}

await main()
