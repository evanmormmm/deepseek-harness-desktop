/**
 * Built-entry lifecycle smoke for the Windows desktop process adapter. The
 * test owns an isolated Harness home, waits for the adapter's trusted
 * loopback record, requests the real Web shell, then asks the adapter to
 * dispose and verifies the listener is gone.
 */

import { spawn } from 'node:child_process'
import { createConnection } from 'node:net'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'

const builtDesktopBin = fileURLToPath(new URL('../lib/desktop-bin.js', import.meta.url))
const requireBuiltDesktop = process.env.DSH_REQUIRE_BUILT_DESKTOP_SMOKE === '1'
const fixtures: string[] = []

afterEach(async () => {
  await Promise.all(fixtures.splice(0).map(path => rm(path, { recursive: true, force: true })))
})

/** Resolve once no process accepts a TCP connection on the released port. */
async function expectPortReleased(port: number): Promise<void> {
  await new Promise<void>((resolve, reject) => {
    const socket = createConnection({ host: '127.0.0.1', port })
    const timer = setTimeout(() => {
      socket.destroy()
      reject(new Error(`desktop-bin: port ${String(port)} did not reject a post-exit connection`))
    }, 2_000)
    socket.once('connect', () => {
      clearTimeout(timer)
      socket.destroy()
      reject(new Error(`desktop-bin: port ${String(port)} still accepts connections after exit`))
    })
    socket.once('error', () => {
      clearTimeout(timer)
      resolve()
    })
  })
}

describe.skipIf(!requireBuiltDesktop)('built desktop process adapter', () => {
  it('serves the Web shell and reaches quiescence through its private control channel', async () => {
    const fixture = await mkdtemp(join(tmpdir(), 'dsh-desktop-bin-'))
    fixtures.push(fixture)
    const workspace = join(fixture, 'workspace')
    const home = join(fixture, 'home')
    await Promise.all([
      import('node:fs/promises').then(({ mkdir }) => mkdir(workspace, { recursive: true })),
      import('node:fs/promises').then(({ mkdir }) => mkdir(home, { recursive: true })),
    ])

    const env: NodeJS.ProcessEnv = {
      ...process.env,
      DSH_HOME: home,
      DSH_TELEMETRY_DISABLED: '1',
    }
    delete env.NODE_OPTIONS
    const child = spawn(process.execPath, [builtDesktopBin], {
      cwd: workspace,
      env,
      stdio: ['pipe', 'pipe', 'pipe'],
    })
    child.stdout.setEncoding('utf8')
    child.stderr.setEncoding('utf8')
    let stdout = ''
    let stderr = ''
    let readyUrl: URL | undefined
    let status: number | undefined
    let contentType: string | null | undefined
    let stopped = false

    const completed = new Promise<number>((resolve, reject) => {
      const timer = setTimeout(() => {
        child.kill('SIGKILL')
        reject(new Error(`desktop-bin did not complete within 90s\nstdout:\n${stdout}\nstderr:\n${stderr}`))
      }, 90_000)
      child.stderr.on('data', (chunk: string) => { stderr += chunk })
      child.stdout.on('data', (chunk: string) => {
        stdout += chunk
        for (const line of stdout.split(/\r?\n/u)) {
          if (line.startsWith('DSH_DESKTOP_STOPPED ')) stopped = true
          if (readyUrl !== undefined || !line.startsWith('DSH_DESKTOP_READY ')) continue
          const payload = JSON.parse(line.slice('DSH_DESKTOP_READY '.length)) as { url: string; pid: number }
          readyUrl = new URL(payload.url)
          expect(payload.pid).toBe(child.pid)
          void fetch(readyUrl).then(async (response) => {
            status = response.status
            contentType = response.headers.get('content-type')
            await response.body?.cancel()
            child.stdin.write('shutdown\n')
          }, reject)
        }
      })
      child.once('error', reject)
      child.once('close', (code) => {
        clearTimeout(timer)
        resolve(code ?? -1)
      })
    })

    const code = await completed
    expect(code).toBe(0)
    expect(readyUrl?.hostname).toBe('127.0.0.1')
    expect(status).toBe(200)
    expect(contentType).toContain('text/html')
    expect(stopped).toBe(true)
    expect(stderr).toBe('')
    await expectPortReleased(readyUrl!.port === '' ? 80 : Number(readyUrl!.port))
  }, 100_000)
})
