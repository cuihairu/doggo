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

module.exports = (async function () {
  return mergeConfig(getDefaultConfig(__dirname), await getMetroConfig(), config)
})()
