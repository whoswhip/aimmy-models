const SHA1_LENGTH = 20;
const TIMESTAMP_LENGTH = 8;
const LONG_LENGTH = 8;
const HAS_METADATA_FLAG = 0x01;

const textDecoder = new TextDecoder('utf-8');

function bytesToHex(bytes) {
  return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
}

function readInt64LE(view, offset) {
  if (typeof view.getBigInt64 === 'function') {
    return view.getBigInt64(offset, true);
  }
  const low = BigInt(view.getUint32(offset, true));
  const high = BigInt(view.getInt32(offset + 4, true));
  return (high << 32n) | low;
}

function parseEntry(view, offset) {
  const totalLen = view.byteLength;
  if (offset >= totalLen) throw new Error('Offset out of range');

  if (offset + 1 + SHA1_LENGTH + TIMESTAMP_LENGTH > totalLen) {
    throw new Error('Data is too short to contain an entry header');
  }

  const nameLen = view.getUint8(offset);
  if (nameLen === 0) throw new Error('Invalid name length 0');

  const minSize = 1 + nameLen + SHA1_LENGTH + TIMESTAMP_LENGTH;
  if (offset + minSize > totalLen) throw new Error('Data is too short for declared name length');

  let p = offset + 1;
  const nameBytes = new Uint8Array(view.buffer, view.byteOffset + p, nameLen);
  const fileName = textDecoder.decode(nameBytes);
  p += nameLen;

  const sha1Bytes = new Uint8Array(view.buffer, view.byteOffset + p, SHA1_LENGTH);
  const sha1Hex = bytesToHex(sha1Bytes);
  p += SHA1_LENGTH;

  const timestamp = readInt64LE(view, p);
  p += TIMESTAMP_LENGTH;

  if (p >= totalLen) {
    return {
      entry: {
        fileName,
        sha1: sha1Hex,
        sha1Bytes: new Uint8Array(sha1Bytes),
        timestamp,
        messageMetadata: false,
        serverID: null,
        channelID: null,
        messageID: null
      },
      nextOffset: p
    };
  }
  const flags = view.getUint8(p);
  p += 1;

  let messageMetadata = false;
  let serverID = null, channelID = null, messageID = null;

  if ((flags & HAS_METADATA_FLAG) !== 0) {
    if (p + LONG_LENGTH * 3 > totalLen) {
      throw new Error('Data indicates message metadata but file is truncated');
    }
    serverID = readInt64LE(view, p); p += LONG_LENGTH;
    channelID = readInt64LE(view, p); p += LONG_LENGTH;
    messageID = readInt64LE(view, p); p += LONG_LENGTH;
    messageMetadata = true;
  }

  return {
    entry: {
      fileName,
      sha1: sha1Hex,
      sha1Bytes: new Uint8Array(sha1Bytes),
      timestamp,
      messageMetadata,
      serverID,
      channelID,
      messageID
    },
    nextOffset: p
  };
}

function parseMetadata(arrayBuffer) {
  const view = new DataView(arrayBuffer);
  const entries = [];
  let offset = 0;
  while (offset < view.byteLength) {
    try {
      if (offset + 1 + SHA1_LENGTH + TIMESTAMP_LENGTH > view.byteLength) {
        break;
      }
      const nameLen = view.getUint8(offset);
      if (nameLen === 0) break;
      if (offset + 1 + nameLen + SHA1_LENGTH + TIMESTAMP_LENGTH > view.byteLength) break;

      const { entry, nextOffset } = parseEntry(view, offset);
      entries.push(entry);
      if (nextOffset <= offset) break;
      offset = nextOffset;
    } catch (err) {
      console.warn('Stopping parse due to error:', err);
      break;
    }
  }
  return entries;
}

async function fetchAndParseMetadata(url) {
  const resp = await fetch(url, { method: 'GET' });
  if (!resp.ok) throw new Error(`Failed to fetch metadata: ${resp.status} ${resp.statusText}`);
  const buf = await resp.arrayBuffer();
  return parseMetadata(buf);
}