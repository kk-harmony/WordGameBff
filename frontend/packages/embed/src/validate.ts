export function validateApiBase(apiBase: string): void {
  let url: URL;
  try {
    url = new URL(apiBase);
  } catch {
    throw new Error('api-base must be a valid URL.');
  }

  if (url.protocol === 'http:') {
    if (!isHttpAllowedHost(url.hostname)) {
      throw new Error('api-base must use HTTPS in production (http:// allowed only for localhost and private networks).');
    }
  } else if (url.protocol !== 'https:') {
    throw new Error('api-base must use HTTPS.');
  }
}

function isHttpAllowedHost(hostname: string): boolean {
  const host = hostname.toLowerCase();
  if (host === 'localhost' || host === '127.0.0.1' || host === '[::1]') {
    return true;
  }
  if (host.endsWith('.local')) {
    return true;
  }
  return isPrivateNetworkHost(host);
}

function isPrivateNetworkHost(hostname: string): boolean {
  const ipv4Match = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(hostname);
  if (!ipv4Match) {
    return false;
  }
  const octets = ipv4Match.slice(1, 5).map((part) => Number(part));
  if (octets.some((octet) => Number.isNaN(octet) || octet < 0 || octet > 255)) {
    return false;
  }
  const [a, b] = octets;
  if (a === 10) {
    return true;
  }
  if (a === 172 && b >= 16 && b <= 31) {
    return true;
  }
  if (a === 192 && b === 168) {
    return true;
  }
  return false;
}
