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
