import { defineConfig } from 'tsdown'

/**
 * The dsh CLI ships its public `bin` and the private desktop process adapter.
 * The root tsdown builds only `lib/types/index.js`, so this override points at
 * both emitted entries; each reachable mode module bundles with its owner.
 * Declarations come from `tsc -b` (dts: false), matching every package.
 */
export default defineConfig({
  entry: ['lib/types/bin.js', 'lib/types/desktop-bin.js'],
  outDir: 'lib',
  format: ['esm'],
  platform: 'node',
  target: 'es2024',
  fixedExtension: false,
  dts: false,
  clean: false,
})
