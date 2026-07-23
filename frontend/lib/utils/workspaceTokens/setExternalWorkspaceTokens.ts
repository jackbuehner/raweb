/**
 * Encodes and stores the external workspace tokens in localStorage.
 * The tokens are encrypted using AES-GCM with the provided encryption key.
 */
export async function setExternalWorkspaceTokens(
  tokens: { endpoint: string; username: string; password: string; name: string }[],
  encryptionKey: string
): Promise<void> {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const encodedTokens = new TextEncoder().encode(JSON.stringify(tokens));

  const hashedInputKey = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(encryptionKey)); // AES-GCM requires 128 or 256 bit keys
  const key = await crypto.subtle.importKey('raw', hashedInputKey, { name: 'AES-GCM' }, false, ['encrypt']);

  const encryptedTokens = await crypto.subtle.encrypt(
    {
      name: 'AES-GCM',
      iv,
    },
    key,
    encodedTokens
  );

  if (!Uint8Array.prototype.toBase64) {
    throw new Error(
      'Uint8Array.toBase64 is not defined. Please ensure that the polyfill for Uint8Array.toBase64 is included.'
    );
  }

  // the IV must be persisted alongside the ciphertext since it is required to decrypt it later
  const combined = new Uint8Array(iv.length + encryptedTokens.byteLength);
  combined.set(iv, 0);
  combined.set(new Uint8Array(encryptedTokens), iv.length);

  const base64EncryptedTokens = combined.toBase64!();

  localStorage.setItem('externalWorkspaceTokens', base64EncryptedTokens);
}
