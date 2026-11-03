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
  // Baked into the bundle as process.env.TARO_APP_* so the shared API layer can
  // resolve the origin at runtime. Local devtools use 不校验合法域名 and point at
  // the host loopback; CI / production must override TARO_APP_API_BASE.
  env: {
    TARO_APP_API_BASE: JSON.stringify(process.env.TARO_APP_API_BASE ?? 'http://127.0.0.1:8082')
  },
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
