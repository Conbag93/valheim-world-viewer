// Browser storage for WorldStore: imported worlds, their saved views and rendered terrain tiles,
// kept in IndexedDB so a world survives reloads without picking its folder again.
const DB_NAME = 'valheim-world-viewer';
const STORES = ['summaries', 'worlds', 'views', 'tiles'];
let dbPromise;

function db() {
    dbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, 1);
        request.onupgradeneeded = () => {
            for (const name of STORES)
                if (!request.result.objectStoreNames.contains(name)) request.result.createObjectStore(name);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    return dbPromise;
}

async function run(store, mode, work) {
    const tx = (await db()).transaction(store, mode);
    const result = work(tx.objectStore(store));
    return new Promise((resolve, reject) => {
        tx.oncomplete = () => resolve(result.result);
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error);
    });
}

export const get = (store, key) => run(store, 'readonly', s => s.get(key)).then(v => v ?? null);
export const put = (store, key, value) => run(store, 'readwrite', s => s.put(value, key)).then(() => undefined);
export const remove = (store, key) => run(store, 'readwrite', s => s.delete(key)).then(() => undefined);
export const values = store => run(store, 'readonly', s => s.getAll());

// Deletes every key starting with prefix (e.g. all of a world's tiles).
export const removePrefix = (store, prefix) =>
    run(store, 'readwrite', s => s.delete(IDBKeyRange.bound(prefix, prefix + '￿'))).then(() => undefined);

// Terrain tiles: RGBA pixels from .NET become a PNG blob (stored for next time) and an object URL.
const urls = new Map();

export async function storedTileUrl(key) {
    if (urls.has(key)) return urls.get(key);
    const blob = await get('tiles', key);
    return blob ? remember(key, blob) : null;
}

export async function tileUrl(key, rgba, size) {
    const canvas = typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(size, size) : Object.assign(document.createElement('canvas'), { width: size, height: size });
    canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(rgba.buffer, rgba.byteOffset, rgba.byteLength), size, size), 0, 0);
    const blob = canvas.convertToBlob ? await canvas.convertToBlob({ type: 'image/png' }) : await new Promise(r => canvas.toBlob(r, 'image/png'));
    await put('tiles', key, blob);
    return remember(key, blob);
}

function remember(key, blob) {
    const url = URL.createObjectURL(blob);
    urls.set(key, url);
    return url;
}
