import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API base defaults to same-origin so both dev (proxy below) and production
// (nginx reverse proxy) call the API without cross-origin requests.
const apiTarget = process.env.API_ORIGIN ?? 'http://localhost:5077'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
    },
  },
  preview: {
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
    },
  },
})
