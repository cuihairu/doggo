import path from 'node:path'
import { defineConfig } from '@tarojs/cli'

export default defineConfig({
  projectName: 'doggo-miniapp',
  date: '2026-10-11',
  designWidth: 750,
  deviceRatio: {
    640: 2.34 / 2,
    750: 1,
    828: 1.81 / 2,
    375: 2
  },
  sourceRoot: 'src',
  outputRoot: 'dist',
  plugins: [],
  defineConstants: {},
  framework: 'react',
  compiler: 'webpack5',
  cache: { enable: false },
  mini: {
    // Compile the linked shared api layer (@doggo/shared) with the project babel config.
    // Note: Taro resets __dirname when loading this file, so anchor on cwd.
    compile: {
      include: [path.resolve(process.cwd(), '..', 'shared', 'src')]
    },
    // Imports inside shared resolve node modules from this project. Keep the
    // relative 'node_modules' entry: setting the list drops webpack's default walk-up.
    webpackChain(chain: { resolve: { modules: { add: (p: string) => void } } }) {
      chain.resolve.modules.add(path.resolve(process.cwd(), 'node_modules'))
      chain.resolve.modules.add('node_modules')
    },
    postcss: {
      pxtransform: { enable: true, config: {} },
      cssModules: { enable: false }
    }
  },
  h5: {}
})
