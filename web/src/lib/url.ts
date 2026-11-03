/** Only the schemes the web client may navigate to; everything else is unsafe. */
const ALLOWED_SCHEMES = ['https:', 'http:', 'steam:']

/** Returns the URL when its scheme is safe for direct navigation, otherwise null. */
export function safeExternalUrl(value: string | null | undefined): string | null {
  if (!value) {
    return null
  }

  try {
    const url = new URL(value)
    return ALLOWED_SCHEMES.includes(url.protocol) ? value : null
  } catch {
    return null
  }
}
