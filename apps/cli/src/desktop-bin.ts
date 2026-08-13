#!/usr/bin/env node
/**
 * Desktop-only process adapter over the shipped Web profile. It owns no model,
 * session, or tool behavior: the native Windows host starts this entry with a
 * private stdio channel, waits for its loopback readiness record, then writes
 * `shutdown` so the profile's bounded disposer reaches quiescence.
 * @module @deepseek-ai/dsh/desktop-bin
 */

/* v8 ignore file -- the packaged desktop lifecycle smoke exercises this self-executing adapter. */

import { createInterface } from 'node:readline'
import { loadLayeredEnv } from '@deepseek-ai/dsh-app-boot'
import type {} from '@deepseek-ai/dsh-host-webserver'
import { runProfile } from './profile-boot.ts'

const READY_PREFIX = 'DSH_DESKTOP_READY '
const STOPPED_PREFIX = 'DSH_DESKTOP_STOPPED '

async function main(): Promise<void> {
  const { ctx, shutdown } = await runProfile({
    environment: loadLayeredEnv('dsh-desktop'),
    profile: 'web',
    patchFiles: [],
    args: ['--host', '127.0.0.1', '--port', '0'],
  })
  const server = ctx.get('webServer')
  if (server === undefined) throw new Error('dsh-desktop: web profile activated without webServer')

  console.log(`${READY_PREFIX}${JSON.stringify({
    url: `http://127.0.0.1:${String(server.port)}/`,
    pid: process.pid,
  })}`)

  const input = createInterface({ input: process.stdin, crlfDelay: Infinity })
  let stopping: Promise<void> | undefined
  const stop = (reason: 'command' | 'stdin-closed'): Promise<void> => {
    stopping ??= (async () => {
      await shutdown.shutdown(0)
      console.log(`${STOPPED_PREFIX}${JSON.stringify({ reason, pid: process.pid })}`)
      input.close()
    })()
    return stopping
  }

  input.on('line', (line) => {
    if (line === 'shutdown') {
      void stop('command')
      return
    }
    console.error(`dsh-desktop: ignored unknown control command ${JSON.stringify(line)}`)
  })
  input.on('close', () => {
    if (stopping === undefined) void stop('stdin-closed')
  })
}

try {
  await main()
} catch (error) {
  console.error(`dsh-desktop: ${error instanceof Error ? error.stack ?? error.message : String(error)}`)
  process.exitCode = 1
}
