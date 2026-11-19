export interface LatLng {
  latitude: number;
  longitude: number;
}

const NUM = String.raw`(-?\d{1,3}(?:\.\d+)?)`;

// Ordered by accuracy: !3d/!4d is the actual pin; @ is only the map's centre.
const PATTERNS = [
  new RegExp(`!3d${NUM}!4d${NUM}`),
  new RegExp(`[?&](?:q|query|ll|destination|daddr)=${NUM},\\s*${NUM}`),
  new RegExp(`@${NUM},${NUM}`),
  new RegExp(`^\\s*${NUM}\\s*,\\s*${NUM}\\s*$`), // plain "14.676, 121.043"
];

export function parseMapsCoordinates(input: string): LatLng | null {
  let text = input.trim();
  try {
    text = decodeURIComponent(text);
  } catch {
    // keep the raw text if it isn't valid percent-encoding
  }

  for (const pattern of PATTERNS) {
    const match = pattern.exec(text);
    if (!match) continue;
    const latitude = Number(match[1]);
    const longitude = Number(match[2]);
    if (Math.abs(latitude) <= 90 && Math.abs(longitude) <= 180) {
      return { latitude, longitude };
    }
  }
  return null;
}

/** Short share links redirect server-side, so the browser can't read their coordinates. */
export function isShortMapsLink(input: string): boolean {
  return /(maps\.app\.goo\.gl|goo\.gl\/maps)/i.test(input);
}
