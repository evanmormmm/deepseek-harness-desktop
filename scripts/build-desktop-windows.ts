/**
 * Build and verify the Windows desktop distribution. The native host stays a
 * small self-contained .NET executable; `runtime/node` carries Node and
 * `runtime/harness` is pnpm's symlink-free production deploy of the shipped
 * CLI, including the Web frontend and Windows native modules.
 */

import { spawn } from 'node:child_process'
import { createConnection } from 'node:net'
import { createHash, randomUUID } from 'node:crypto'
import { createReadStream, existsSync } from 'node:fs'
import { cp, lstat, mkdir, readFile, readdir, realpath, rename, rm, stat, unlink, writeFile } from 'node:fs/promises'
import { join, relative, resolve, sep } from 'node:path'
import { createInterface } from 'node:readline'
import { parseArgs } from 'node:util'

const root = resolve(import.meta.dirname, '..')
const artifactsRoot = join(root, '.artifacts')
const defaultOutput = join(artifactsRoot, 'DeepSeek-Harness-Desktop')
const desktopProject = join(root, 'apps', 'desktop', 'DeepSeekHarness.Desktop.csproj')
const desktopTests = join(root, 'apps', 'desktop', 'tests', 'DeepSeekHarness.Desktop.Tests.csproj')
const installScript = join(root, 'scripts', 'install-desktop-windows.ps1')
const executableName = 'DeepSeek Harness.exe'
const webView2Version = '1.0.4078.44'

async function readDesktopVersion(): Promise<string> {
  const project = await readFile(desktopProject, 'utf8')
  const match = /<Version>([^<]+)<\/Version>/u.exec(project)
  if (match?.[1] === undefined || !/^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/u.test(match[1])) {
    throw new Error(`desktop-build: ${desktopProject} must contain one valid <Version>`)
  }
  return match[1]
}

interface BuildOptions {
  output: string
  skipHarnessBuild: boolean
  skipTests: boolean
  install: boolean
}

function parseOptions(): BuildOptions {
  const { values } = parseArgs({
    args: process.argv.slice(2),
    options: {
      output: { type: 'string' },
      'skip-harness-build': { type: 'boolean', default: false },
      'skip-tests': { type: 'boolean', default: false },
      install: { type: 'boolean', default: false },
      help: { type: 'boolean', default: false },
    },
    strict: true,
  })
  if (values.help) {
    console.log([
      'Usage: pnpm exec tsx scripts/build-desktop-windows.ts [options]',
      '',
      '  --output <dir>          distribution directory (default: .artifacts/DeepSeek-Harness-Desktop)',
      '  --skip-harness-build    reuse current lib/ and apps/web/dist artifacts',
      '  --skip-tests            skip .NET unit and built desktop-adapter tests',
      '  --install               copy the verified distribution to LocalAppData and create shortcuts',
      '  --help                  print this help',
    ].join('\n'))
    process.exit(0)
  }
  return {
    output: resolve(root, values.output ?? defaultOutput),
    skipHarnessBuild: values['skip-harness-build'],
    skipTests: values['skip-tests'],
    install: values.install,
  }
}

function assertDescendant(parent: string, target: string, label: string): void {
  const rel = relative(resolve(parent), resolve(target))
  if (rel === '' || rel === '..' || rel.startsWith(`..${sep}`)) {
    throw new Error(`desktop-build: ${label} must be a descendant of ${parent}, got ${target}`)
  }
}

function printable(command: string, args: readonly string[]): string {
  return [command, ...args].map(value => /\s/u.test(value) ? JSON.stringify(value) : value).join(' ')
}

async function run(command: string, args: string[], cwd = root, env?: NodeJS.ProcessEnv): Promise<void> {
  const commandUsesCmd = process.platform === 'win32' && command.toLowerCase().endsWith('.cmd')
  const shellCommand = process.platform === 'win32' && command === 'pnpm' ? 'pnpm.cmd' : command
  const executable = process.platform === 'win32' && (command === 'pnpm' || commandUsesCmd)
    ? process.env.ComSpec ?? 'cmd.exe'
    : command
  const processArgs = process.platform === 'win32' && (command === 'pnpm' || commandUsesCmd)
    ? ['/d', '/c', shellCommand, ...args]
    : args
  console.log(`desktop-build: ${printable(shellCommand, args)}`)
  await new Promise<void>((resolvePromise, reject) => {
    const child = spawn(executable, processArgs, { cwd, env: env ?? process.env, stdio: 'inherit' })
    child.once('error', (error) => {
      reject(new Error(`desktop-build: failed to spawn ${executable}: ${error.message}`))
    })
    child.once('exit', (code, signal) => {
      if (code === 0) resolvePromise()
      else reject(new Error(`desktop-build: ${executable} failed with ${code === null ? `signal ${signal ?? 'unknown'}` : `exit ${String(code)}`}`))
    })
  })
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

async function findLinks(directory: string): Promise<string[]> {
  const links: string[] = []
  const pending = [directory]
  while (pending.length > 0) {
    const current = pending.pop()
    if (current === undefined) break
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = join(current, entry.name)
      if (entry.isSymbolicLink()) links.push(path)
      else if (entry.isDirectory()) pending.push(path)
    }
  }
  return links
}

async function materializeLinks(directory: string): Promise<void> {
  let links = await findLinks(directory)
  while (links.length > 0) {
    for (const destination of links) {
      const metadata = await lstat(destination)
      if (!metadata.isSymbolicLink()) continue
      const source = await realpath(destination)
      const sourceNodeModules = join(source, 'node_modules')
      await unlink(destination)
      await cp(source, destination, {
        recursive: true,
        dereference: true,
        filter: path => path !== sourceNodeModules && !path.startsWith(sourceNodeModules + sep),
      })
      console.log(`desktop-build: materialized ${relative(directory, destination)} from ${source}`)
    }
    links = await findLinks(directory)
  }
}

function requiredPaths(output: string): string[] {
  return [
    join(output, executableName),
    join(output, 'WebView2Loader.dll'),
    join(output, 'runtime', 'node', 'node.exe'),
    join(output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'desktop-bin.js'),
    join(output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh', 'config', 'agent-presets', 'standard', 'preset.yml'),
    join(output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh-web-app', 'cordis.patch.yml'),
    join(output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh-web-frontend', 'dist', 'index.html'),
    join(output, 'runtime', 'harness', 'node_modules', 'node-pty', 'prebuilds', 'win32-x64', 'pty.node'),
    join(output, 'runtime', 'harness', 'node_modules', 'node-pty', 'prebuilds', 'win32-x64', 'conpty', 'conpty.dll'),
  ]
}

async function verifyLayout(output: string): Promise<void> {
  const missing = requiredPaths(output).filter(path => !existsSync(path))
  if (missing.length > 0) throw new Error(`desktop-build: distribution is missing:\n${missing.map(path => `  ${path}`).join('\n')}`)
  const links = await findLinks(output)
  if (links.length > 0) throw new Error(`desktop-build: distribution contains filesystem links:\n${links.slice(0, 20).join('\n')}`)
}

interface ReadyPayload {
  url: string
  pid: number
}

async function waitForPortRelease(port: number): Promise<void> {
  await new Promise<void>((resolvePromise, reject) => {
    const socket = createConnection({ host: '127.0.0.1', port })
    const timer = setTimeout(() => {
      socket.destroy()
      reject(new Error(`desktop-build: port ${String(port)} did not reject after backend exit`))
    }, 2_000)
    socket.once('connect', () => {
      clearTimeout(timer)
      socket.destroy()
      reject(new Error(`desktop-build: port ${String(port)} still accepts connections after backend exit`))
    })
    socket.once('error', () => {
      clearTimeout(timer)
      resolvePromise()
    })
  })
}

async function stopFailedChild(
  child: ReturnType<typeof spawn>,
  close: Promise<number>,
): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return
  try {
    child.stdin?.write('shutdown\n')
  } catch {
    // The child may have closed stdin while its exit notification is still pending.
  }
  const stopped = await Promise.race([
    close.then(() => true),
    new Promise<false>((resolvePromise) => {
      setTimeout(() => {
        resolvePromise(false)
      }, 8_000)
    }),
  ])
  if (stopped) return
  child.kill('SIGKILL')
  await close
}

async function smokeBackend(output: string, fixture: string): Promise<void> {
  const node = join(output, 'runtime', 'node', 'node.exe')
  const entry = join(output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'desktop-bin.js')
  const workspace = join(fixture, 'backend-workspace')
  const home = join(fixture, 'backend-home')
  await Promise.all([mkdir(workspace, { recursive: true }), mkdir(home, { recursive: true })])
  const env: NodeJS.ProcessEnv = { ...process.env, DSH_HOME: home, DSH_TELEMETRY_DISABLED: '1' }
  delete env.DEEPSEEK_API_KEY
  delete env.DEEPSEEK_BASE_URL
  delete env.NODE_OPTIONS
  const child = spawn(node, [entry], { cwd: workspace, env, stdio: ['pipe', 'pipe', 'pipe'] })
  child.stdout.setEncoding('utf8')
  child.stderr.setEncoding('utf8')
  let stdout = ''
  let stderr = ''
  child.stderr.on('data', (chunk: string) => { stderr += chunk })
  const lines = createInterface({ input: child.stdout, crlfDelay: Infinity })
  const close = new Promise<number>((resolvePromise, reject) => {
    child.once('error', reject)
    child.once('close', (code) => {
      resolvePromise(code ?? -1)
    })
  })
  const ready = new Promise<ReadyPayload>((resolvePromise, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`desktop-build: deployed backend emitted no readiness record\nstdout:\n${stdout}\nstderr:\n${stderr}`))
    }, 90_000)
    lines.on('line', (line) => {
      stdout += `${line}\n`
      if (!line.startsWith('DSH_DESKTOP_READY ')) return
      clearTimeout(timer)
      try {
        resolvePromise(JSON.parse(line.slice('DSH_DESKTOP_READY '.length)) as ReadyPayload)
      } catch (error) {
        reject(error instanceof Error ? error : new Error(String(error)))
      }
    })
    void close.then((code) => {
      clearTimeout(timer)
      reject(new Error(`desktop-build: deployed backend exited ${String(code)} before readiness\nstdout:\n${stdout}\nstderr:\n${stderr}`))
    }, reject)
  })

  try {
    const payload = await ready
    const response = await fetch(payload.url)
    if (response.status !== 200 || !response.headers.get('content-type')?.includes('text/html')) {
      await response.body?.cancel()
      throw new Error(`desktop-build: deployed backend returned HTTP ${String(response.status)}`)
    }
    await response.body?.cancel()
    child.stdin.write('shutdown\n')
    const exitCode = await Promise.race([
      close,
      new Promise<never>((_resolve, reject) => {
        setTimeout(() => {
          reject(new Error('desktop-build: deployed backend did not exit after shutdown'))
        }, 15_000)
      }),
    ])
    const stopped = stdout.split(/\r?\n/u).some(line => line.startsWith('DSH_DESKTOP_STOPPED '))
    if (exitCode !== 0 || !stopped || stderr !== '') {
      throw new Error(`desktop-build: deployed backend failed: exit=${String(exitCode)}, stopped=${String(stopped)}\nstdout:\n${stdout}\nstderr:\n${stderr}`)
    }
    await waitForPortRelease(new URL(payload.url).port === '' ? 80 : Number(new URL(payload.url).port))
    console.log(`desktop-build: deployed backend smoke passed (${payload.url}, pid ${String(payload.pid)})`)
  } catch (error) {
    await stopFailedChild(child, close)
    throw error
  } finally {
    lines.close()
  }
}

async function smokeDesktop(output: string, fixture: string, label = 'portable'): Promise<void> {
  const executable = join(output, executableName)
  const workspace = join(fixture, `${label}-workspace`)
  const home = join(fixture, `${label}-home`)
  const resultPath = join(fixture, `${label}-result.json`)
  await Promise.all([mkdir(workspace, { recursive: true }), mkdir(home, { recursive: true })])
  const env: NodeJS.ProcessEnv = { ...process.env, DSH_HOME: home, DSH_TELEMETRY_DISABLED: '1' }
  delete env.DEEPSEEK_API_KEY
  delete env.DEEPSEEK_BASE_URL
  delete env.NODE_OPTIONS

  const child = spawn(executable, [
    '--workspace', workspace,
    '--runtime', join(output, 'runtime'),
    '--smoke-result', resultPath,
  ], { cwd: workspace, env, stdio: 'ignore' })
  const exitCode = await new Promise<number>((resolvePromise, reject) => {
    const timer = setTimeout(() => {
      child.kill('SIGKILL')
      reject(new Error(`desktop-build: ${label} desktop smoke timed out`))
    }, 120_000)
    child.once('error', reject)
    child.once('exit', (code) => {
      clearTimeout(timer)
      resolvePromise(code ?? -1)
    })
  })
  if (!existsSync(resultPath)) throw new Error(`desktop-build: ${label} desktop smoke wrote no result: ${resultPath}`)
  const result = JSON.parse(await readFile(resultPath, 'utf8')) as {
    Success?: boolean
    WebViewLoaded?: boolean
    GracefulShutdown?: boolean
    Url?: string
    BackendProcessId?: number
    Error?: string
  }
  if (exitCode !== 0 || result.Success !== true || result.WebViewLoaded !== true || result.GracefulShutdown !== true) {
    throw new Error(`desktop-build: ${label} desktop smoke failed (exit ${String(exitCode)}): ${JSON.stringify(result, null, 2)}`)
  }
  if (result.BackendProcessId !== undefined) {
    try {
      process.kill(result.BackendProcessId, 0)
      throw new Error(`desktop-build: ${label} left backend pid ${String(result.BackendProcessId)} running`)
    } catch (error) {
      if (error instanceof Error && !('code' in error && error.code === 'ESRCH')) throw error
    }
  }
  if (result.Url !== undefined) await waitForPortRelease(Number(new URL(result.Url).port))
  console.log(`desktop-build: ${label} WebView lifecycle smoke passed (${result.Url ?? 'no URL'})`)
}

async function copyLegalFiles(output: string): Promise<void> {
  const webView2Package = join(
    process.env.USERPROFILE ?? '',
    '.nuget', 'packages', 'microsoft.web.webview2', webView2Version,
  )
  await Promise.all([
    cp(join(root, 'LICENSE'), join(output, 'LICENSE.txt')),
    cp(join(root, 'THIRD_PARTY_NOTICES.md'), join(output, 'THIRD_PARTY_NOTICES.md')),
    cp(join(root, 'apps', 'desktop', 'README.md'), join(output, 'README.md')),
    cp(join(root, 'apps', 'desktop', 'README.zh.md'), join(output, 'README.zh.md')),
    cp(join(webView2Package, 'LICENSE.txt'), join(output, 'WEBVIEW2_LICENSE.txt')),
    cp(join(webView2Package, 'NOTICE.txt'), join(output, 'WEBVIEW2_NOTICE.txt')),
  ])
  const version = process.versions.node
  let nodeLicense = `Node.js ${version}\nhttps://nodejs.org/\n`
  try {
    const response = await fetch(`https://raw.githubusercontent.com/nodejs/node/v${version}/LICENSE`)
    if (response.ok) nodeLicense = await response.text()
  } catch {
    // The artifact remains usable offline; the distribution still identifies the Node version and project URL.
  }
  await writeFile(join(output, 'NODE_LICENSE.txt'), nodeLicense.endsWith('\n') ? nodeLicense : `${nodeLicense}\n`)
}

async function directorySize(directory: string): Promise<number> {
  let total = 0
  const pending = [directory]
  while (pending.length > 0) {
    const current = pending.pop()
    if (current === undefined) break
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = join(current, entry.name)
      if (entry.isDirectory()) pending.push(path)
      else if (entry.isFile()) total += (await stat(path)).size
    }
  }
  return total
}

async function main(): Promise<void> {
  if (process.platform !== 'win32' || process.arch !== 'x64') {
    throw new Error(`desktop-build: Windows x64 host required, got ${process.platform}-${process.arch}`)
  }
  const options = parseOptions()
  const desktopVersion = await readDesktopVersion()
  assertDescendant(artifactsRoot, options.output, 'output')
  await mkdir(artifactsRoot, { recursive: true })
  const stage = join(artifactsRoot, `.desktop-stage-${randomUUID()}`)
  assertDescendant(artifactsRoot, stage, 'stage')
  let workspaceInstallNeedsRestore = false
  await rm(options.output, { recursive: true, force: true })
  await rm(stage, { recursive: true, force: true })
  await mkdir(stage, { recursive: true })

  try {
    if (!options.skipHarnessBuild) await run('pnpm', ['run', 'build'])
    if (!options.skipTests) {
      await run('dotnet', ['test', desktopTests, '--nologo', '--verbosity', 'minimal'])
      await run(join(root, 'node_modules', '.bin', 'vitest.cmd'), ['run', 'apps/cli/tests/desktop-bin.e2e.spec.ts'], root, {
        ...process.env,
        DSH_REQUIRE_BUILT_DESKTOP_SMOKE: '1',
      })
    }

    const deployedHarness = join(stage, 'harness')
    workspaceInstallNeedsRestore = true
    await run('pnpm', [
      '--filter', 'dsh-desktop-runtime-pkg',
      'deploy', '--legacy', '--prod',
      '--config.node-linker=hoisted',
      '--config.auto-install-peers=false',
      '--config.link-workspace-packages=true',
      deployedHarness,
    ])
    await materializeLinks(deployedHarness)
    const host = join(stage, 'host')
    await run('dotnet', [
      'publish', desktopProject,
      '--configuration', 'Release',
      '--runtime', 'win-x64',
      '--self-contained', 'true',
      '--output', host,
      '--nologo',
    ])

    await mkdir(options.output, { recursive: true })
    await cp(host, options.output, { recursive: true })
    const webView2Loader = join(
      process.env.USERPROFILE ?? '',
      '.nuget', 'packages', 'microsoft.web.webview2', webView2Version,
      'runtimes', 'win-x64', 'native', 'WebView2Loader.dll',
    )
    if (!existsSync(webView2Loader)) {
      throw new Error(`desktop-build: WebView2Loader.dll missing after restore: ${webView2Loader}`)
    }
    await cp(webView2Loader, join(options.output, 'WebView2Loader.dll'))
    await mkdir(join(options.output, 'runtime', 'node'), { recursive: true })
    await cp(process.execPath, join(options.output, 'runtime', 'node', 'node.exe'))
    await mkdir(join(options.output, 'runtime'), { recursive: true })
    await rename(deployedHarness, join(options.output, 'runtime', 'harness'))
    await copyLegalFiles(options.output)
    await verifyLayout(options.output)

    const fixture = join(stage, 'smoke')
    await mkdir(fixture, { recursive: true })
    await smokeBackend(options.output, fixture)
    await smokeDesktop(options.output, fixture)

    const manifest = {
      product: 'DeepSeek Harness Desktop',
      version: desktopVersion,
      harnessVersion: JSON.parse(await readFile(join(root, 'package.json'), 'utf8')) as { version: string },
      nodeVersion: process.versions.node,
      webView2SdkVersion: webView2Version,
      platform: `${process.platform}-${process.arch}`,
      builtAt: new Date().toISOString(),
      executableSha256: await sha256(join(options.output, executableName)),
      backendSha256: await sha256(join(options.output, 'runtime', 'harness', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'desktop-bin.js')),
    }
    await writeFile(join(options.output, 'desktop-manifest.json'), `${JSON.stringify({
      ...manifest,
      harnessVersion: manifest.harnessVersion.version,
    }, null, 2)}\n`)

    if (options.install) {
      await run('pwsh', [
        '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', installScript,
        '-SourceDirectory', options.output,
      ])
      const installed = join(process.env.LOCALAPPDATA ?? '', 'Programs', 'DeepSeek Harness')
      await smokeDesktop(installed, fixture, 'installed')
    }

    const size = await directorySize(options.output)
    console.log('desktop-build: product:')
    console.log(`  ${join(options.output, executableName)}`)
    console.log(`  size ${(size / 1024 / 1024).toFixed(1)} MB`)
    console.log(`  sha256 ${await sha256(join(options.output, executableName))}`)
  } finally {
    await rm(stage, { recursive: true, force: true, maxRetries: 10, retryDelay: 250 })
    if (workspaceInstallNeedsRestore) {
      await run('pnpm', ['install', '--frozen-lockfile', '--prod=false'], root, {
        ...process.env,
        CI: 'true',
      })
    }
  }
}

await main()
