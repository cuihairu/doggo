const path = require('node:path')
const { getDefaultConfig, mergeConfig } = require('@react-native/metro-config')
const { getMetroConfig } = require('@tarojs/rn-supporter')

/**
 * Metro configuration
 * https://facebook.github.io/metro/docs/configuration
 *
 * @type {import('metro-config').MetroConfig}
 */
const sharedPath = path.resolve(__dirname, '..', 'shared')
const nodeModules = path.resolve(__dirname, 'node_modules')
const config = {
  watchFolders: [sharedPath],
  resolver: {
    extraNodeModules: {
      '@doggo/shared': sharedPath,
      // Imports rewritten inside the shared layer (Taro member access becomes
      // direct taro-rn subpath imports) resolve from here.
      '@tarojs/taro': path.join(nodeModules, '@tarojs/taro'),
      '@tarojs/taro-rn': path.join(nodeModules, '@tarojs/taro-rn'),
      '@tarojs/components-rn': path.join(nodeModules, '@tarojs/components-rn'),
      '@tarojs/runtime-rn': path.join(nodeModules, '@tarojs/runtime-rn'),
      '@tarojs/rn-supporter': path.join(nodeModules, '@tarojs/rn-supporter'),
      '@tarojs/react': path.join(nodeModules, '@tarojs/react')
    }
  }
}

// expo export:embed (the gradle JS bundle step) requires hashed asset names,
// which come from the expo-asset plugin. The plain RN config does not enable
// it, so only add it when running under expo's export pipeline.
if (process.argv.some((a) => String(a).includes('export:embed'))) {
  config.transformer = config.transformer || {}
  config.transformer.assetPlugins = [
    ...(config.transformer.assetPlugins || []),
    require.resolve('expo-asset/tools/hashAssetFiles')
  ]
}

module.exports = (async function () {
  return mergeConfig(getDefaultConfig(__dirname), await getMetroConfig(), config)
})()
