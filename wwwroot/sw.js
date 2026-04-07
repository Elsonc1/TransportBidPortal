/**
 * Service worker conservador (Fase C PWA).
 * - APIs /api/* : sempre rede, sem cache de resposta (evita JWT/dados sensiveis offline).
 * - Documentos (SPA): rede primeiro, fallback para shell em cache (offline).
 * - Demais origem local: cache-first para assets estaticos listados em PRECACHE + mesma origem.
 *
 * Ao alterar app.js, styles.css, manifest ou icones: subir CACHE_VERSION e ?v= em index.html.
 */
const CACHE_VERSION = "portal-shell-12";
const CACHE_NAME = `transport-bid-portal-${CACHE_VERSION}`;

const PRECACHE_URLS = [
  "/index.html",
  "/styles.css",
  "/app.js?v=12",
  "/manifest.webmanifest",
  "/icons/icon-192.png",
  "/icons/icon-512.png"
];

function isSameOrigin(url) {
  try {
    return new URL(url).origin === self.location.origin;
  } catch {
    return false;
  }
}

function isApiRequest(url) {
  try {
    const p = new URL(url).pathname;
    return p.startsWith("/api/");
  } catch {
    return false;
  }
}

function isNavigateRequest(request) {
  return request.mode === "navigate" || request.destination === "document";
}

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll(PRECACHE_URLS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((k) => k !== CACHE_NAME && k.startsWith("transport-bid-portal-")).map((k) => caches.delete(k)))
      )
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const req = event.request;
  const url = req.url;

  if (!isSameOrigin(url)) {
    return;
  }

  if (isApiRequest(url)) {
    event.respondWith(fetch(req));
    return;
  }

  if (isNavigateRequest(req)) {
    event.respondWith(
      fetch(req).catch(async () => {
        const cache = await caches.open(CACHE_NAME);
        const cached = await cache.match("/index.html");
        if (cached) {
          return cached;
        }
        return new Response("Offline", { status: 503, headers: { "Content-Type": "text/plain; charset=utf-8" } });
      })
    );
    return;
  }

  event.respondWith(
    caches.open(CACHE_NAME).then(async (cache) => {
      const cached = await cache.match(req);
      if (cached) {
        return cached;
      }
      const res = await fetch(req);
      if (res.ok && req.method === "GET") {
        try {
          await cache.put(req, res.clone());
        } catch {
          /* ignore quota / opaqueredirect */
        }
      }
      return res;
    })
  );
});
