import { defineConfig } from '@tarojs/cli'

export default defineConfig({
  projectName: 'doggo-mobile',
  date: '2026-10-12',
  designWidth: 750,
  deviceRatio: {
    640: 2.34 / 2,
    750: 1,
    828: 1.81 / 2,
    375: 2
  },
  sourceRoot: 'src',
  outputRoot: 'dist',
  // Baked into the bundle as process.env.TARO_APP_* (rn-supporter getEnv).
  // CI overrides the value at the job level to go through adb reverse.
  env: {
    TARO_APP_API_BASE: JSON.stringify(process.env.TARO_APP_API_BASE ?? 'http://10.0.2.2:5077')
  },
  plugins: [],
  defineConstants: {},
  framework: 'react',
  compiler: 'webpack5',
  cache: { enable: false },
  rn: {
    // AppRegistry name inside the JS bundle must match the expo-generated
    // MainActivity getMainComponentName ("main") or launch dies with
    // "Invariant Violation: main has not been registered".
    appName: 'main',
    designWidth: 750,
    deviceRatio: {
      640: 2.34 / 2,
      750: 1,
      828: 1.81 / 2,
      375: 2
    }
  }
})
