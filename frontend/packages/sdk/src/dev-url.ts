export function resolveDevApiBase(
  fallback: string,
  location: Pick<Location, 'search' | 'hostname'> = globalThis.location,
): string {
  const override = new URLSearchParams(location.search).get('apiBase');
  if (override) {
    return override;
  }
  try {
    const url = new URL(fallback);
    if (location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
      url.hostname = location.hostname;
    }
    return url.origin;
  } catch {
    return fallback;
  }
}
